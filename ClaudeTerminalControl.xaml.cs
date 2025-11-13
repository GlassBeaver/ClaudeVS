namespace ClaudeVS
{
    using System;
    using System.IO;
    using System.Windows.Controls;
    using System.Windows.Input;
    using System.Windows.Media;
    using EnvDTE;
    using EnvDTE80;
    using Microsoft.Terminal.Wpf;
    using Microsoft.VisualStudio.Shell;

    /// <summary>
    /// Interaction logic for ClaudeTerminalControl.xaml
    /// </summary>
    public partial class ClaudeTerminalControl : UserControl
    {
        private ClaudeTerminal claudeTerminal;
        private DTE2 dte;
        private SolutionEvents solutionEvents;
        private bool isInitialized;

        /// <summary>
        /// Initializes a new instance of the <see cref="ClaudeTerminalControl"/> class.
        /// </summary>
        public ClaudeTerminalControl(ToolWindowPane toolWindowPane = null)
        {
            this.claudeTerminal = toolWindowPane as ClaudeTerminal;
            this.InitializeComponent();
            this.Loaded += ClaudeTerminalControl_Loaded;
            this.Unloaded += ClaudeTerminalControl_Unloaded;
            this.SizeChanged += ClaudeTerminalControl_SizeChanged;
            this.PreviewKeyDown += ClaudeTerminalControl_PreviewKeyDown;
            System.Diagnostics.Debug.WriteLine("ClaudeTerminalControl constructed");
        }

        private void ClaudeTerminalControl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                System.Diagnostics.Debug.WriteLine("ClaudeTerminalControl_PreviewKeyDown: Escape key intercepted, sending to terminal");

                var terminal = claudeTerminal?.Terminal;
                if (terminal != null && terminal.IsRunning)
                {
                    terminal.WriteInput("\x1b");
                    System.Diagnostics.Debug.WriteLine("ClaudeTerminalControl_PreviewKeyDown: Escape sent successfully");
                }

                e.Handled = true;
            }
        }

        private void TerminalControl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                System.Diagnostics.Debug.WriteLine("TerminalControl_PreviewKeyDown: Escape key already handled by parent");
                e.Handled = true;
            }
        }

        private void ClaudeTerminalControl_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("ClaudeTerminalControl_Loaded starting");

                if (!isInitialized)
                {
                    dte = GetDTE();
                    if (dte != null && dte.Events != null)
                    {
                        solutionEvents = dte.Events.SolutionEvents;
                        if (solutionEvents != null)
                        {
                            solutionEvents.Opened += SolutionEvents_Opened;
                            solutionEvents.AfterClosing += SolutionEvents_AfterClosing;
                            System.Diagnostics.Debug.WriteLine("Subscribed to solution events");
                        }
                    }

                    string projectDir = GetActiveProjectDirectory();
                    if (!string.IsNullOrEmpty(projectDir))
                    {
                        System.Diagnostics.Debug.WriteLine($"Project found, initializing terminal with: {projectDir}");
                        InitializeConPtyTerminal();
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("No project found, waiting for project to be opened");
                    }

                    isInitialized = true;
                }
                else if (claudeTerminal?.Terminal == null)
                {
                    System.Diagnostics.Debug.WriteLine("Terminal was disposed (window closed), reinitializing");
                    string projectDir = GetActiveProjectDirectory();
                    if (!string.IsNullOrEmpty(projectDir))
                    {
                        InitializeConPtyTerminal();
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Terminal already initialized and connected, nothing to do");
                }

                TerminalControl.PreviewKeyDown += TerminalControl_PreviewKeyDown;

                System.Diagnostics.Debug.WriteLine("Setting focus to TerminalControl");
                TerminalControl.Focus();
                System.Diagnostics.Debug.WriteLine($"Focus set to TerminalControl");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing terminal: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private void InitializeConPtyTerminal()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("InitializeConPtyTerminal starting");

                System.Diagnostics.Debug.WriteLine("Creating new ConPtyTerminal instance");
                var conPtyTerminal = new ConPtyTerminal(rows: 30, columns: 120);
                System.Diagnostics.Debug.WriteLine("ConPtyTerminal instance created successfully");

                string workingDir = GetActiveProjectDirectory();
                if (string.IsNullOrEmpty(workingDir))
                {
                    workingDir = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
                    System.Diagnostics.Debug.WriteLine($"No active project found, using default working directory: {workingDir}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Using active project directory: {workingDir}");
                }

                bool initialized = conPtyTerminal.Initialize(workingDir);
                System.Diagnostics.Debug.WriteLine($"ConPTY Initialize returned: {initialized}");

                if (!initialized)
                {
                    System.Diagnostics.Debug.WriteLine("FAILED: ConPTY terminal initialization returned false");
                    conPtyTerminal?.Dispose();
                    return;
                }

                System.Diagnostics.Debug.WriteLine("SUCCESS: ConPTY terminal initialized successfully");

                System.Diagnostics.Debug.WriteLine("Creating ConPtyTerminalConnection");
                var terminalConnection = new ConPtyTerminalConnection(conPtyTerminal);

                claudeTerminal?.SetTerminalInstances(conPtyTerminal, terminalConnection);

                var theme = new TerminalTheme
                {
                    DefaultBackground = 0xFF1e1e1e,
                    DefaultForeground = 0xFFd4d4d4,
                    DefaultSelectionBackground = 0xFF264F78,
                    CursorStyle = CursorStyle.BlinkingBar,
                    ColorTable = new uint[]
                    {
                        0xFF0C0C0C, 0xFFC50F1F, 0xFF13A10E, 0xFFC19C00,
                        0xFF0037DA, 0xFF881798, 0xFF3A96DD, 0xFFCCCCCC,
                        0xFF767676, 0xFFE74856, 0xFF16C60C, 0xFFF9F1A5,
                        0xFF3B78FF, 0xFFB4009E, 0xFF61D6D6, 0xFFF2F2F2
                    }
                };
                TerminalControl.SetTheme(theme, "Consolas", 10, Colors.Transparent);

                System.Diagnostics.Debug.WriteLine("Setting TerminalControl.Connection");
                TerminalControl.Connection = terminalConnection;
                System.Diagnostics.Debug.WriteLine("TerminalControl.Connection set successfully");

                System.Diagnostics.Debug.WriteLine("Starting the terminal connection");
                terminalConnection.Start();
                System.Diagnostics.Debug.WriteLine("Terminal connection started");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EXCEPTION in InitializeConPtyTerminal: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private void ReconnectTerminal()
        {
            try
            {
                if (claudeTerminal?.TerminalConnection != null)
                {
                    System.Diagnostics.Debug.WriteLine("Reconnecting to existing terminal instance");
                    TerminalControl.Connection = claudeTerminal.TerminalConnection;
                    TerminalControl.InvalidateVisual();
                    TerminalControl.UpdateLayout();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EXCEPTION in ReconnectTerminal: {ex.Message}");
            }
        }

        private void ClaudeTerminalControl_SizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
        {
            try
            {
                var terminalConnection = claudeTerminal?.TerminalConnection;
                if (terminalConnection != null && TerminalControl.ActualHeight > 0 && TerminalControl.ActualWidth > 0)
                {
                    double fontSize = 10;
                    double charHeight = fontSize * 1.2;

                    uint columns = 120;
                    uint rows = (uint)Math.Max(1, TerminalControl.ActualHeight / charHeight);

                    terminalConnection.Resize(rows, columns);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ClaudeTerminalControl_SizeChanged error: {ex.Message}");
            }
        }

        private void ClaudeTerminalControl_Unloaded(object sender, System.Windows.RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("ClaudeTerminalControl_Unloaded: Not disconnecting - preserving terminal state");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ClaudeTerminalControl_Unloaded: {ex.Message}");
            }
        }

        private void SolutionEvents_Opened()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("SolutionEvents_Opened: Solution opened");
                string projectDir = GetActiveProjectDirectory();
                if (!string.IsNullOrEmpty(projectDir))
                {
                    System.Diagnostics.Debug.WriteLine($"SolutionEvents_Opened: Restarting Claude with new project directory: {projectDir}");
                    RestartClaudeWithWorkingDirectory(projectDir);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SolutionEvents_Opened error: {ex.Message}");
            }
        }

        private void SolutionEvents_AfterClosing()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("SolutionEvents_AfterClosing: Solution closed, stopping Claude");
                StopClaude();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SolutionEvents_AfterClosing error: {ex.Message}");
            }
        }

        private void RestartClaudeWithWorkingDirectory(string workingDirectory)
        {
            try
            {
                StopClaude();
                InitializeConPtyTerminal();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RestartClaudeWithWorkingDirectory error: {ex.Message}");
            }
        }

        private void StopClaude()
        {
            try
            {
                TerminalControl.Connection = null;

                var terminal = claudeTerminal?.Terminal;
                if (terminal != null)
                {
                    terminal.Dispose();
                }

                claudeTerminal?.SetTerminalInstances(null, null);
                isInitialized = false;

                System.Diagnostics.Debug.WriteLine("Claude terminal stopped");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"StopClaude error: {ex.Message}");
            }
        }

        private DTE2 GetDTE()
        {
            try
            {
                if (claudeTerminal != null)
                {
                    DTE2 result = claudeTerminal.GetService<EnvDTE.DTE, EnvDTE.DTE>() as DTE2;
                    if (result != null)
                        return result;
                }

                return (DTE2)System.Runtime.InteropServices.Marshal.GetActiveObject("VisualStudio.DTE.18.0");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetDTE error: {ex.Message}");
                return null;
            }
        }

        private string GetActiveProjectDirectory()
        {
            try
            {
                DTE2 localDte = dte ?? GetDTE();

                if (localDte == null || localDte.Solution == null)
                {
                    System.Diagnostics.Debug.WriteLine("GetActiveProjectDirectory: DTE or Solution is null");
                    return null;
                }

                if (localDte.Solution.IsOpen && !string.IsNullOrEmpty(localDte.Solution.FullName))
                {
                    string solutionDir = Path.GetDirectoryName(localDte.Solution.FullName);
                    System.Diagnostics.Debug.WriteLine($"GetActiveProjectDirectory: Using solution directory: {solutionDir}");
                    return solutionDir;
                }

                System.Diagnostics.Debug.WriteLine("GetActiveProjectDirectory: No solution open");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetActiveProjectDirectory: Exception: {ex}");
            }

            System.Diagnostics.Debug.WriteLine("GetActiveProjectDirectory: Returning null (no solution found)");
            return null;
        }

        public void SendToClaude(string message, bool bEnter, bool bFocus)
        {
            try
            {
                var terminal = claudeTerminal?.Terminal;
                if (terminal != null)
                    terminal.SendToClaude(message, bEnter);

                if (bFocus)
                    TerminalControl.Focus();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SendToClaude failed: {ex.Message}");
            }
        }
    }
}
