namespace ClaudeVS
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Win32.SafeHandles;

    /// <summary>
    /// Manages a ConPTY (Windows Pseudo-Console) session for terminal emulation.
    /// Based on Microsoft's official GUIConsole sample implementation.
    /// </summary>
    public class ConPtyTerminal : IDisposable
    {
        // ConPTY API P/Invoke declarations using SafeFileHandle
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern int CreatePseudoConsole(
            COORD size,
            SafeFileHandle hInput,
            SafeFileHandle hOutput,
            uint dwFlags,
            out IntPtr hPC);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ClosePseudoConsole(IntPtr hPC);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern int ResizePseudoConsole(IntPtr hPC, COORD size);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CreatePipe(
            out SafeFileHandle hReadPipe,
            out SafeFileHandle hWritePipe,
            IntPtr lpPipeAttributes,
            uint nSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetHandleInformation(
            SafeFileHandle hObject,
            uint dwMask,
            uint dwFlags);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(SafeFileHandle hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool UpdateProcThreadAttribute(
            IntPtr lpAttributeList,
            uint dwFlags,
            IntPtr Attribute,
            IntPtr lpValue,
            IntPtr cbSize,
            IntPtr lpPreviousValue,
            IntPtr lpReturnSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool InitializeProcThreadAttributeList(
            IntPtr lpAttributeList,
            int dwAttributeCount,
            int dwFlags,
            ref IntPtr lpSize);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CreateProcessW(
            string lpApplicationName,
            string lpCommandLine,
            ref SECURITY_ATTRIBUTES lpProcessAttributes,
            ref SECURITY_ATTRIBUTES lpThreadAttributes,
            bool bInheritHandles,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            string lpCurrentDirectory,
            ref STARTUPINFOEX lpStartupInfo,
            out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint FormatMessage(
            uint dwFlags,
            IntPtr lpSource,
            uint dwMessageId,
            uint dwLanguageId,
            StringBuilder lpBuffer,
            uint nSize,
            IntPtr Arguments);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetConsoleMode(SafeFileHandle hConsoleHandle, out uint lpMode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleMode(SafeFileHandle hConsoleHandle, uint dwMode);

        private const uint FORMAT_MESSAGE_FROM_SYSTEM = 0x00001000;
        private const uint FORMAT_MESSAGE_IGNORE_INSERTS = 0x00000200;

        // Console mode flags
        private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;
        private const uint DISABLE_NEWLINE_AUTO_RETURN = 0x0008;

        [StructLayout(LayoutKind.Sequential)]
        private struct COORD
        {
            public short X;
            public short Y;
        }

        // STARTUPINFO struct with proper marshaling for string fields
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct STARTUPINFO
        {
            public int cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public int dwX;
            public int dwY;
            public int dwXSize;
            public int dwYSize;
            public int dwXCountChars;
            public int dwYCountChars;
            public int dwFillAttribute;
            public int dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        // STARTUPINFOEX extends STARTUPINFO with attribute list pointer
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct STARTUPINFOEX
        {
            public STARTUPINFO StartupInfo;
            public IntPtr lpAttributeList;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SECURITY_ATTRIBUTES
        {
            public int nLength;
            public IntPtr lpSecurityDescriptor;
            public int bInheritHandle;
        }

        private const uint STARTF_USESTDHANDLES = 0x00000100;
        private const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
        private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
        private const uint CREATE_NO_WINDOW = 0x08000000;
        private const uint PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;
        private const uint HANDLE_FLAG_INHERIT = 1;
        private const uint PSEUDOCONSOLE_INHERIT_CURSOR = 0x1;

        /// <summary>
        /// Gets the human-readable error message for a Windows error code.
        /// </summary>
        private static string GetWindowsErrorMessage(uint errorCode)
        {
            StringBuilder messageBuffer = new StringBuilder(256);
            uint result = FormatMessage(
                FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS,
                IntPtr.Zero,
                errorCode,
                0,
                messageBuffer,
                (uint)messageBuffer.Capacity,
                IntPtr.Zero);

            if (result > 0)
            {
                return messageBuffer.ToString().TrimEnd('\r', '\n');
            }
            return $"Unknown error (0x{errorCode:X8})";
        }

        private IntPtr pseudoConsoleHandle = IntPtr.Zero;
        private IntPtr shellProcessHandle = IntPtr.Zero;  // Keep process handle open
        private SafeFileHandle inputReadPipe;
        private SafeFileHandle inputWritePipe;
        private SafeFileHandle outputReadPipe;
        private SafeFileHandle outputWritePipe;

        private static readonly object debugFileLock = new object();
        private FileStream outputStream;
        private CancellationTokenSource cancellationTokenSource;
        private Task outputReadingTask;

        public event EventHandler<string> OutputReceived;
        public event EventHandler<int> ProcessExited;

        private static void SafeWriteDebugFile(string message)
        {
            try
            {
                lock (debugFileLock)
                {
                    System.IO.File.AppendAllText(@"c:\temp\conpty-debug.txt", message);
                }
            }
            catch
            {
                // Ignore file write errors
            }
        }

        public ushort Rows { get; private set; } = 30;
        public ushort Columns { get; private set; } = 120;
        public bool IsRunning { get; private set; }

        public ConPtyTerminal(ushort rows = 30, ushort columns = 120)
        {
            Rows = rows;
            Columns = columns;
        }

        public bool Initialize(string workingDirectory = null)
        {
            try
            {
                SafeWriteDebugFile("=== Initialize called ===\n");
                cancellationTokenSource = new CancellationTokenSource();

                // Create pipes for input/output
                if (!CreateConsolePipes())
                {
                    System.Diagnostics.Debug.WriteLine("Failed to create console pipes");
                    SafeWriteDebugFile("FAILED: CreateConsolePipes\n");
                    return false;
                }
                SafeWriteDebugFile("SUCCESS: CreateConsolePipes\n");

                // Create the pseudo-console
                if (!CreatePseudoConsoleSession())
                {
                    System.Diagnostics.Debug.WriteLine("Failed to create pseudo-console session");
                    SafeWriteDebugFile("FAILED: CreatePseudoConsoleSession\n");
                    return false;
                }
                SafeWriteDebugFile("SUCCESS: CreatePseudoConsoleSession\n");

                // Start the shell process (cmd.exe)
                if (!StartShellProcess(workingDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)))
                {
                    System.Diagnostics.Debug.WriteLine("Failed to start shell process");
                    SafeWriteDebugFile("FAILED: StartShellProcess\n");
                    return false;
                }
                SafeWriteDebugFile("SUCCESS: StartShellProcess - terminal should be working!\n");

                IsRunning = true;

                // NOW create the output stream from outputReadPipe after everything is set up
                System.Diagnostics.Debug.WriteLine($"Creating outputStream from outputReadPipe handle: {outputReadPipe?.DangerousGetHandle()}");
                SafeWriteDebugFile($"Creating outputStream from handle: {outputReadPipe?.DangerousGetHandle()}\n");
                outputStream = new FileStream(outputReadPipe, FileAccess.Read, 4096, false);
                System.Diagnostics.Debug.WriteLine("outputStream created successfully");
                SafeWriteDebugFile("outputStream created successfully\n");

                // Start reading output using synchronous reads in background thread
                System.Diagnostics.Debug.WriteLine($"About to start ReadOutputSync task. outputStream null? {outputStream == null}");
                SafeWriteDebugFile($"About to start ReadOutputSync. outputStream null? {outputStream == null}\n");
                outputReadingTask = Task.Run(() => ReadOutputSync(cancellationTokenSource.Token));
                System.Diagnostics.Debug.WriteLine($"ReadOutputSync task started. Task status: {outputReadingTask?.Status}");
                SafeWriteDebugFile($"ReadOutputSync task started. Status: {outputReadingTask?.Status}\n");

                // Resize the pseudoconsole to "wake it up" and trigger output
                // This is a known requirement to start ConPTY output flowing
                System.Diagnostics.Debug.WriteLine($"Calling ResizePseudoConsole to wake up ConPTY output");
                SafeWriteDebugFile($"Calling ResizePseudoConsole with size {Columns}x{Rows}\n");
                COORD consoleSize = new COORD { X = (short)Columns, Y = (short)Rows };
                int resizeResult = ResizePseudoConsole(pseudoConsoleHandle, consoleSize);
                System.Diagnostics.Debug.WriteLine($"ResizePseudoConsole result: {resizeResult}");
                SafeWriteDebugFile($"ResizePseudoConsole result: {resizeResult}\n");

                // Claude is launched directly via PowerShell args
                SafeWriteDebugFile("Claude launched via PowerShell args\n");

                //BOOKMARK: 2025-11-12 18:58:08 uncomment
                //Task.Delay(3000).ContinueWith(_ =>
                //{
                //    SafeWriteDebugFile("Sending 'hello' to terminal after 3 second delay...\n");
                //    WriteInput("list files");

                //    // Send Enter as raw CR (0x0D) - what PowerShell/ConPTY expects
                //    if (inputWritePipe != null && !inputWritePipe.IsClosed)
                //    {
                //        if (inputStream == null)
                //        {
                //            inputStream = new FileStream(inputWritePipe, FileAccess.Write, 1024, false);
                //        }
                //        byte[] enterKey = new byte[] { 0x0D }; // Just CR
                //        inputStream.Write(enterKey, 0, enterKey.Length);
                //        inputStream.Flush();
                //        SafeWriteDebugFile("Sent Enter key (0x0D) after hello\n");
                //    }
                //});

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ConPtyTerminal.Initialize failed: {ex.Message}");
                Cleanup();
                return false;
            }
        }

        private bool CreateConsolePipes()
        {
            try
            {
                // Create input pipe (stdin)
                if (!CreatePipe(out inputReadPipe, out inputWritePipe, IntPtr.Zero, 0))
                {
                    System.Diagnostics.Debug.WriteLine($"CreatePipe for input failed: {Marshal.GetLastWin32Error()}");
                    return false;
                }

                System.Diagnostics.Debug.WriteLine("Input pipes created successfully");

                // Make the write handle (parent's side) non-inheritable - child only reads from stdin
                if (!SetHandleInformation(inputWritePipe, HANDLE_FLAG_INHERIT, 0))
                {
                    System.Diagnostics.Debug.WriteLine($"SetHandleInformation for inputWritePipe (parent) failed: {Marshal.GetLastWin32Error()}");
                    return false;
                }

                // Create output pipe (stdout/stderr)
                if (!CreatePipe(out outputReadPipe, out outputWritePipe, IntPtr.Zero, 0))
                {
                    System.Diagnostics.Debug.WriteLine($"CreatePipe for output failed: {Marshal.GetLastWin32Error()}");
                    return false;
                }

                System.Diagnostics.Debug.WriteLine($"Output pipes created: outputReadPipe={outputReadPipe.DangerousGetHandle()}, outputWritePipe={outputWritePipe.DangerousGetHandle()}");
                SafeWriteDebugFile($"Output pipes: read={outputReadPipe.DangerousGetHandle()}, write={outputWritePipe.DangerousGetHandle()}\n");

                // Make the read handle (parent's side) non-inheritable - parent only reads stdout
                if (!SetHandleInformation(outputReadPipe, HANDLE_FLAG_INHERIT, 0))
                {
                    System.Diagnostics.Debug.WriteLine($"SetHandleInformation for outputReadPipe (parent) failed: {Marshal.GetLastWin32Error()}");
                    return false;
                }

                // DON'T create the file stream yet - wait until after ConPTY is created and process is started
                // outputStream = new FileStream(outputReadPipe, FileAccess.Read, 4096, false);
                System.Diagnostics.Debug.WriteLine("Output pipes created successfully - stream will be created after ConPTY setup");

                // IMPORTANT: Set console mode on the input pipe handle (PTY-side)
                // This enables VT processing for the ConPTY
                uint inputMode;
                if (GetConsoleMode(inputReadPipe, out inputMode))
                {
                    System.Diagnostics.Debug.WriteLine($"Current input console mode: 0x{inputMode:X8}");
                    SafeWriteDebugFile($"Current input console mode: 0x{inputMode:X8}\n");

                    // Enable VT processing
                    uint newInputMode = inputMode | ENABLE_VIRTUAL_TERMINAL_PROCESSING | DISABLE_NEWLINE_AUTO_RETURN;
                    if (SetConsoleMode(inputReadPipe, newInputMode))
                    {
                        System.Diagnostics.Debug.WriteLine($"Set input console mode to: 0x{newInputMode:X8}");
                        SafeWriteDebugFile($"SUCCESS: Set input console mode to 0x{newInputMode:X8}\n");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"SetConsoleMode for input failed: {Marshal.GetLastWin32Error()}");
                        SafeWriteDebugFile($"WARNING: SetConsoleMode for input failed: {Marshal.GetLastWin32Error()}\n");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"GetConsoleMode for input failed (expected for pipe): {Marshal.GetLastWin32Error()}");
                    SafeWriteDebugFile($"GetConsoleMode for input failed (expected for pipe): {Marshal.GetLastWin32Error()}\n");
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CreateConsolePipes failed: {ex.Message}");
                return false;
            }
        }

        private bool CreatePseudoConsoleSession()
        {
            try
            {
                COORD consoleSize = new COORD { X = (short)Columns, Y = (short)Rows };

                System.Diagnostics.Debug.WriteLine($"Attempting to create pseudo-console with size: {Columns}x{Rows}");

                int createResult = CreatePseudoConsole(
                    consoleSize,
                    inputReadPipe,
                    outputWritePipe,
                    0,  // Try with no flags
                    out pseudoConsoleHandle);

                if (createResult != 0)
                {
                    System.Diagnostics.Debug.WriteLine($"CreatePseudoConsole failed with error code: {createResult}");
                    return false;
                }

                if (pseudoConsoleHandle == IntPtr.Zero)
                {
                    System.Diagnostics.Debug.WriteLine("CreatePseudoConsole returned zero handle");
                    return false;
                }

                System.Diagnostics.Debug.WriteLine($"Pseudo-console created successfully with handle: {pseudoConsoleHandle}");

                // NOTE: We will close the PTY-side handles (inputReadPipe and outputWritePipe)
                // AFTER creating the child process, as per Microsoft documentation
                return true;
            }
            catch (EntryPointNotFoundException)
            {
                System.Diagnostics.Debug.WriteLine("CreatePseudoConsole not available on this system (Windows 10 1909+ required)");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CreatePseudoConsoleSession failed: {ex.Message}");
                return false;
            }
        }

        private bool StartShellProcess(string workingDirectory)
        {
            try
            {
                var lpSize = IntPtr.Zero;
                bool success = InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref lpSize);
                if (success || lpSize == IntPtr.Zero)
                {
                    System.Diagnostics.Debug.WriteLine("Could not calculate the number of bytes for the attribute list");
                    return false;
                }

                var startupInfo = new STARTUPINFOEX();
                startupInfo.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
                startupInfo.lpAttributeList = Marshal.AllocHGlobal(lpSize);

                success = InitializeProcThreadAttributeList(startupInfo.lpAttributeList, 1, 0, ref lpSize);
                if (!success)
                {
                    System.Diagnostics.Debug.WriteLine("Could not set up attribute list");
                    return false;
                }

                success = UpdateProcThreadAttribute(
                    startupInfo.lpAttributeList,
                    0,
                    (IntPtr)PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
                    pseudoConsoleHandle,
                    (IntPtr)IntPtr.Size,
                    IntPtr.Zero,
                    IntPtr.Zero);
                if (!success)
                {
                    System.Diagnostics.Debug.WriteLine("Could not set pseudoconsole thread attribute");
                    return false;
                }

                int securityAttributeSize = Marshal.SizeOf<SECURITY_ATTRIBUTES>();
                var pSec = new SECURITY_ATTRIBUTES { nLength = securityAttributeSize };
                var tSec = new SECURITY_ATTRIBUTES { nLength = securityAttributeSize };

                // Get the Claude CLI path and build PowerShell command
                string claudeCommand = GetClaudeCliPath();
                string commandLine = $"powershell.exe -NoExit -Command \"{claudeCommand}\"";

                success = CreateProcessW(
                    null,
                    commandLine,
                    ref pSec,
                    ref tSec,
                    false,
                    EXTENDED_STARTUPINFO_PRESENT,
                    IntPtr.Zero,
                    workingDirectory,
                    ref startupInfo,
                    out PROCESS_INFORMATION pInfo);
                if (!success)
                {
                    System.Diagnostics.Debug.WriteLine("Could not create process");
                    return false;
                }

                CloseHandle(pInfo.hThread);
                inputReadPipe?.Dispose();
                outputWritePipe?.Dispose();
                inputReadPipe = null;
                outputWritePipe = null;
                shellProcessHandle = pInfo.hProcess;

                DeleteProcThreadAttributeList(startupInfo.lpAttributeList);
                Marshal.FreeHGlobal(startupInfo.lpAttributeList);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"StartShellProcess failed: {ex.Message}");
                return false;
            }
        }

        private void ReadOutputSync(CancellationToken cancellationToken)
        {
            SafeWriteDebugFile("ReadOutputSync: METHOD ENTERED\n");
            System.Diagnostics.Debug.WriteLine("ConPtyTerminal.ReadOutputSync: METHOD ENTERED");

            try
            {
                System.Diagnostics.Debug.WriteLine("ConPtyTerminal.ReadOutputSync: Starting SYNCHRONOUS output reading loop");
                SafeWriteDebugFile("ReadOutputSync: Starting synchronous read loop\n");
                byte[] buffer = new byte[4096];
                int loopCount = 0;

                while (!cancellationToken.IsCancellationRequested && IsRunning && outputStream != null)
                {
                    loopCount++;
                    if (loopCount == 1)
                    {
                        System.Diagnostics.Debug.WriteLine($"ConPtyTerminal.ReadOutputSync: First iteration");
                        SafeWriteDebugFile($"ReadOutputSync: First iteration, calling Read()\n");
                    }
                    if (loopCount % 100 == 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"ConPtyTerminal.ReadOutputSync: Loop iteration {loopCount}");
                        SafeWriteDebugFile($"ReadOutputSync: Loop iteration {loopCount}\n");
                    }

                    System.Diagnostics.Debug.WriteLine($"ConPtyTerminal.ReadOutputSync: Calling BLOCKING Read() (iteration {loopCount})");

                    // Do a BLOCKING synchronous read
                    int bytesRead = 0;
                    try
                    {
                        bytesRead = outputStream.Read(buffer, 0, buffer.Length);
                        System.Diagnostics.Debug.WriteLine($"ConPtyTerminal.ReadOutputSync: Read() returned {bytesRead} bytes");
                        if (loopCount <= 5)
                        {
                            SafeWriteDebugFile($"ReadOutputSync: Read() returned {bytesRead} bytes\n");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"ConPtyTerminal.ReadOutputSync: Read() threw exception: {ex.Message}");
                        SafeWriteDebugFile($"ReadOutputSync: Read() exception: {ex.Message}\n");
                        break;
                    }

                    if (bytesRead > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"ConPtyTerminal.ReadOutputSync: Read {bytesRead} bytes from output stream");
                        string output = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        System.Diagnostics.Debug.WriteLine($"ConPtyTerminal.ReadOutputSync: Output text (first 100 chars): {output.Substring(0, Math.Min(100, output.Length))}");
                        SafeWriteDebugFile($"ReadOutputSync: Received {output.Length} chars of output\n");

                        if (OutputReceived != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"ConPtyTerminal.ReadOutputSync: Invoking OutputReceived event with {output.Length} characters");
                            OutputReceived.Invoke(this, output);
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"ConPtyTerminal.ReadOutputSync: WARNING - OutputReceived event has no subscribers!");
                        }
                    }
                    else
                    {
                        // 0 bytes means end of stream
                        System.Diagnostics.Debug.WriteLine($"ConPtyTerminal.ReadOutputSync: Read() returned 0 bytes - end of stream");
                        SafeWriteDebugFile("ReadOutputSync: End of stream (0 bytes)\n");
                        break;
                    }
                }

                System.Diagnostics.Debug.WriteLine($"ConPtyTerminal.ReadOutputSync: Output reading loop finished after {loopCount} iterations");
                SafeWriteDebugFile($"ReadOutputSync: Loop finished after {loopCount} iterations\n");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ConPtyTerminal.ReadOutputSync failed: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                SafeWriteDebugFile($"ReadOutputSync exception: {ex.Message}\n");
            }
        }

        private async Task ReadOutputAsync(CancellationToken cancellationToken)
        {
            System.IO.File.AppendAllText(@"c:\temp\conpty-debug.txt", "ReadOutputAsync: METHOD ENTERED\n");
            System.Diagnostics.Debug.WriteLine("ConPtyTerminal.ReadOutputAsync: METHOD ENTERED");

            try
            {
                System.Diagnostics.Debug.WriteLine("ConPtyTerminal.ReadOutputAsync: Starting output reading loop");
                System.IO.File.AppendAllText(@"c:\temp\conpty-debug.txt", "ReadOutputAsync: Starting loop\n");
                byte[] buffer = new byte[4096];
                int loopCount = 0;

                while (!cancellationToken.IsCancellationRequested && IsRunning && outputStream != null)
                {
                    loopCount++;
                    if (loopCount == 1)
                    {
                        System.Diagnostics.Debug.WriteLine($"ConPtyTerminal.ReadOutputAsync: First iteration of while loop");
                        System.IO.File.AppendAllText(@"c:\temp\conpty-debug.txt", $"ReadOutputAsync: First iteration, about to call ReadAsync\n");
                    }
                    if (loopCount % 100 == 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"ConPtyTerminal.ReadOutputAsync: Loop iteration {loopCount}");
                        System.IO.File.AppendAllText(@"c:\temp\conpty-debug.txt", $"ReadOutputAsync: Loop iteration {loopCount}\n");
                    }

                    System.Diagnostics.Debug.WriteLine($"ConPtyTerminal.ReadOutputAsync: Calling ReadAsync (iteration {loopCount})");

                    // Try a read with a short delay first to see if data becomes available
                    var readTask = outputStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                    var delayTask = Task.Delay(100, cancellationToken);
                    var completedTask = await Task.WhenAny(readTask, delayTask);

                    int bytesRead = 0;
                    if (completedTask == readTask)
                    {
                        bytesRead = await readTask;
                        System.Diagnostics.Debug.WriteLine($"ConPtyTerminal.ReadOutputAsync: ReadAsync returned {bytesRead} bytes");
                        if (loopCount <= 5)
                        {
                            System.IO.File.AppendAllText(@"c:\temp\conpty-debug.txt", $"ReadOutputAsync: ReadAsync returned {bytesRead} bytes\n");
                        }
                    }
                    else
                    {
                        if (loopCount <= 5)
                        {
                            System.Diagnostics.Debug.WriteLine($"ConPtyTerminal.ReadOutputAsync: ReadAsync timed out after 100ms (iteration {loopCount})");
                            System.IO.File.AppendAllText(@"c:\temp\conpty-debug.txt", $"ReadOutputAsync: Timeout after 100ms (iter {loopCount})\n");
                        }
                        continue; // Try again
                    }

                    if (bytesRead > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"ConPtyTerminal.ReadOutputAsync: Read {bytesRead} bytes from output stream");
                        string output = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        System.Diagnostics.Debug.WriteLine($"ConPtyTerminal.ReadOutputAsync: Output text (first 100 chars): {output.Substring(0, Math.Min(100, output.Length))}");

                        if (OutputReceived != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"ConPtyTerminal.ReadOutputAsync: Invoking OutputReceived event with {output.Length} characters");
                            OutputReceived.Invoke(this, output);
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"ConPtyTerminal.ReadOutputAsync: WARNING - OutputReceived event has no subscribers!");
                        }
                    }
                    else if (!IsRunning)
                    {
                        System.Diagnostics.Debug.WriteLine($"ConPtyTerminal.ReadOutputAsync: Terminal no longer running, stopping");
                        break;
                    }
                    else
                    {
                        // No data available, wait a bit
                        await Task.Delay(10, cancellationToken);
                    }
                }

                System.Diagnostics.Debug.WriteLine($"ConPtyTerminal.ReadOutputAsync: Output reading loop finished after {loopCount} iterations");
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("ConPtyTerminal.ReadOutputAsync: Operation cancelled");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ConPtyTerminal.ReadOutputAsync failed: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private FileStream inputStream;

        public void WriteInput(string text)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"ConPtyTerminal.WriteInput: text='{text}', length={text?.Length}");

                if (!IsRunning || shellProcessHandle == IntPtr.Zero)
                {
                    System.Diagnostics.Debug.WriteLine($"ConPtyTerminal.WriteInput: Cannot write - terminal not running");
                    return;
                }

                if (inputWritePipe == null || inputWritePipe.IsInvalid || inputWritePipe.IsClosed)
                {
                    System.Diagnostics.Debug.WriteLine($"ConPtyTerminal.WriteInput: Cannot write - inputWritePipe is invalid/closed");
                    return;
                }

                // Create input stream on first use if needed
                if (inputStream == null)
                {
                    System.Diagnostics.Debug.WriteLine($"ConPtyTerminal.WriteInput: Creating input FileStream");
                    inputStream = new FileStream(inputWritePipe, FileAccess.Write, 1024, false);
                }

                byte[] buffer = Encoding.UTF8.GetBytes(text);
                System.Diagnostics.Debug.WriteLine($"ConPtyTerminal.WriteInput: Writing {buffer.Length} bytes to input stream");
                inputStream.Write(buffer, 0, buffer.Length);
                inputStream.Flush();
                System.Diagnostics.Debug.WriteLine($"ConPtyTerminal.WriteInput: Write completed successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ConPtyTerminal.WriteInput failed: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private string GetClaudeCliPath()
        {
            // Try npm installed location first
            string npmPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "npm",
                "claude.cmd"
            );
            if (File.Exists(npmPath))
                return npmPath;

            // Try PATH environment variable
            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string dir in pathEnv.Split(Path.PathSeparator))
            {
                string claudePath = Path.Combine(dir, "claude.cmd");
                if (File.Exists(claudePath))
                    return claudePath;

                claudePath = Path.Combine(dir, "claude");
                if (File.Exists(claudePath))
                    return claudePath;

                claudePath = Path.Combine(dir, "claude-code");
                if (File.Exists(claudePath))
                    return claudePath;

                claudePath = Path.Combine(dir, "claude-code.cmd");
                if (File.Exists(claudePath))
                    return claudePath;
            }

            // Fallback to just "claude" and hope it's in PATH
            return "claude";
        }

        public void Dispose()
        {
            Cleanup();
        }

        private void Cleanup()
        {
            try
            {
                IsRunning = false;
                cancellationTokenSource?.Cancel();
                cancellationTokenSource?.Dispose();

                inputStream?.Dispose();
                outputStream?.Dispose();

                // Close the process handle - this will terminate the shell process
                if (shellProcessHandle != IntPtr.Zero)
                {
                    CloseHandle(shellProcessHandle);
                    shellProcessHandle = IntPtr.Zero;
                }

                if (pseudoConsoleHandle != IntPtr.Zero)
                {
                    ClosePseudoConsole(pseudoConsoleHandle);
                    pseudoConsoleHandle = IntPtr.Zero;
                }

                inputReadPipe?.Dispose();
                inputWritePipe?.Dispose();
                outputReadPipe?.Dispose();
                outputWritePipe?.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Cleanup failed: {ex.Message}");
            }
        }
    }
}
