namespace ClaudeVS.DebuggerMcp
{
	using System;
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.IO;
	using System.IO.Pipes;
	using System.Linq;
	using System.Security.Principal;
	using System.Text;
	using System.Threading.Tasks;
	using System.Web.Script.Serialization;

	internal static class Program
	{
		private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
		private static readonly Encoding Utf8 = new UTF8Encoding(false);
		private static readonly Encoding Ascii = Encoding.ASCII;
		private static readonly object OutputLock = new object();
		private const int BridgeTimeoutMs = 5000;
		private static int? forcedVsPid;
		private static string forcedVsPidError;

		private static int Main(string[] args)
		{
			ParseArgs(args);
			Stream input = Console.OpenStandardInput();
			Stream output = Console.OpenStandardOutput();

			while (true)
			{
				MessageEnvelope message = ReadMessage(input);
				if (message == null)
					return 0;

				Dictionary<string, object> request = null;
				object id = null;
				try
				{
					request = Serializer.Deserialize<Dictionary<string, object>>(message.Content);
					if (request != null && request.TryGetValue("id", out object requestId))
						id = requestId;

					Dictionary<string, object> response = ProcessRequest(request);
					if (response != null)
						WriteMessage(output, Serializer.Serialize(response), message.IsJsonLine);
				}
				catch (Exception ex)
				{
					if (id != null)
						WriteMessage(output, Serializer.Serialize(ErrorResponse(id, -32603, ex.Message)), message.IsJsonLine);
				}
			}
		}

		private static Dictionary<string, object> ProcessRequest(Dictionary<string, object> request)
		{
			if (request == null)
				return ErrorResponse(null, -32600, "Invalid JSON-RPC request.");

			bool hasId = request.TryGetValue("id", out object id);
			string method = GetString(request, "method");
			Dictionary<string, object> parameters = GetDictionary(request, "params");

			if (!hasId)
				return null;

			switch (method)
			{
				case "initialize":
					return SuccessResponse(id, InitializeResult(parameters));
				case "initialized":
				case "notifications/initialized":
					return null;
				case "ping":
					return SuccessResponse(id, new Dictionary<string, object>());
				case "tools/list":
					return SuccessResponse(id, new Dictionary<string, object> { { "_meta", new Dictionary<string, object>() }, { "tools", GetTools() } });
				case "tools/call":
					return SuccessResponse(id, CallTool(parameters));
				case "resources/list":
					return SuccessResponse(id, new Dictionary<string, object> { { "resources", new object[0] } });
				case "resources/templates/list":
					return SuccessResponse(id, new Dictionary<string, object> { { "resourceTemplates", new object[0] } });
				case "prompts/list":
					return SuccessResponse(id, new Dictionary<string, object> { { "prompts", new object[0] } });
				default:
					if (!hasId)
						return null;
					return ErrorResponse(id, -32601, "Unknown MCP method: " + method);
			}
		}

		private static Dictionary<string, object> InitializeResult(Dictionary<string, object> parameters)
		{
			string protocolVersion = GetString(parameters, "protocolVersion");
			if (string.IsNullOrWhiteSpace(protocolVersion))
				protocolVersion = "2024-11-05";

			return new Dictionary<string, object>
			{
				{ "protocolVersion", protocolVersion },
				{ "capabilities", new Dictionary<string, object>
					{
						{ "tools", new Dictionary<string, object> { { "listChanged", false } } },
						{ "resources", new Dictionary<string, object> { { "listChanged", false }, { "subscribe", false } } },
						{ "prompts", new Dictionary<string, object> { { "listChanged", false } } }
					}
				},
				{ "serverInfo", new Dictionary<string, object> { { "name", "claudevs-debugger" }, { "title", "ClaudeVS Debugger" }, { "version", "1.0.0" } } }
			};
		}

		private static object CallTool(Dictionary<string, object> parameters)
		{
			string toolName = GetString(parameters, "name");
			Dictionary<string, object> arguments = GetDictionary(parameters, "arguments") ?? new Dictionary<string, object>();

			if (string.IsNullOrWhiteSpace(toolName))
				return ToolError("MCP tool name is required.", null);

			List<BridgeInstance> instances = LoadActiveInstances();
			BridgeSelection selection = SelectBridge(instances);
			if (selection.Error != null)
				return ToolError(selection.Error, instances);

