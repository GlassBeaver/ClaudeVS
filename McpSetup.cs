namespace ClaudeVS
{
	using System;
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.IO;
	using System.Reflection;
	using System.Text;
	using System.Threading.Tasks;
	using System.Web.Script.Serialization;
	using Process = System.Diagnostics.Process;
	using Task = System.Threading.Tasks.Task;

	internal static class McpSetup
	{
		private const string ServerName = "claudevs-debugger";
		private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

		public static string GetStableHelperDirectory()
		{
			return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClaudeVS", "DebuggerMcp", "current");
		}

		public static string GetStableHelperPath()
		{
			return Path.Combine(GetStableHelperDirectory(), "ClaudeVS.DebuggerMcp.exe");
		}

		public static string GetDiscoveryDirectory()
		{
			return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClaudeVS", "DebuggerBridge", "instances");
		}

		public static bool CopyHelperToStablePath()
		{
			try
			{
				string extensionDirectory = Path.GetDirectoryName(typeof(ClaudeVSPackage).Assembly.Location);
				if (string.IsNullOrEmpty(extensionDirectory))
					return false;

				string sourceDirectory = Path.Combine(extensionDirectory, "DebuggerMcp");
				if (!Directory.Exists(sourceDirectory))
					return false;

				string targetDirectory = GetStableHelperDirectory();
				Directory.CreateDirectory(targetDirectory);

				foreach (string sourceFile in Directory.GetFiles(sourceDirectory))
				{
					string targetFile = Path.Combine(targetDirectory, Path.GetFileName(sourceFile));
					if (ShouldCopyHelperFile(sourceFile, targetFile))
						CopyHelperFile(sourceFile, targetFile);
				}

				return File.Exists(GetStableHelperPath());
			}
			catch
			{
				return false;
			}
		}

		private static bool ShouldCopyHelperFile(string sourceFile, string targetFile)
		{
			if (!File.Exists(targetFile))
				return true;

			Version sourceVersion = GetFileVersion(sourceFile);
			Version targetVersion = GetFileVersion(targetFile);
			if (sourceVersion != null && targetVersion != null)
			{
				int versionComparison = sourceVersion.CompareTo(targetVersion);
				if (versionComparison != 0)
					return versionComparison > 0;
			}

			DateTime sourceTime = File.GetLastWriteTimeUtc(sourceFile);
			DateTime targetTime = File.GetLastWriteTimeUtc(targetFile);
			if (sourceTime != targetTime)
				return sourceTime > targetTime;

			return new FileInfo(sourceFile).Length != new FileInfo(targetFile).Length;
		}

		private static void CopyHelperFile(string sourceFile, string targetFile)
		{
			try
			{
				File.Copy(sourceFile, targetFile, true);
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
			{
				if (!IsStableHelperPath(targetFile))
					throw;

				StopRunningStableHelpers(targetFile);
				File.Copy(sourceFile, targetFile, true);
			}
		}

		private static bool IsStableHelperPath(string path)
		{
			try
			{
				return string.Equals(Path.GetFullPath(path), Path.GetFullPath(GetStableHelperPath()), StringComparison.OrdinalIgnoreCase);
			}
			catch
			{
				return false;
			}
		}

		private static void StopRunningStableHelpers(string targetFile)
		{
			foreach (Process process in Process.GetProcessesByName("ClaudeVS.DebuggerMcp"))
			{
				try
				{
					string path = process.MainModule?.FileName;
					if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(targetFile), StringComparison.OrdinalIgnoreCase))
						continue;

					process.Kill();
					process.WaitForExit(2000);
				}
				catch
				{
				}
				finally
				{
					process.Dispose();
				}
			}
		}

		private static Version GetFileVersion(string path)
		{
			try
			{
				string version = FileVersionInfo.GetVersionInfo(path).FileVersion;
				if (Version.TryParse(version, out Version parsed))
					return parsed;
			}
			catch
			{
			}

			return null;
		}

		public static void CleanupStaleDiscoveryRecords()
		{
			string directory = GetDiscoveryDirectory();
			if (!Directory.Exists(directory))
				return;

			foreach (string file in Directory.GetFiles(directory, "*.json"))
			{
				try
				{
					Dictionary<string, object> record = Serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(file, new UTF8Encoding(false)));
					int pid = Convert.ToInt32(record["pid"]);
					if (!IsActiveDevenv(pid))
						File.Delete(file);
				}
				catch
				{
					try
					{
						File.Delete(file);
					}
					catch
					{
					}
				}
			}
		}

		public static async Task<McpSetupResult> ConfigureClientAsync(string client)
		{
			CopyHelperToStablePath();
			string helperPath = GetStableHelperPath();
			if (!File.Exists(helperPath))
				return McpSetupResult.CreateFailure("The MCP helper was not found at:" + Environment.NewLine + helperPath);

			string executable = FindClientExecutable(client);
			if (string.IsNullOrEmpty(executable))
				return McpSetupResult.CreateFailure("Could not find the " + client + " CLI on PATH or in the expected npm install location.");

			CommandResult addResult = await RunClientCommandAsync(executable, GetAddArguments(client), 30000);
			if (!addResult.Success && LooksAlreadyConfigured(addResult.CombinedOutput))
			{
				await RunClientCommandAsync(executable, GetRemoveArguments(client), 15000);
				addResult = await RunClientCommandAsync(executable, GetAddArguments(client), 30000);
			}

			if (!addResult.Success)
				return McpSetupResult.CreateFailure("Failed to configure " + client + " MCP." + Environment.NewLine + Environment.NewLine + Limit(addResult.CombinedOutput, 4000));

			CommandResult getResult = await RunClientCommandAsync(executable, "mcp get " + ServerName, 15000);
			string message = char.ToUpperInvariant(client[0]) + client.Substring(1) + " MCP is configured for " + ServerName + "." + Environment.NewLine + Environment.NewLine + "Helper:" + Environment.NewLine + helperPath;
			if (getResult.Success && !PathAppearsInOutput(getResult.CombinedOutput, helperPath))
				message += Environment.NewLine + Environment.NewLine + "The client accepted the setup, but its reported command did not contain the expected helper path.";

			return McpSetupResult.CreateSuccess(message);
		}

		public static bool IsClientAvailable(string client)
		{
			return !string.IsNullOrEmpty(FindClientExecutable(client));
		}

		public static async Task<McpSetupResult> VerifyAsync()
		{
			CopyHelperToStablePath();
			CleanupStaleDiscoveryRecords();

			List<string> lines = new List<string>();
			string helperPath = GetStableHelperPath();
			bool helperExists = File.Exists(helperPath);
			lines.Add("Helper file: " + (helperExists ? "OK" : "Missing"));
			lines.Add(helperPath);

			if (helperExists)
			{
				CommandResult probe = await ProbeHelperAsync(helperPath);
				lines.Add("Helper MCP handshake: " + (probe.Success ? "OK" : "Failed"));
				if (!probe.Success && !string.IsNullOrWhiteSpace(probe.CombinedOutput))
					lines.Add(Limit(probe.CombinedOutput, 1000));
			}

			List<string> activeBridges = GetActiveBridgeRecords();
			lines.Add("Active Visual Studio bridge: " + (activeBridges.Count > 0 ? "OK" : "Missing"));
			foreach (string bridge in activeBridges)
				lines.Add(bridge);

			await AddClientStatusAsync(lines, "codex");
			await AddClientStatusAsync(lines, "claude");

			lines.Add("");
			lines.Add("Manual commands:");
			lines.AddRange(GetManualCommands().Split(new[] { Environment.NewLine }, StringSplitOptions.None));

			bool success = helperExists && activeBridges.Count > 0;
			return new McpSetupResult(success, string.Join(Environment.NewLine, lines));
		}

		public static string GetManualCommands()
		{
			string helperPath = GetStableHelperPath();
			return "codex mcp add " + ServerName + " -- " + QuoteArg(helperPath)
				+ Environment.NewLine
				+ "claude mcp add --scope user " + ServerName + " -- " + QuoteArg(helperPath);
		}

		private static async Task AddClientStatusAsync(List<string> lines, string client)
		{
			string executable = FindClientExecutable(client);
			lines.Add("");
			lines.Add(char.ToUpperInvariant(client[0]) + client.Substring(1) + " CLI: " + (string.IsNullOrEmpty(executable) ? "Missing" : "OK"));
			if (string.IsNullOrEmpty(executable))
				return;

			lines.Add(executable);
			CommandResult result = await RunClientCommandAsync(executable, "mcp get " + ServerName, 15000);
			if (result.Success)
				lines.Add(char.ToUpperInvariant(client[0]) + client.Substring(1) + " registration: " + (PathAppearsInOutput(result.CombinedOutput, GetStableHelperPath()) ? "OK" : "Different command"));
			else
				lines.Add(char.ToUpperInvariant(client[0]) + client.Substring(1) + " registration: Missing or unreadable");
		}

		private static List<string> GetActiveBridgeRecords()
		{
			List<string> records = new List<string>();
			string directory = GetDiscoveryDirectory();
			if (!Directory.Exists(directory))
				return records;

			foreach (string file in Directory.GetFiles(directory, "*.json"))
			{
				try
				{
					Dictionary<string, object> record = Serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(file, new UTF8Encoding(false)));
					int pid = Convert.ToInt32(record["pid"]);
					if (!IsActiveDevenv(pid))
						continue;

					string solutionPath = record.ContainsKey("solutionPath") && record["solutionPath"] != null ? record["solutionPath"].ToString() : "";
					records.Add("devenv " + pid + ": " + (string.IsNullOrEmpty(solutionPath) ? "(no solution)" : solutionPath));
				}
				catch
				{
				}
			}

			return records;
		}

		private static bool IsActiveDevenv(int pid)
		{
			try
			{
				Process process = Process.GetProcessById(pid);
				return !process.HasExited && string.Equals(process.ProcessName, "devenv", StringComparison.OrdinalIgnoreCase);
			}
			catch
			{
				return false;
			}
		}

		private static string FindClientExecutable(string client)
		{
			string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
			if (string.Equals(client, "codex", StringComparison.OrdinalIgnoreCase))
			{
				string packagedExePath = Path.Combine(appData, "npm", "node_modules", "@openai", "codex", "node_modules", "@openai", "codex-win32-x64", "vendor", "x86_64-pc-windows-msvc", "bin", "codex.exe");
				if (File.Exists(packagedExePath))
					return packagedExePath;
			}

			foreach (string candidate in GetClientCandidates(client, appData))
			{
				if (File.Exists(candidate))
					return candidate;
			}

			string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
			foreach (string dir in pathEnv.Split(Path.PathSeparator))
			{
				if (string.IsNullOrWhiteSpace(dir))
					continue;

				foreach (string name in GetClientNames(client))
				{
					string candidate = Path.Combine(dir.Trim(), name);
					if (File.Exists(candidate))
						return candidate;
				}
			}

			return null;
		}

		private static IEnumerable<string> GetClientCandidates(string client, string appData)
		{
			foreach (string name in GetClientNames(client))
				yield return Path.Combine(appData, "npm", name);
		}

		private static IEnumerable<string> GetClientNames(string client)
		{
			if (string.Equals(client, "claude", StringComparison.OrdinalIgnoreCase))
			{
				yield return "claude.cmd";
				yield return "claude.exe";
				yield return "claude";
				yield return "claude-code.cmd";
				yield return "claude-code.exe";
				yield return "claude-code";
				yield break;
			}

			yield return client + ".exe";
			yield return client + ".cmd";
			yield return client + ".bat";
			yield return client;
		}

		private static string GetAddArguments(string client)
		{
			if (string.Equals(client, "claude", StringComparison.OrdinalIgnoreCase))
				return "mcp add --scope user " + ServerName + " -- " + QuoteArg(GetStableHelperPath());

			return "mcp add " + ServerName + " -- " + QuoteArg(GetStableHelperPath());
		}

		private static string GetRemoveArguments(string client)
		{
			if (string.Equals(client, "claude", StringComparison.OrdinalIgnoreCase))
				return "mcp remove --scope user " + ServerName;

			return "mcp remove " + ServerName;
		}

		private static async Task<CommandResult> ProbeHelperAsync(string helperPath)
		{
			return await Task.Run(() =>
			{
				Process process = new Process();
				try
				{
					process.StartInfo = new ProcessStartInfo
					{
						FileName = helperPath,
						UseShellExecute = false,
						RedirectStandardInput = true,
						RedirectStandardOutput = true,
						RedirectStandardError = true,
						CreateNoWindow = true,
						WorkingDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
					};

					process.Start();
					Task<string> outputTask = process.StandardOutput.ReadLineAsync();
					Task<string> errorTask = process.StandardError.ReadToEndAsync();
					process.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":0,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2024-11-05\",\"capabilities\":{},\"clientInfo\":{\"name\":\"ClaudeVS\",\"version\":\"1.0\"}}}");
					process.StandardInput.Flush();

					if (!outputTask.Wait(5000))
					{
						TryKill(process);
						return new CommandResult(false, -1, "", "Timed out waiting for MCP initialize response.");
					}

					string output = outputTask.Result ?? "";
					TryKill(process);
					string error = errorTask.Wait(1000) ? errorTask.Result : "";
					return new CommandResult(output.Contains("\"serverInfo\""), 0, output, error);
				}
				catch (Exception ex)
				{
					TryKill(process);
					return new CommandResult(false, -1, "", ex.Message);
				}
				finally
				{
					process.Dispose();
				}
			});
		}

		private static async Task<CommandResult> RunClientCommandAsync(string executable, string arguments, int timeoutMs)
		{
			return await Task.Run(() =>
			{
				Process process = new Process();
				try
				{
					ProcessStartInfo startInfo = CreateClientStartInfo(executable, arguments);
					process.StartInfo = startInfo;
					process.Start();

					Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
					Task<string> errorTask = process.StandardError.ReadToEndAsync();
					if (!process.WaitForExit(timeoutMs))
					{
						TryKill(process);
						return new CommandResult(false, -1, GetTaskResult(outputTask), "Timed out running " + Path.GetFileName(executable) + ".");
					}

					Task.WaitAll(new Task[] { outputTask, errorTask }, 1000);
					string output = GetTaskResult(outputTask);
					string error = GetTaskResult(errorTask);
					return new CommandResult(process.ExitCode == 0, process.ExitCode, output, error);
				}
				catch (Exception ex)
				{
					return new CommandResult(false, -1, "", ex.Message);
				}
				finally
				{
					process.Dispose();
				}
			});
		}

		private static ProcessStartInfo CreateClientStartInfo(string executable, string arguments)
		{
			ProcessStartInfo startInfo = new ProcessStartInfo
			{
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true,
				WindowStyle = ProcessWindowStyle.Hidden
			};

			string extension = Path.GetExtension(executable);
			if (string.Equals(extension, ".cmd", StringComparison.OrdinalIgnoreCase) || string.Equals(extension, ".bat", StringComparison.OrdinalIgnoreCase))
			{
				startInfo.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
				startInfo.Arguments = "/d /s /c \"\"" + executable + "\" " + arguments + "\"";
			}
			else
			{
				startInfo.FileName = executable;
				startInfo.Arguments = arguments;
			}

			return startInfo;
		}

		private static string GetTaskResult(Task<string> task)
		{
			try
			{
				if (task.IsCompleted)
					return task.Result ?? "";
			}
			catch
			{
			}

			return "";
		}

		private static void TryKill(Process process)
		{
			try
			{
				if (process != null && !process.HasExited)
					process.Kill();
			}
			catch
			{
			}
		}

		private static bool LooksAlreadyConfigured(string output)
		{
			if (string.IsNullOrEmpty(output))
				return false;

			return output.IndexOf("already", StringComparison.OrdinalIgnoreCase) >= 0
				|| output.IndexOf("exists", StringComparison.OrdinalIgnoreCase) >= 0
				|| output.IndexOf("duplicate", StringComparison.OrdinalIgnoreCase) >= 0;
		}

		private static bool PathAppearsInOutput(string output, string path)
		{
			if (string.IsNullOrEmpty(output) || string.IsNullOrEmpty(path))
				return false;

			return output.IndexOf(path, StringComparison.OrdinalIgnoreCase) >= 0
				|| output.IndexOf(path.Replace("\\", "\\\\"), StringComparison.OrdinalIgnoreCase) >= 0;
		}

		private static string QuoteArg(string value)
		{
			if (value == null)
				return "\"\"";

			return "\"" + value.Replace("\"", "\\\"") + "\"";
		}

		private static string Limit(string value, int maxLength)
		{
			if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
				return value ?? "";

			return value.Substring(0, maxLength);
		}
	}

	internal sealed class McpSetupResult
	{
		public McpSetupResult(bool success, string message)
		{
			Success = success;
			Message = message;
		}

		public bool Success
		{
			get;
		}

		public string Message
		{
			get;
		}

		public static McpSetupResult CreateSuccess(string message)
		{
			return new McpSetupResult(true, message);
		}

		public static McpSetupResult CreateFailure(string message)
		{
			return new McpSetupResult(false, message);
		}
	}

	internal sealed class CommandResult
	{
		public CommandResult(bool success, int exitCode, string output, string error)
		{
			Success = success;
			ExitCode = exitCode;
			Output = output ?? "";
			Error = error ?? "";
		}

		public bool Success
		{
			get;
		}

		public int ExitCode
		{
			get;
		}

		public string Output
		{
			get;
		}

		public string Error
		{
			get;
		}

		public string CombinedOutput
		{
			get
			{
				if (string.IsNullOrWhiteSpace(Error))
					return Output;

				if (string.IsNullOrWhiteSpace(Output))
					return Error;

				return Output + Environment.NewLine + Error;
			}
		}
	}
}
