namespace ClaudeVS
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.IO.Pipes;
	using System.Linq;
	using System.Reflection;
	using System.Runtime.InteropServices;
	using System.Security.AccessControl;
	using System.Security.Cryptography;
	using System.Security.Principal;
	using System.Text;
	using System.Text.RegularExpressions;
	using System.Threading;
	using System.Threading.Tasks;
	using System.Web.Script.Serialization;
	using EnvDTE;
	using EnvDTE80;
	using EnvDTE90a;
	using Microsoft.VisualStudio;
	using Microsoft.VisualStudio.Debugger.Interop;
	using Microsoft.VisualStudio.Shell;
	using Microsoft.VisualStudio.Shell.Interop;
	using Process = System.Diagnostics.Process;
	using Task = System.Threading.Tasks.Task;

	internal sealed class VsDebuggerBridgeService : IDebugEventCallback2, IDisposable
	{
		private readonly AsyncPackage package;
		private readonly int devenvPid;
		private readonly string pipeName;
		private readonly string token;
		private readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
		private readonly JavaScriptSerializer serializer = new JavaScriptSerializer();
		private readonly object exceptionLock = new object();
		private DTE2 dte;
		private DebuggerEvents debuggerEvents;
		private SolutionEvents solutionEvents;
		private Task pipeTask;
		private string discoveryFilePath;
		private string lastExceptionType;
		private string lastExceptionName;
		private uint lastExceptionCode;
		private string lastExceptionDescription;
		private bool disposed;

		public VsDebuggerBridgeService(AsyncPackage package)
		{
			this.package = package ?? throw new ArgumentNullException(nameof(package));
			devenvPid = Process.GetCurrentProcess().Id;
			pipeName = "ClaudeVS.DebuggerBridge." + devenvPid + "." + Guid.NewGuid().ToString("N");
			token = CreateToken();
			serializer.MaxJsonLength = int.MaxValue;
		}

		public static VsDebuggerBridgeService Current
		{
			get;
			private set;
		}

		public async Task InitializeAsync(CancellationToken cancellationToken)
		{
			await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

			Current = this;
			dte = Package.GetGlobalService(typeof(DTE)) as DTE2;
			SubscribeToDebuggerEvents();
			SubscribeToSolutionEvents();
			McpSetup.CleanupStaleDiscoveryRecords();
			CopyHelperToStablePath();
			WriteDiscoveryRecord();
			pipeTask = Task.Run(() => RunPipeServerAsync(cancellationTokenSource.Token));
		}

		public int Event(IDebugEngine2 pEngine, IDebugProcess2 pProcess, IDebugProgram2 pProgram, IDebugThread2 pThread, IDebugEvent2 pEvent, ref Guid riidEvent, uint dwAttrib)
		{
			try
			{
				if (pEvent is IDebugExceptionEvent2 exceptionEvent)
				{
					EXCEPTION_INFO[] exInfo = new EXCEPTION_INFO[1];
					string description = null;
					if (exceptionEvent.GetException(exInfo) == VSConstants.S_OK)
					{
						lock (exceptionLock)
						{
							lastExceptionName = exInfo[0].bstrExceptionName;
							lastExceptionCode = exInfo[0].dwCode;
						}
					}

					if (exceptionEvent.GetExceptionDescription(out description) == VSConstants.S_OK)
					{
						lock (exceptionLock)
						{
							lastExceptionDescription = description;
						}
					}
				}
			}
			catch
			{
			}

			return VSConstants.S_OK;
		}

		public void Dispose()
		{
			if (disposed)
				return;

			disposed = true;
			cancellationTokenSource.Cancel();
			DeleteDiscoveryRecord();
			cancellationTokenSource.Dispose();

			if (Current == this)
				Current = null;
		}

		private void SubscribeToDebuggerEvents()
		{
			ThreadHelper.ThrowIfNotOnUIThread();

			try
			{
				IVsDebugger debugger = Package.GetGlobalService(typeof(SVsShellDebugger)) as IVsDebugger;
				if (debugger != null)
					debugger.AdviseDebugEventCallback(this);
			}
			catch
			{
			}

			try
			{
				if (dte != null)
				{
					debuggerEvents = dte.Events.DebuggerEvents;
					debuggerEvents.OnExceptionThrown += OnExceptionThrown;
					debuggerEvents.OnEnterBreakMode += OnEnterBreakMode;
				}
			}
			catch
			{
			}
		}

		private void SubscribeToSolutionEvents()
		{
			ThreadHelper.ThrowIfNotOnUIThread();

			try
			{
				if (dte != null)
				{
					solutionEvents = dte.Events.SolutionEvents;
					solutionEvents.Opened += OnSolutionOpened;
					solutionEvents.AfterClosing += OnSolutionAfterClosing;
				}
			}
			catch
			{
			}
		}

		private void OnSolutionOpened()
		{
			ThreadHelper.ThrowIfNotOnUIThread();
			WriteDiscoveryRecord();
		}

		private void OnSolutionAfterClosing()
		{
			ThreadHelper.ThrowIfNotOnUIThread();
			WriteDiscoveryRecord();
		}

		private void OnExceptionThrown(string exceptionType, string name, int code, string description, ref dbgExceptionAction exceptionAction)
		{
			lock (exceptionLock)
			{
				lastExceptionType = exceptionType;
				lastExceptionName = name;
				lastExceptionCode = (uint)code;
				lastExceptionDescription = description;
			}
		}

		private void OnEnterBreakMode(dbgEventReason reason, ref dbgExecutionAction executionAction)
		{
			if (reason == dbgEventReason.dbgEventReasonExceptionThrown || reason == dbgEventReason.dbgEventReasonExceptionNotHandled)
				return;

			lock (exceptionLock)
			{
				lastExceptionType = null;
				lastExceptionName = null;
				lastExceptionCode = 0;
				lastExceptionDescription = null;
			}
		}

		private async Task RunPipeServerAsync(CancellationToken cancellationToken)
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				NamedPipeServerStream server = null;
				try
				{
					server = CreatePipeServer();
					await server.WaitForConnectionAsync(cancellationToken);
					NamedPipeServerStream connectedServer = server;
					server = null;
					_ = Task.Run(() => HandlePipeClient(connectedServer), cancellationToken);
				}
				catch (OperationCanceledException)
				{
					if (server != null)
						server.Dispose();
					break;
				}
				catch
				{
					if (server != null)
						server.Dispose();

					if (!cancellationToken.IsCancellationRequested)
					{
						try
						{
							await Task.Delay(250, cancellationToken);
						}
						catch (OperationCanceledException)
						{
						}
					}
				}
			}
		}

		private NamedPipeServerStream CreatePipeServer()
		{
			PipeSecurity security = new PipeSecurity();
			SecurityIdentifier user = WindowsIdentity.GetCurrent().User;
			security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));

			return new NamedPipeServerStream(
				pipeName,
				PipeDirection.InOut,
				NamedPipeServerStream.MaxAllowedServerInstances,
				PipeTransmissionMode.Byte,
				PipeOptions.Asynchronous,
				65536,
				65536,
				security);
		}

		private void HandlePipeClient(NamedPipeServerStream pipe)
		{
			using (pipe)
			using (StreamReader reader = new StreamReader(pipe, new UTF8Encoding(false)))
			using (StreamWriter writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true })
			{
				try
				{
					string line = reader.ReadLine();
					if (string.IsNullOrWhiteSpace(line))
						return;

					Dictionary<string, object> request = serializer.Deserialize<Dictionary<string, object>>(line);
					string requestToken = GetString(request, "token");
					if (!string.Equals(requestToken, token, StringComparison.Ordinal))
					{
						WritePipeResponse(writer, false, null, "Debugger bridge authentication failed.");
						return;
					}

					string tool = GetString(request, "tool");
					Dictionary<string, object> arguments = GetDictionary(request, "arguments") ?? new Dictionary<string, object>();
					object result = ExecuteToolOnMainThread(tool, arguments);
					WritePipeResponse(writer, true, result, null);
				}
				catch (Exception ex)
				{
					WritePipeResponse(writer, false, null, ex.Message);
				}
			}
		}

		private void WritePipeResponse(StreamWriter writer, bool ok, object result, string error)
		{
			Dictionary<string, object> response = new Dictionary<string, object>
			{
				{ "ok", ok }
			};

			if (ok)
				response["result"] = result;
			else
				response["error"] = error ?? "Debugger bridge request failed.";

			writer.WriteLine(serializer.Serialize(response));
		}

		private object ExecuteToolOnMainThread(string tool, Dictionary<string, object> arguments)
		{
			return ThreadHelper.JoinableTaskFactory.Run(async delegate
			{
				await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
				return ExecuteTool(tool, arguments);
			});
		}

		private object ExecuteTool(string tool, Dictionary<string, object> arguments)
		{
			ThreadHelper.ThrowIfNotOnUIThread();
			if (dte == null)
				dte = Package.GetGlobalService(typeof(DTE)) as DTE2;

			WriteDiscoveryRecord();

			switch (tool)
			{
				case "debugger_status":
					return GetStatus();
				case "debugger_threads":
					return GetThreads();
				case "debugger_call_stack":
					return GetCallStack(arguments);
				case "debugger_locals":
					return GetLocals(arguments);
				case "debugger_evaluate":
					return Evaluate(arguments);
				case "debugger_exception":
					return GetException();
				case "debugger_output":
					return GetOutput(arguments);
				case "debugger_breakpoints":
					return GetBreakpoints();
				default:
					return Unavailable("Unknown debugger bridge tool: " + tool);
			}
		}

		private Dictionary<string, object> GetStatus()
		{
			ThreadHelper.ThrowIfNotOnUIThread();
			Debugger debugger = dte?.Debugger;
			Dictionary<string, object> status = BaseResult(debugger, true, null);
			status["process"] = GetProcess(debugger);
			status["pid"] = GetDebuggerPid(debugger);
			status["thread"] = GetCurrentThread(debugger);
			status["currentFrame"] = GetCurrentFrame(debugger);
			status["breakReason"] = GetBreakReason(debugger);
			status["exception"] = GetExceptionSnapshot(debugger, true);
			return status;
		}

		private Dictionary<string, object> GetThreads()
		{
			ThreadHelper.ThrowIfNotOnUIThread();
			Debugger debugger = dte?.Debugger;
			Dictionary<string, object> unavailable = RequireBreakMode(debugger);
			if (unavailable != null)
				return unavailable;

			List<object> threads = new List<object>();
			EnvDTE.Thread currentThread = GetSafe(() => debugger.CurrentThread);
			try
			{
				foreach (EnvDTE.Thread thread in debugger.CurrentProgram.Threads)
					threads.Add(GetThread(thread, currentThread));
			}
			catch
			{
			}

			Dictionary<string, object> result = BaseResult(debugger, true, null);
			result["threads"] = threads;
			return result;
		}

		private Dictionary<string, object> GetCallStack(Dictionary<string, object> arguments)
		{
			ThreadHelper.ThrowIfNotOnUIThread();
			Debugger debugger = dte?.Debugger;
			Dictionary<string, object> unavailable = RequireBreakMode(debugger);
			if (unavailable != null)
				return unavailable;

			int maxFrames = Clamp(GetInt(arguments, "maxFrames", 50), 1, 200);
			int? threadId = GetNullableInt(arguments, "threadId");
			EnvDTE.Thread thread = threadId.HasValue ? FindThread(debugger, threadId.Value) : GetSafe(() => debugger.CurrentThread);
			if (thread == null)
				return Unavailable("Requested debugger thread is not available.", debugger);

			List<object> frames = new List<object>();
			EnvDTE.StackFrame currentFrame = GetSafe(() => debugger.CurrentStackFrame);
			int index = 0;
			try
			{
				foreach (EnvDTE.StackFrame frame in thread.StackFrames)
				{
					if (index >= maxFrames)
						break;

					frames.Add(GetStackFrame(frame, index, IsCurrentFrame(thread, frame, currentFrame, debugger)));
					index++;
				}
			}
			catch
			{
			}

			Dictionary<string, object> result = BaseResult(debugger, true, null);
			result["thread"] = GetThread(thread, GetSafe(() => debugger.CurrentThread));
			result["frames"] = frames;
			result["truncated"] = index >= maxFrames;
			return result;
		}

		private Dictionary<string, object> GetLocals(Dictionary<string, object> arguments)
		{
			ThreadHelper.ThrowIfNotOnUIThread();
			Debugger debugger = dte?.Debugger;
			Dictionary<string, object> unavailable = RequireBreakMode(debugger);
			if (unavailable != null)
				return unavailable;

			int? frameIndex = GetNullableInt(arguments, "frameIndex");
			int maxDepth = Clamp(GetInt(arguments, "maxDepth", 2), 0, 5);
			int maxChildren = Clamp(GetInt(arguments, "maxChildren", 50), 1, 200);
			EnvDTE.StackFrame frame = frameIndex.HasValue ? FindFrame(debugger, frameIndex.Value) : GetSafe(() => debugger.CurrentStackFrame);
			if (frame == null)
				return Unavailable("Requested debugger stack frame is not available.", debugger);

			List<object> locals = new List<object>();
			bool truncated = false;
			try
			{
				int count = 0;
				foreach (Expression local in frame.Locals)
				{
					if (count >= maxChildren)
					{
						truncated = true;
						break;
					}

					locals.Add(GetExpressionNode(local, maxDepth, maxChildren));
					count++;
				}
			}
			catch
			{
			}

			Dictionary<string, object> result = BaseResult(debugger, true, null);
			result["frame"] = GetStackFrame(frame, frameIndex ?? -1, true);
			result["locals"] = locals;
			result["truncated"] = truncated;
			return result;
		}

		private Dictionary<string, object> Evaluate(Dictionary<string, object> arguments)
		{
			ThreadHelper.ThrowIfNotOnUIThread();
			Debugger debugger = dte?.Debugger;
			Dictionary<string, object> unavailable = RequireBreakMode(debugger);
			if (unavailable != null)
				return unavailable;

			string expressionText = GetString(arguments, "expression");
			int timeoutMs = Clamp(GetInt(arguments, "timeoutMs", 1000), 100, 5000);
			Dictionary<string, object> result = BaseResult(debugger, true, null);
			result["expression"] = expressionText;

			if (string.IsNullOrWhiteSpace(expressionText))
			{
				result["isValid"] = false;
				result["error"] = "Expression is required.";
				return result;
			}

			if (LooksMutatingExpression(expressionText))
			{
				result["isValid"] = false;
				result["rejected"] = true;
				result["error"] = "Only read-only expressions are allowed.";
				return result;
			}

			try
			{
				Expression expression = debugger.GetExpression(expressionText, false, timeoutMs);
				result["isValid"] = GetSafe(() => expression.IsValidValue);
				result["name"] = GetSafe(() => expression.Name);
				result["type"] = GetSafe(() => expression.Type);
				result["value"] = Limit(GetSafe(() => expression.Value), 4000);
				if (!GetSafe(() => expression.IsValidValue))
					result["error"] = "Expression did not produce a valid value.";
			}
			catch (Exception ex)
			{
				result["isValid"] = false;
				result["error"] = ex.Message;
			}

			return result;
		}

		private Dictionary<string, object> GetException()
		{
			ThreadHelper.ThrowIfNotOnUIThread();
			Debugger debugger = dte?.Debugger;
			Dictionary<string, object> result = BaseResult(debugger, true, null);
			result["exception"] = GetExceptionSnapshot(debugger, true);
			return result;
		}

		private Dictionary<string, object> GetOutput(Dictionary<string, object> arguments)
		{
			ThreadHelper.ThrowIfNotOnUIThread();
			Debugger debugger = dte?.Debugger;
			string paneName = GetString(arguments, "pane");
			if (string.IsNullOrWhiteSpace(paneName))
				paneName = "Debug";

			int lineCount = Clamp(GetInt(arguments, "lineCount", 200), 1, 1000);
			Dictionary<string, object> result = BaseResult(debugger, true, null);
			result["pane"] = paneName;
			result["lineCount"] = lineCount;

			try
			{
				OutputWindowPane pane = FindOutputPane(paneName);
				if (pane == null || pane.TextDocument == null)
				{
					result["available"] = false;
					result["message"] = "Output pane is not available: " + paneName;
					result["lines"] = new object[0];
					return result;
				}

				EditPoint startPoint = pane.TextDocument.StartPoint.CreateEditPoint();
				EditPoint endPoint = pane.TextDocument.EndPoint.CreateEditPoint();
				string text = startPoint.GetText(endPoint) ?? string.Empty;
				List<string> lines = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None).ToList();
				while (lines.Count > 0 && string.IsNullOrEmpty(lines[lines.Count - 1]))
					lines.RemoveAt(lines.Count - 1);

				List<string> selected = lines.Skip(Math.Max(0, lines.Count - lineCount)).ToList();
				result["lines"] = selected;
				result["text"] = string.Join(Environment.NewLine, selected);
			}
			catch (Exception ex)
			{
				result["available"] = false;
				result["message"] = ex.Message;
				result["lines"] = new object[0];
			}

			return result;
		}

		private Dictionary<string, object> GetBreakpoints()
		{
			ThreadHelper.ThrowIfNotOnUIThread();
			Debugger debugger = dte?.Debugger;
			Dictionary<string, object> result = BaseResult(debugger, true, null);
			List<object> breakpoints = new List<object>();
			Breakpoint lastHit = GetSafe(() => debugger?.BreakpointLastHit);

			try
			{
				if (debugger?.Breakpoints != null)
				{
					foreach (Breakpoint breakpoint in debugger.Breakpoints)
						breakpoints.Add(GetBreakpoint(breakpoint, lastHit));
				}
			}
			catch
			{
			}

			result["breakpoints"] = breakpoints;
			return result;
		}

		private Dictionary<string, object> BaseResult(Debugger debugger, bool available, string message)
		{
			Dictionary<string, object> result = new Dictionary<string, object>
			{
				{ "available", available },
				{ "mode", GetMode(debugger) },
				{ "solution", GetSolution() },
				{ "devenvPid", devenvPid }
			};

			if (!string.IsNullOrEmpty(message))
				result["message"] = message;

			return result;
		}

		private Dictionary<string, object> RequireBreakMode(Debugger debugger)
		{
			if (debugger == null)
				return Unavailable("Visual Studio debugger is not available.", debugger);

			dbgDebugMode mode = GetSafe(() => debugger.CurrentMode);
			if (mode != dbgDebugMode.dbgBreakMode)
				return Unavailable("Debugger is in " + GetMode(debugger) + " mode, not break mode.", debugger);

			return null;
		}

		private Dictionary<string, object> Unavailable(string message, Debugger debugger = null)
		{
			Dictionary<string, object> result = BaseResult(debugger ?? dte?.Debugger, false, message);
			result["process"] = GetProcess(debugger ?? dte?.Debugger);
			result["pid"] = GetDebuggerPid(debugger ?? dte?.Debugger);
			return result;
		}

		private Dictionary<string, object> GetSolution()
		{
			ThreadHelper.ThrowIfNotOnUIThread();
			string fullName = GetSafe(() => dte?.Solution?.FullName);
			string directory = null;
			string name = null;
			if (!string.IsNullOrEmpty(fullName))
			{
				directory = Path.GetDirectoryName(fullName);
				name = Path.GetFileName(fullName);
			}

			return new Dictionary<string, object>
			{
				{ "path", fullName },
				{ "directory", directory },
				{ "name", name }
			};
		}

		private Dictionary<string, object> GetProcess(Debugger debugger)
		{
			ThreadHelper.ThrowIfNotOnUIThread();
			EnvDTE.Process process = GetSafe(() => debugger?.CurrentProcess);
			if (process == null)
				return null;

			return new Dictionary<string, object>
			{
				{ "name", GetSafe(() => process.Name) },
				{ "pid", GetSafe(() => process.ProcessID) }
			};
		}

		private object GetDebuggerPid(Debugger debugger)
		{
			ThreadHelper.ThrowIfNotOnUIThread();
			return GetSafe(() => debugger?.CurrentProcess?.ProcessID);
		}

		private Dictionary<string, object> GetCurrentThread(Debugger debugger)
		{
			ThreadHelper.ThrowIfNotOnUIThread();
			EnvDTE.Thread thread = GetSafe(() => debugger?.CurrentThread);
			if (thread == null)
				return null;

			return GetThread(thread, thread);
		}

		private Dictionary<string, object> GetThread(EnvDTE.Thread thread, EnvDTE.Thread currentThread)
		{
			if (thread == null)
				return null;

			int id = GetSafe(() => thread.ID);
			return new Dictionary<string, object>
			{
				{ "id", id },
				{ "name", GetSafe(() => thread.Name) },
				{ "category", GetComString(thread, "Category") },
				{ "current", currentThread != null && id == GetSafe(() => currentThread.ID) }
			};
		}

		private Dictionary<string, object> GetCurrentFrame(Debugger debugger)
		{
			ThreadHelper.ThrowIfNotOnUIThread();
			EnvDTE.StackFrame frame = GetSafe(() => debugger?.CurrentStackFrame);
			if (frame == null)
				return null;

			return GetStackFrame(frame, -1, true);
		}

		private string GetBreakReason(Debugger debugger)
		{
			ThreadHelper.ThrowIfNotOnUIThread();
			if (debugger == null)
				return null;

			return GetSafe(() => debugger.LastBreakReason.ToString());
		}

		private Dictionary<string, object> GetStackFrame(EnvDTE.StackFrame frame, int index, bool current)
		{
			string file = null;
			int line = 0;
			try
			{
				StackFrame2 frame2 = frame as StackFrame2;
				if (frame2 != null)
				{
					file = frame2.FileName;
					line = unchecked((int)frame2.LineNumber);
				}
			}
			catch
			{
			}

			return new Dictionary<string, object>
			{
				{ "index", index },
				{ "function", GetSafe(() => frame.FunctionName) },
				{ "file", file },
				{ "line", line },
				{ "module", GetSafe(() => frame.Module?.ToString()) },
				{ "language", GetSafe(() => frame.Language) },
				{ "current", current }
			};
		}

		private Dictionary<string, object> GetExpressionNode(Expression expression, int depth, int maxChildren)
		{
			Dictionary<string, object> node = new Dictionary<string, object>
			{
				{ "name", GetSafe(() => expression.Name) },
				{ "type", GetSafe(() => expression.Type) },
				{ "value", Limit(GetSafe(() => expression.Value), 2000) },
				{ "isValid", GetSafe(() => expression.IsValidValue) }
			};

			if (depth <= 0)
				return node;

			List<object> children = new List<object>();
			bool hasMore = false;
			try
			{
				int count = 0;
				foreach (Expression child in expression.DataMembers)
				{
					if (count >= maxChildren)
					{
						hasMore = true;
						break;
					}

					children.Add(GetExpressionNode(child, depth - 1, maxChildren));
					count++;
				}
			}
			catch
			{
			}

			if (children.Count > 0)
				node["children"] = children;

			if (hasMore)
				node["hasMoreChildren"] = true;

			return node;
		}

		private Dictionary<string, object> GetExceptionSnapshot(Debugger debugger, bool includeEvaluation)
		{
			ThreadHelper.ThrowIfNotOnUIThread();
			string exceptionType;
			string exceptionName;
			uint exceptionCode;
			string exceptionDescription;

			lock (exceptionLock)
			{
				exceptionType = lastExceptionType;
				exceptionName = lastExceptionName;
				exceptionCode = lastExceptionCode;
				exceptionDescription = lastExceptionDescription;
			}

			Dictionary<string, object> result = new Dictionary<string, object>
			{
				{ "type", exceptionType },
				{ "name", exceptionName },
				{ "code", exceptionCode == 0 ? null : "0x" + exceptionCode.ToString("X8") },
				{ "description", exceptionDescription },
				{ "decodedDescription", exceptionCode == 0 ? null : DecodeSEH(exceptionCode) },
				{ "breakReason", GetBreakReason(debugger) }
			};

			bool hasCaptured = !string.IsNullOrEmpty(exceptionType) || !string.IsNullOrEmpty(exceptionName) || exceptionCode != 0 || !string.IsNullOrEmpty(exceptionDescription);

			if (includeEvaluation && debugger != null && GetSafe(() => debugger.CurrentMode) == dbgDebugMode.dbgBreakMode)
			{
				try
				{
					Expression exception = debugger.GetExpression("$exception", false, 1000);
					if (exception != null && GetSafe(() => exception.IsValidValue))
					{
						result["current"] = GetExpressionNode(exception, 1, 20);
						hasCaptured = true;
					}
				}
				catch
				{
				}

				AddEvaluatedExceptionValue(debugger, result, "$exception.Message", "message");
				AddEvaluatedExceptionValue(debugger, result, "$exception.StackTrace", "stackTrace");
				AddEvaluatedExceptionValue(debugger, result, "$exception.InnerException", "innerException");
			}

			result["available"] = hasCaptured;
			if (!hasCaptured)
				result["message"] = "No current or captured debugger exception is available.";

			return result;
		}

		private void AddEvaluatedExceptionValue(Debugger debugger, Dictionary<string, object> result, string expressionText, string key)
		{
			try
			{
				Expression expression = debugger.GetExpression(expressionText, false, 1000);
				if (expression != null && expression.IsValidValue)
				{
					string value = expression.Value;
					if (!string.IsNullOrEmpty(value) && !string.Equals(value, "null", StringComparison.OrdinalIgnoreCase))
						result[key] = Limit(value.Trim('"'), 4000);
				}
			}
			catch
			{
			}
		}

		private Dictionary<string, object> GetBreakpoint(Breakpoint breakpoint, Breakpoint lastHit)
		{
			string file = GetSafe(() => breakpoint.File);
			int line = GetSafe(() => breakpoint.FileLine);
			string name = GetSafe(() => breakpoint.Name);
			return new Dictionary<string, object>
			{
				{ "name", name },
				{ "file", file },
				{ "line", line },
				{ "condition", GetSafe(() => breakpoint.Condition) },
				{ "enabled", GetSafe(() => breakpoint.Enabled) },
				{ "lastHit", IsLastHitBreakpoint(file, line, name, lastHit) }
			};
		}

		private bool IsLastHitBreakpoint(string file, int line, string name, Breakpoint lastHit)
		{
			if (lastHit == null)
				return false;

			string lastFile = GetSafe(() => lastHit.File);
			int lastLine = GetSafe(() => lastHit.FileLine);
			string lastName = GetSafe(() => lastHit.Name);
			return string.Equals(file, lastFile, StringComparison.OrdinalIgnoreCase) && line == lastLine || !string.IsNullOrEmpty(name) && string.Equals(name, lastName, StringComparison.Ordinal);
		}

		private OutputWindowPane FindOutputPane(string paneName)
		{
			ThreadHelper.ThrowIfNotOnUIThread();
			OutputWindow outputWindow = GetSafe(() => dte?.ToolWindows.OutputWindow);
			if (outputWindow == null || outputWindow.OutputWindowPanes == null)
				return null;

			foreach (OutputWindowPane pane in outputWindow.OutputWindowPanes)
			{
				if (string.Equals(pane.Name, paneName, StringComparison.OrdinalIgnoreCase))
					return pane;
			}

			return null;
		}

		private EnvDTE.Thread FindThread(Debugger debugger, int threadId)
		{
			ThreadHelper.ThrowIfNotOnUIThread();
			try
			{
				foreach (EnvDTE.Thread thread in debugger.CurrentProgram.Threads)
				{
					if (thread.ID == threadId)
						return thread;
				}
			}
			catch
			{
			}

			return null;
		}

		private EnvDTE.StackFrame FindFrame(Debugger debugger, int frameIndex)
		{
			ThreadHelper.ThrowIfNotOnUIThread();
			if (frameIndex < 0)
				return null;

			try
			{
				int index = 0;
				foreach (EnvDTE.StackFrame frame in debugger.CurrentThread.StackFrames)
				{
					if (index == frameIndex)
						return frame;

					index++;
				}
			}
			catch
			{
			}

			return null;
		}

		private bool IsCurrentFrame(EnvDTE.Thread thread, EnvDTE.StackFrame frame, EnvDTE.StackFrame currentFrame, Debugger debugger)
		{
			if (thread == null || frame == null || currentFrame == null || debugger == null)
				return false;

			EnvDTE.Thread currentThread = GetSafe(() => debugger.CurrentThread);
			if (currentThread == null || GetSafe(() => currentThread.ID) != GetSafe(() => thread.ID))
				return false;

			Dictionary<string, object> left = GetStackFrame(frame, -1, false);
			Dictionary<string, object> right = GetStackFrame(currentFrame, -1, false);
			return string.Equals(left["function"] as string, right["function"] as string, StringComparison.Ordinal)
				&& string.Equals(left["file"] as string, right["file"] as string, StringComparison.OrdinalIgnoreCase)
				&& Equals(left["line"], right["line"]);
		}

		private string GetMode(Debugger debugger)
		{
			if (debugger == null)
				return "unavailable";

			try
			{
				switch (debugger.CurrentMode)
				{
					case dbgDebugMode.dbgBreakMode:
						return "break";
					case dbgDebugMode.dbgDesignMode:
						return "design";
					case dbgDebugMode.dbgRunMode:
						return "run";
					default:
						return debugger.CurrentMode.ToString();
				}
			}
			catch
			{
				return "unavailable";
			}
		}

		private string GetComString(object instance, string propertyName)
		{
			if (instance == null)
				return null;

			try
			{
				object value = instance.GetType().InvokeMember(propertyName, BindingFlags.GetProperty, null, instance, null);
				return value?.ToString();
			}
			catch
			{
				return null;
			}
		}

		private bool LooksMutatingExpression(string expression)
		{
			return Regex.IsMatch(expression, @"(\+\+|--|\+=|-=|\*=|/=|%=|&=|\|=|\^=|<<=|>>=|(?<![=!<>])=(?!=)|;)");
		}

		private int Clamp(int value, int min, int max)
		{
			if (value < min)
				return min;

			if (value > max)
				return max;

			return value;
		}

		private int GetInt(Dictionary<string, object> dictionary, string key, int defaultValue)
		{
			int? value = GetNullableInt(dictionary, key);
			return value ?? defaultValue;
		}

		private int? GetNullableInt(Dictionary<string, object> dictionary, string key)
		{
			if (dictionary == null || !dictionary.TryGetValue(key, out object value) || value == null)
				return null;

			try
			{
				return Convert.ToInt32(value);
			}
			catch
			{
				return null;
			}
		}

		private string GetString(Dictionary<string, object> dictionary, string key)
		{
			if (dictionary == null || !dictionary.TryGetValue(key, out object value) || value == null)
				return null;

			return value.ToString();
		}

		private Dictionary<string, object> GetDictionary(Dictionary<string, object> dictionary, string key)
		{
			if (dictionary == null || !dictionary.TryGetValue(key, out object value) || value == null)
				return null;

			return value as Dictionary<string, object>;
		}

		private T GetSafe<T>(Func<T> read)
		{
			try
			{
				return read();
			}
			catch
			{
				return default(T);
			}
		}

		private string Limit(string value, int maxLength)
		{
			if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
				return value;

			return value.Substring(0, maxLength);
		}

		private void WriteDiscoveryRecord()
		{
			ThreadHelper.ThrowIfNotOnUIThread();
			try
			{
				string directory = GetDiscoveryDirectory();
				Directory.CreateDirectory(directory);
				discoveryFilePath = Path.Combine(directory, devenvPid + ".json");

				Dictionary<string, object> record = new Dictionary<string, object>
				{
					{ "pid", devenvPid },
					{ "pipeName", pipeName },
					{ "token", token },
					{ "solutionPath", GetSafe(() => dte?.Solution?.FullName) },
					{ "version", typeof(ClaudeVSPackage).Assembly.GetName().Version.ToString() },
					{ "timestampUtc", DateTime.UtcNow.ToString("o") }
				};

				File.WriteAllText(discoveryFilePath, serializer.Serialize(record), new UTF8Encoding(false));
			}
			catch
			{
			}
		}

		private void DeleteDiscoveryRecord()
		{
			try
			{
				if (!string.IsNullOrEmpty(discoveryFilePath) && File.Exists(discoveryFilePath))
					File.Delete(discoveryFilePath);
			}
			catch
			{
			}
		}

		private string GetDiscoveryDirectory()
		{
			return McpSetup.GetDiscoveryDirectory();
		}

		private void CopyHelperToStablePath()
		{
			McpSetup.CopyHelperToStablePath();
		}

		private string CreateToken()
		{
			byte[] bytes = new byte[32];
			using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
				rng.GetBytes(bytes);

			return Convert.ToBase64String(bytes);
		}

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern int FormatMessage(int dwFlags, IntPtr lpSource, uint dwMessageId, int dwLanguageId, StringBuilder lpBuffer, int nSize, IntPtr Arguments);

		private const int FORMAT_MESSAGE_FROM_SYSTEM = 0x00001000;

		private string DecodeSEH(uint errorCode)
		{
			StringBuilder buffer = new StringBuilder(1024);
			int result = FormatMessage(FORMAT_MESSAGE_FROM_SYSTEM, IntPtr.Zero, errorCode, 0, buffer, buffer.Capacity, IntPtr.Zero);
			if (result > 0)
				return buffer.ToString().Trim();

			switch (errorCode)
			{
				case 0xC0000005:
					return "Access Violation (Memory Read/Write Error)";
				case 0xC00000FD:
					return "Stack Overflow";
				case 0xC0000094:
					return "Integer Division by Zero";
				case 0xC000001D:
					return "Illegal Instruction";
				case 0xC000008C:
					return "Array Bounds Exceeded";
				case 0xC0000409:
					return "Stack Buffer Overrun";
				case 0x80000003:
					return "Breakpoint";
				case 0xC0000008:
					return "Invalid Handle";
				case 0xC000013A:
					return "Control-C Exit";
				default:
					return "System Exception: 0x" + errorCode.ToString("X8");
			}
		}
	}
}