			try
			{
				Dictionary<string, object> bridgeResponse = SendBridgeRequest(selection.Instance, toolName, arguments);
				if (!GetBool(bridgeResponse, "ok"))
					return ToolError(GetString(bridgeResponse, "error") ?? "Debugger bridge returned an error.", instances);

				object result = bridgeResponse.ContainsKey("result") ? bridgeResponse["result"] : null;
				return ToolSuccess(result);
			}
			catch (Exception ex)
			{
				return ToolError(ex.Message, instances);
			}
		}

		private static Dictionary<string, object> SendBridgeRequest(BridgeInstance instance, string toolName, Dictionary<string, object> arguments)
		{
			using (NamedPipeClientStream pipe = new NamedPipeClientStream(".", instance.PipeName, PipeDirection.InOut, PipeOptions.None, TokenImpersonationLevel.Impersonation))
			{
				pipe.Connect(BridgeTimeoutMs);
				using (StreamReader reader = new StreamReader(pipe, Utf8))
				using (StreamWriter writer = new StreamWriter(pipe, Utf8) { AutoFlush = true })
				{
					Dictionary<string, object> request = new Dictionary<string, object>
					{
						{ "token", instance.Token },
						{ "tool", toolName },
						{ "arguments", arguments }
					};
					WriteLineWithTimeout(writer, Serializer.Serialize(request));
					string line = ReadLineWithTimeout(reader);
					if (string.IsNullOrWhiteSpace(line))
						throw new InvalidOperationException("Debugger bridge did not return a response.");

					return Serializer.Deserialize<Dictionary<string, object>>(line);
				}
			}
		}

		private static BridgeSelection SelectBridge(List<BridgeInstance> instances)
		{
			if (!string.IsNullOrEmpty(forcedVsPidError))
				return BridgeSelection.FromError(forcedVsPidError);

			if (forcedVsPid.HasValue)
			{
				BridgeInstance forced = instances.FirstOrDefault(i => i.Pid == forcedVsPid.Value);
				if (forced == null)
					return BridgeSelection.FromError("No active ClaudeVS debugger bridge is available for --vs-pid " + forcedVsPid.Value + ".");

				return BridgeSelection.FromInstance(forced);
			}

			string currentDirectory = GetNormalizedDirectory(Environment.CurrentDirectory);
			List<BridgeInstance> matches = instances.Where(i => IsSameOrUnder(currentDirectory, i.SolutionDirectory)).ToList();
			if (matches.Count == 1)
				return BridgeSelection.FromInstance(matches[0]);

			if (matches.Count > 1)
				return BridgeSelection.FromError("Multiple Visual Studio instances match " + currentDirectory + ". Append --vs-pid <pid> to select one.");

			return BridgeSelection.FromError("No active Visual Studio instance matches " + currentDirectory + ". Start the agent from the solution directory or append --vs-pid <pid>.");
		}

		private static List<BridgeInstance> LoadActiveInstances()
		{
			List<BridgeInstance> instances = new List<BridgeInstance>();
			string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClaudeVS", "DebuggerBridge", "instances");
			if (!Directory.Exists(directory))
				return instances;

			foreach (string file in Directory.GetFiles(directory, "*.json"))
			{
				try
				{
					Dictionary<string, object> record = Serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(file, Utf8));
					BridgeInstance instance = BridgeInstance.FromRecord(record);
					if (instance != null && IsActiveProcess(instance.Pid))
						instances.Add(instance);
				}
				catch
				{
				}
			}

			return instances;
		}

		private static bool IsActiveProcess(int pid)
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

		private static object ToolSuccess(object payload)
		{
			return new Dictionary<string, object>
			{
				{ "content", new object[] { new Dictionary<string, object> { { "type", "text" }, { "text", Serializer.Serialize(payload ?? new Dictionary<string, object>()) } } } },
				{ "structuredContent", payload ?? new Dictionary<string, object>() },
				{ "isError", false }
			};
		}

		private static object ToolError(string message, List<BridgeInstance> instances)
		{
			Dictionary<string, object> payload = new Dictionary<string, object>
			{
				{ "error", message },
				{ "cwd", Environment.CurrentDirectory },
				{ "availableInstances", GetAvailableInstances(instances ?? LoadActiveInstances()) },
				{ "override", "--vs-pid <pid>" }
			};

			return new Dictionary<string, object>
			{
				{ "content", new object[] { new Dictionary<string, object> { { "type", "text" }, { "text", Serializer.Serialize(payload) } } } },
				{ "structuredContent", payload },
				{ "isError", true }
			};
		}

