using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace ClaudeVS
{
    public class ClaudeCliManager : IDisposable
    {
        private Process claudeProcess;
        private bool isInitialized;
        private StringBuilder outputBuffer;

        public event EventHandler<string> ResponseReceived;
        public event EventHandler<string> ErrorOccurred;

        public ClaudeCliManager()
        {
            outputBuffer = new StringBuilder();
        }

        private async Task EnsureInitializedAsync()
        {
            if (isInitialized)
                return;

            try
            {
                // Start claude-code process
                claudeProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "claude-code",
                        Arguments = "", // Can add arguments if needed
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                    }
                };

                // Set up output handlers
                claudeProcess.OutputDataReceived += OnOutputDataReceived;
                claudeProcess.ErrorDataReceived += OnErrorDataReceived;

                // Start the process
                claudeProcess.Start();
                claudeProcess.BeginOutputReadLine();
                claudeProcess.BeginErrorReadLine();

                isInitialized = true;

                // Give the process a moment to start
                await Task.Delay(500);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"Failed to start Claude CLI: {ex.Message}");
                throw;
            }
        }

        public async Task SendMessageAsync(string message)
        {
            await EnsureInitializedAsync();

            if (claudeProcess == null || claudeProcess.HasExited)
            {
                ErrorOccurred?.Invoke(this, "Claude CLI process is not running");
                return;
            }

            try
            {
                // Clear previous output
                outputBuffer.Clear();

                // Send message to stdin
                await claudeProcess.StandardInput.WriteLineAsync(message);
                await claudeProcess.StandardInput.FlushAsync();

                // Wait a bit for response (in a real implementation, you'd have better parsing)
                await Task.Delay(2000);

                // Get accumulated output
                string response = outputBuffer.ToString();
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
                ErrorOccurred?.Invoke(this, $"Error sending message: {ex.Message}");
            }
        }

        private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                outputBuffer.AppendLine(e.Data);
            }
        }

        private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                ErrorOccurred?.Invoke(this, e.Data);
            }
        }

        public void Dispose()
        {
            if (claudeProcess != null)
            {
                try
                {
                    if (!claudeProcess.HasExited)
                    {
                        claudeProcess.Kill();
                    }
                    claudeProcess.Dispose();
                }
                catch
                {
                    // Ignore errors during cleanup
                }
            }
        }
    }
}
