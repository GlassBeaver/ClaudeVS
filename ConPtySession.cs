using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace ClaudeVS
{
    public class ConPtySession : IDisposable
    {
        private string workingDirectory;
        private string executablePath;
        private readonly object syncLock = new object();

        public event EventHandler<string> ChunkReceived;

        public bool IsRunning { get; set; } = true;

        public ConPtySession(string workingDirectory = null)
        {
            this.workingDirectory = workingDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        public async Task<bool> InitializeAsync(string executablePath)
        {
            this.executablePath = executablePath;
            return true;
        }

        public async Task<(string response, string sessionId)> SendMessageAsync(string message, int timeoutMs = 30000)
        {
            if (!IsRunning)
                throw new InvalidOperationException("ConPTY session is not running");

            if (string.IsNullOrEmpty(executablePath))
                throw new InvalidOperationException("Executable path not set");

            lock (syncLock)
            {
                try
                {
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = executablePath,
                            Arguments = $"-p --verbose --output-format json --allowedTools \"Bash(*:*)\" \"Read\" \"Write\" \"Glob\"",
                            UseShellExecute = false,
                            RedirectStandardInput = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true,
                            WorkingDirectory = workingDirectory,
                            StandardOutputEncoding = Encoding.UTF8,
                            StandardErrorEncoding = Encoding.UTF8
                        }
                    };

                    System.Diagnostics.Debug.WriteLine($"ConPtySession.SendMessageAsync: Starting: {process.StartInfo.FileName} {process.StartInfo.Arguments}");
                    System.Diagnostics.Debug.WriteLine($"ConPtySession.SendMessageAsync: WorkDir: {workingDirectory}");
                    System.Diagnostics.Debug.WriteLine($"ConPtySession.SendMessageAsync: WorkDir exists: {Directory.Exists(workingDirectory)}");
                    System.Diagnostics.Debug.WriteLine($"ConPtySession.SendMessageAsync: Message: {message}");

                    process.Start();

                    process.StandardInput.WriteLine(message);
                    process.StandardInput.Close();

                    string fullOutput = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();

                    bool exited = process.WaitForExit(timeoutMs);

                    System.Diagnostics.Debug.WriteLine($"Process exited: {exited}, ExitCode: {process.ExitCode}");
                    System.Diagnostics.Debug.WriteLine($"Full output length: {fullOutput.Length}");
                    System.Diagnostics.Debug.WriteLine($"Error: {error}");

                    if (!exited)
                    {
                        process.Kill();
                        throw new InvalidOperationException("Process timeout");
                    }

                    process.Dispose();

                    string responseText = "";
                    string sessionId = ExtractSessionIdFromJson(fullOutput);

                    if (!string.IsNullOrEmpty(fullOutput))
                    {
                        responseText = ExtractTextFromJson(fullOutput);
                    }

                    if (string.IsNullOrWhiteSpace(responseText))
                    {
                        responseText = string.IsNullOrWhiteSpace(error) ? "No response from Claude CLI" : error;
                    }

                    System.Diagnostics.Debug.WriteLine($"Extracted session ID: {sessionId}");
                    System.Diagnostics.Debug.WriteLine($"Response text length: {responseText.Length}");

                    if (!string.IsNullOrEmpty(responseText))
                    {
                        ChunkReceived?.Invoke(this, responseText);
                    }

                    return (responseText.Trim(), sessionId ?? "");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"SendMessageAsync error: {ex}");
                    throw new InvalidOperationException($"Error communicating with Claude CLI: {ex.Message}", ex);
                }
            }
        }

        private string ExtractSessionIdFromJson(string jsonOutput)
        {
            if (string.IsNullOrWhiteSpace(jsonOutput))
                return null;

            try
            {
                int sessionIdIndex = jsonOutput.IndexOf("\"session_id\":");
                if (sessionIdIndex != -1)
                {
                    int startIndex = jsonOutput.IndexOf('"', sessionIdIndex + 13);
                    if (startIndex != -1)
                    {
                        int endIndex = jsonOutput.IndexOf('"', startIndex + 1);
                        if (endIndex != -1)
                        {
                            string sessionId = jsonOutput.Substring(startIndex + 1, endIndex - startIndex - 1);
                            System.Diagnostics.Debug.WriteLine($"Extracted session ID: {sessionId}");
                            return sessionId;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to extract session ID: {ex}");
            }

            return null;
        }

        private string ExtractTextFromJson(string jsonOutput)
        {
            if (string.IsNullOrWhiteSpace(jsonOutput))
                return string.Empty;

            var textParts = new StringBuilder();

            try
            {
                int index = 0;
                while ((index = jsonOutput.IndexOf("\"text\":", index)) != -1)
                {
                    int startIndex = jsonOutput.IndexOf('"', index + 7);
                    if (startIndex != -1)
                    {
                        int endIndex = jsonOutput.IndexOf('"', startIndex + 1);
                        while (endIndex != -1 && jsonOutput[endIndex - 1] == '\\')
                        {
                            endIndex = jsonOutput.IndexOf('"', endIndex + 1);
                        }

                        if (endIndex != -1)
                        {
                            string text = jsonOutput.Substring(startIndex + 1, endIndex - startIndex - 1);
                            text = text.Replace("\\\"", "\"")
                                      .Replace("\\n", "\n")
                                      .Replace("\\r", "\r")
                                      .Replace("\\\\", "\\");
                            textParts.Append(text);
                            index = endIndex + 1;
                        }
                        else
                        {
                            break;
                        }
                    }
                    else
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to extract text from JSON: {ex}");
            }

            return textParts.ToString();
        }

        private string EscapeForCommandLine(string input)
        {
            return input.Replace("\"", "\\\"");
        }

        public void Dispose()
        {
        }
    }
}