		private static object GetAvailableInstances(List<BridgeInstance> instances)
		{
			if (instances == null)
				return new object[0];

			return instances.Select(i => new Dictionary<string, object>
			{
				{ "pid", i.Pid },
				{ "solutionPath", i.SolutionPath },
				{ "solutionDirectory", i.SolutionDirectory },
				{ "version", i.Version },
				{ "timestampUtc", i.TimestampUtc }
			}).ToList();
		}

		private static List<object> GetTools()
		{
			return new List<object>
			{
				Tool("debugger_status", "Return the active Visual Studio debugger state, solution, process, thread, frame, break reason, and exception summary.", EmptySchema()),
				Tool("debugger_threads", "Return current process debugger threads.", EmptySchema()),
				Tool("debugger_call_stack", "Return debugger call stack frames for the current or requested thread.", ObjectSchema(
					Property("threadId", "integer", "Debugger thread id."),
					Property("maxFrames", "integer", "Maximum number of frames to return."))),
				Tool("debugger_select_frame", "Select a debugger stack frame in Visual Studio for the current or requested thread.", ObjectSchema(new[] { "frameIndex" },
					Property("threadId", "integer", "Debugger thread id."),
					Property("frameIndex", "integer", "Zero-based stack frame index.")), false, false),
				Tool("debugger_locals", "Return bounded local variables for the current or requested stack frame.", ObjectSchema(
					Property("threadId", "integer", "Debugger thread id."),
					Property("frameIndex", "integer", "Zero-based stack frame index."),
					Property("maxDepth", "integer", "Maximum nested child depth. Defaults to 0."),
					Property("maxChildren", "integer", "Maximum children per expression."))),
				Tool("debugger_evaluate", "Evaluate an expression in the current or requested stack frame.", ObjectSchema(new[] { "expression" },
					Property("expression", "string", "Expression to evaluate."),
					Property("threadId", "integer", "Debugger thread id."),
					Property("frameIndex", "integer", "Zero-based stack frame index."),
					Property("timeoutMs", "integer", "Evaluation timeout in milliseconds.")), false, false),
				Tool("debugger_exception", "Return current or last captured debugger exception details.", EmptySchema()),
				Tool("debugger_output", "Return recent lines from a Visual Studio output pane.", ObjectSchema(
					Property("pane", "string", "Output pane name. Defaults to Debug."),
					Property("lineCount", "integer", "Number of recent lines to return."))),
				Tool("debugger_breakpoints", "Return Visual Studio debugger breakpoints and last-hit marker.", EmptySchema()),
				Tool("debugger_terminate", "Terminate the active Visual Studio debugging session, if any.", EmptySchema(), false, true)
			};
		}

		private static Dictionary<string, object> Tool(string name, string description, Dictionary<string, object> inputSchema)
		{
			return Tool(name, description, inputSchema, true, false);
		}

		private static Dictionary<string, object> Tool(string name, string description, Dictionary<string, object> inputSchema, bool readOnly, bool destructive)
		{
			return new Dictionary<string, object>
			{
				{ "name", name },
				{ "title", name },
				{ "description", description },
				{ "inputSchema", inputSchema },
				{ "annotations", new Dictionary<string, object> { { "readOnlyHint", readOnly }, { "destructiveHint", destructive }, { "openWorldHint", false } } }
			};
		}

		private static Dictionary<string, object> EmptySchema()
		{
			return ObjectSchema(new KeyValuePair<string, Dictionary<string, object>>[0]);
		}

		private static Dictionary<string, object> ObjectSchema(params KeyValuePair<string, Dictionary<string, object>>[] properties)
		{
			return ObjectSchema(new string[0], properties);
		}

		private static Dictionary<string, object> ObjectSchema(string[] required, params KeyValuePair<string, Dictionary<string, object>>[] properties)
		{
			return new Dictionary<string, object>
			{
				{ "type", "object" },
				{ "title", "debugger_tool_input" },
				{ "properties", properties.ToDictionary(p => p.Key, p => (object)p.Value) },
				{ "required", required ?? new string[0] },
				{ "additionalProperties", false }
			};
		}

		private static KeyValuePair<string, Dictionary<string, object>> Property(string name, string type, string description)
		{
			return new KeyValuePair<string, Dictionary<string, object>>(name, new Dictionary<string, object>
			{
				{ "type", type },
				{ "title", name },
				{ "description", description }
			});
		}

