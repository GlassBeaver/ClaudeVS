using System;
using System.IO;
using System.Threading.Tasks;

namespace ClaudeVS
{
    public class ClaudeCliManager : IDisposable
    {
        private string workingDirectory;
        private ConPtySession conPtySession;
        private string claudePath;
        private bool initialized = false;
        private string currentSessionId = null;

        public event EventHandler<string> ResponseReceived;
        public event EventHandler<string> ChunkReceived;
        public event EventHandler<string> ErrorOccurred;

        public string CurrentSessionId => currentSessionId;

        public ClaudeCliManager(string workingDirectory = null)
        {
            this.workingDirectory = workingDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        public void SetWorkingDirectory(string directory)
        {
            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            {
                System.Diagnostics.Debug.WriteLine($"ClaudeCliManager.SetWorkingDirectory: Setting to {directory}");
                workingDirectory = directory;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"ClaudeCliManager.SetWorkingDirectory: Directory does not exist or is empty: {directory}");
            }
        }

        public async Task SendMessageAsync(string message)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"ClaudeCliManager.SendMessageAsync: CALLED with workingDirectory = {workingDirectory}");
                System.Diagnostics.Debug.WriteLine($"ClaudeCliManager.SendMessageAsync: Current session ID: {currentSessionId}");

                // Find Claude CLI path if not already found
                if (string.IsNullOrEmpty(claudePath))
                {
                    claudePath = GetClaudeCliPath();
                    if (string.IsNullOrEmpty(claudePath))
                    {
                        ErrorOccurred?.Invoke(this, "Claude CLI not found. Please install Claude Code CLI.");
                        return;
                    }
                }

                // Ensure git-bash path is set
                SetupGitBashPath();

                // Initialize ConPTY session if needed
                if (!initialized || conPtySession == null || !conPtySession.IsRunning)
                {
                    System.Diagnostics.Debug.WriteLine($"ClaudeCliManager.SendMessageAsync: Creating new ConPtySession with workingDirectory = {workingDirectory}");
                    conPtySession?.Dispose();
                    conPtySession = new ConPtySession(workingDirectory);

                    // Wire up streaming chunk events
                    conPtySession.ChunkReceived += (sender, chunk) => ChunkReceived?.Invoke(this, chunk);

                    if (!await conPtySession.InitializeAsync(claudePath))
                    {
                        ErrorOccurred?.Invoke(this, $"Failed to initialize Claude CLI session. Executable: {claudePath}, Working Dir: {workingDirectory}");
                        return;
                    }

                    initialized = true;
                    currentSessionId = null;  // Reset session ID for new session
                }

                // Prepare the message with session resume if we have a previous session
                string messageToSend = message;
                if (!string.IsNullOrEmpty(currentSessionId))
                {
                    // Continue the existing conversation session
                    messageToSend = $"--resume {currentSessionId}\n{message}";
                    System.Diagnostics.Debug.WriteLine($"ClaudeCliManager.SendMessageAsync: Resuming session {currentSessionId}");
                }

                // Send message and get response
                try
                {
                    var (response, sessionId) = await conPtySession.SendMessageAsync(messageToSend, 30000);

                    // Update the session ID for next message
                    if (!string.IsNullOrEmpty(sessionId))
                    {
                        currentSessionId = sessionId;
                        System.Diagnostics.Debug.WriteLine($"ClaudeCliManager.SendMessageAsync: Updated session ID to {sessionId}");
                    }

                    if (!string.IsNullOrWhiteSpace(response))
                    {
                        ResponseReceived?.Invoke(this, response);
                    }
                    else
                    {
                        ResponseReceived?.Invoke(this, "No response from Claude CLI");
                    }
                }
                catch (Exception ex)
                {
                    // Session may have crashed, reset for next attempt
                    initialized = false;
                    ErrorOccurred?.Invoke(this, $"Claude CLI error: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"Error sending message: {ex.Message}");
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

            return null;
        }

        private void SetupGitBashPath()
        {
            // Check if CLAUDE_CODE_GIT_BASH_PATH is already set
            string existingPath = Environment.GetEnvironmentVariable("CLAUDE_CODE_GIT_BASH_PATH");
            if (!string.IsNullOrEmpty(existingPath) && File.Exists(existingPath))
                return;

            // Try common Git Bash locations
            string[] gitBashPaths = new[]
            {
                @"C:\Program Files\Git\bin\bash.exe",
                @"C:\Program Files (x86)\Git\bin\bash.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Git\bin\bash.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Git\bin\bash.exe"),
            };

            foreach (string bashPath in gitBashPaths)
            {
                if (File.Exists(bashPath))
                {
                    Environment.SetEnvironmentVariable("CLAUDE_CODE_GIT_BASH_PATH", bashPath, EnvironmentVariableTarget.Process);
                    return;
                }
            }

            // If not found in standard locations, search PATH
            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string dir in pathEnv.Split(Path.PathSeparator))
            {
                string bashPath = Path.Combine(dir, "bash.exe");
                if (File.Exists(bashPath))
                {
                    Environment.SetEnvironmentVariable("CLAUDE_CODE_GIT_BASH_PATH", bashPath, EnvironmentVariableTarget.Process);
                    return;
                }
            }
        }

        public void Dispose()
        {
            conPtySession?.Dispose();
        }
    }
}