		private static Dictionary<string, object> SuccessResponse(object id, object result)
		{
			return new Dictionary<string, object>
			{
				{ "jsonrpc", "2.0" },
				{ "id", id },
				{ "result", result }
			};
		}

		private static Dictionary<string, object> ErrorResponse(object id, int code, string message)
		{
			return new Dictionary<string, object>
			{
				{ "jsonrpc", "2.0" },
				{ "id", id },
				{ "error", new Dictionary<string, object> { { "code", code }, { "message", message } } }
			};
		}

		private static MessageEnvelope ReadMessage(Stream input)
		{
			int firstByte = input.ReadByte();
			if (firstByte < 0)
				return null;

			if (firstByte == '{' || firstByte == '[')
			{
				return new MessageEnvelope
				{
					Content = ReadJsonLine(input, (byte)firstByte),
					IsJsonLine = true
				};
			}

			string headers = ReadHeaders(input, (byte)firstByte);
			if (headers == null)
				return null;

			int contentLength = 0;
			foreach (string line in headers.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
			{
				int colon = line.IndexOf(':');
				if (colon <= 0)
					continue;

				string name = line.Substring(0, colon).Trim();
				string value = line.Substring(colon + 1).Trim();
				if (string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase))
					contentLength = int.Parse(value);
			}

			if (contentLength <= 0)
				throw new InvalidOperationException("MCP message is missing Content-Length.");

			byte[] payload = new byte[contentLength];
			int offset = 0;
			while (offset < contentLength)
			{
				int read = input.Read(payload, offset, contentLength - offset);
				if (read <= 0)
					throw new EndOfStreamException();

				offset += read;
			}

			return new MessageEnvelope
			{
				Content = Utf8.GetString(payload),
				IsJsonLine = false
			};
		}

		private static string ReadHeaders(Stream input, byte firstByte)
		{
			List<byte> bytes = new List<byte>();
			int lineLength = 0;
			bytes.Add(firstByte);
			if (firstByte != 13 && firstByte != 10)
				lineLength = 1;

			while (true)
			{
				int value = input.ReadByte();
				if (value < 0)
					return null;

				byte b = (byte)value;
				bytes.Add(b);

				if (b == 10)
				{
					if (lineLength == 0)
						break;
					lineLength = 0;
				}
				else if (b == 13)
				{
				}
				else
				{
					lineLength++;
				}
			}

			return Ascii.GetString(bytes.ToArray());
		}

		private static string ReadJsonLine(Stream input, byte firstByte)
		{
			List<byte> bytes = new List<byte>();
			bytes.Add(firstByte);

			while (true)
			{
				int value = input.ReadByte();
				if (value < 0)
					break;

				byte b = (byte)value;
				if (b == 10)
					break;
				if (b == 13)
					continue;

				bytes.Add(b);
			}

			return Utf8.GetString(bytes.ToArray());
		}

		private static void WriteMessage(Stream output, string json, bool jsonLine)
		{
			byte[] payload = Utf8.GetBytes(json);

			lock (OutputLock)
			{
				if (jsonLine)
				{
					output.Write(payload, 0, payload.Length);
					output.WriteByte((byte)'\n');
				}
				else
				{
					byte[] header = Ascii.GetBytes("Content-Length: " + payload.Length + "\r\n\r\n");
					output.Write(header, 0, header.Length);
					output.Write(payload, 0, payload.Length);
				}
				output.Flush();
			}
		}

		private static void WriteLineWithTimeout(StreamWriter writer, string line)
		{
			Task task = writer.WriteLineAsync(line);
			WaitForBridgeTask(task, "Debugger bridge write timed out after 5 seconds.");
		}

		private static string ReadLineWithTimeout(StreamReader reader)
		{
			Task<string> task = reader.ReadLineAsync();
			WaitForBridgeTask(task, "Debugger bridge read timed out after 5 seconds.");
			return task.Result;
		}

		private static void WaitForBridgeTask(Task task, string timeoutMessage)
		{
			try
			{
				if (!task.Wait(BridgeTimeoutMs))
					throw new TimeoutException(timeoutMessage);
			}
			catch (AggregateException ex)
			{
				throw ex.InnerException ?? ex;
			}
		}

		private static void ParseArgs(string[] args)
		{
			for (int i = 0; i < args.Length; i++)
			{
				string arg = args[i];
				string value = null;
				if (string.Equals(arg, "--vs-pid", StringComparison.OrdinalIgnoreCase))
				{
					if (i + 1 < args.Length)
						value = args[++i];
				}
				else if (arg.StartsWith("--vs-pid=", StringComparison.OrdinalIgnoreCase))
				{
					value = arg.Substring("--vs-pid=".Length);
				}

				if (value != null)
				{
					if (int.TryParse(value, out int parsed) && parsed > 0)
						forcedVsPid = parsed;
					else
						forcedVsPidError = "Invalid --vs-pid value: " + value;
				}
			}
		}

		private static bool IsSameOrUnder(string currentDirectory, string solutionDirectory)
		{
			if (string.IsNullOrWhiteSpace(currentDirectory) || string.IsNullOrWhiteSpace(solutionDirectory))
				return false;

			string current = EnsureTrailingSeparator(GetNormalizedDirectory(currentDirectory));
			string solution = EnsureTrailingSeparator(GetNormalizedDirectory(solutionDirectory));
			return current.StartsWith(solution, StringComparison.OrdinalIgnoreCase);
		}

		private static string GetNormalizedDirectory(string path)
		{
			if (string.IsNullOrWhiteSpace(path))
				return null;

			try
			{
				return Path.GetFullPath(path);
			}
			catch
			{
				return path;
			}
		}

		private static string EnsureTrailingSeparator(string path)
		{
			if (string.IsNullOrEmpty(path))
				return path;

			char last = path[path.Length - 1];
			if (last == Path.DirectorySeparatorChar || last == Path.AltDirectorySeparatorChar)
				return path;

			return path + Path.DirectorySeparatorChar;
		}

		private static string GetString(Dictionary<string, object> dictionary, string key)
		{
			if (dictionary == null || !dictionary.TryGetValue(key, out object value) || value == null)
				return null;

			return value.ToString();
		}

		private static Dictionary<string, object> GetDictionary(Dictionary<string, object> dictionary, string key)
		{
			if (dictionary == null || !dictionary.TryGetValue(key, out object value) || value == null)
				return null;

			return value as Dictionary<string, object>;
		}

		private static bool GetBool(Dictionary<string, object> dictionary, string key)
		{
			if (dictionary == null || !dictionary.TryGetValue(key, out object value) || value == null)
				return false;

			try
			{
				return Convert.ToBoolean(value);
			}
			catch
			{
				return false;
			}
		}

		private sealed class BridgeSelection
		{
			public BridgeInstance Instance { get; private set; }
			public string Error { get; private set; }

			public static BridgeSelection FromInstance(BridgeInstance instance)
			{
				return new BridgeSelection { Instance = instance };
			}

			public static BridgeSelection FromError(string error)
			{
				return new BridgeSelection { Error = error };
			}
		}

		private sealed class BridgeInstance
		{
			public int Pid { get; private set; }
			public string PipeName { get; private set; }
			public string Token { get; private set; }
			public string SolutionPath { get; private set; }
			public string SolutionDirectory { get; private set; }
			public string Version { get; private set; }
			public string TimestampUtc { get; private set; }

			public static BridgeInstance FromRecord(Dictionary<string, object> record)
			{
				if (record == null)
					return null;

				int pid = Convert.ToInt32(record["pid"]);
				string pipeName = GetRecordString(record, "pipeName");
				string token = GetRecordString(record, "token");
				if (pid <= 0 || string.IsNullOrWhiteSpace(pipeName) || string.IsNullOrWhiteSpace(token))
					return null;

				string solutionPath = GetRecordString(record, "solutionPath");
				string solutionDirectory = GetSolutionDirectory(solutionPath);
				return new BridgeInstance
				{
					Pid = pid,
					PipeName = pipeName,
					Token = token,
					SolutionPath = solutionPath,
					SolutionDirectory = solutionDirectory,
					Version = GetRecordString(record, "version"),
					TimestampUtc = GetRecordString(record, "timestampUtc")
				};
			}

			private static string GetRecordString(Dictionary<string, object> record, string key)
			{
				if (!record.TryGetValue(key, out object value) || value == null)
					return null;

				return value.ToString();
			}

			private static string GetSolutionDirectory(string solutionPath)
			{
				if (string.IsNullOrWhiteSpace(solutionPath))
					return null;

				try
				{
					if (Directory.Exists(solutionPath))
						return Path.GetFullPath(solutionPath);

					return Path.GetDirectoryName(Path.GetFullPath(solutionPath));
				}
				catch
				{
					return null;
				}
			}
		}

		private sealed class MessageEnvelope
		{
			public string Content { get; set; }
			public bool IsJsonLine { get; set; }
		}
	}
}
