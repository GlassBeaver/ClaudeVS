namespace ClaudeVS
{
    using System;
    using System.ComponentModel.Design;
    using System.IO;
    using EnvDTE;
    using EnvDTE80;
    using Microsoft.VisualStudio.Shell;
    using Microsoft.VisualStudio.Shell.Interop;
    using Task = System.Threading.Tasks.Task;

    internal sealed class SendCommentLineCommand
    {
        public const int CommandId = 0x0103;

        public static readonly Guid CommandSet = new Guid("a7c8e9d0-1234-5678-9abc-def012345678");

        private readonly AsyncPackage package;

        private SendCommentLineCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuItem = new MenuCommand(this.Execute, menuCommandID);
            commandService.AddCommand(menuItem);
        }

        public static SendCommentLineCommand Instance
        {
            get;
            private set;
        }

        private IAsyncServiceProvider ServiceProvider
        {
            get { return this.package; }
        }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            OleMenuCommandService commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            Instance = new SendCommentLineCommand(package, commandService);
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                DTE2 dte = Package.GetGlobalService(typeof(DTE)) as DTE2;
                if (dte == null || dte.ActiveDocument == null)
                {
                    return;
                }

                string filePath = dte.ActiveDocument.FullName;
                if (string.IsNullOrEmpty(filePath))
                {
                    return;
                }

                TextSelection selection = dte.ActiveDocument.Selection as TextSelection;
                if (selection == null)
                {
                    return;
                }

                int lineNumber = selection.CurrentLine;

                EditPoint editPoint = selection.ActivePoint.CreateEditPoint();
                editPoint.StartOfLine();
                string lineText = editPoint.GetText(editPoint.LineLength);

                if (!IsCommentLine(lineText))
                {
                    return;
                }

                string solutionDir = null;
                if (dte.Solution != null && !string.IsNullOrEmpty(dte.Solution.FullName))
                {
                    solutionDir = Path.GetDirectoryName(dte.Solution.FullName);
                }

                string relativePath = filePath;
                if (!string.IsNullOrEmpty(solutionDir) && filePath.StartsWith(solutionDir, StringComparison.OrdinalIgnoreCase))
                {
                    relativePath = filePath.Substring(solutionDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }

                string message = $"TASK: Insert code completion at @{relativePath}:{lineNumber}\n\nINSTRUCTIONS:\n- The comment on line {lineNumber} describes what code to insert AFTER that line\n- Generate ONLY the code to insert (no explanations, no markdown, no comments)\n- Preserve the existing indentation level\n- Do not modify or remove line {lineNumber}\n- Output format: Use the Edit tool to insert the new code after line {lineNumber}\n\nCOMMENT TEXT (this describes what to generate):\n{lineText}\n\nRemember: Output ONLY the Edit tool call, nothing else.";

                ToolWindowPane window = this.package.FindToolWindow(typeof(ClaudeTerminal), 0, true);
                if (window?.Frame is IVsWindowFrame windowFrame)
                {
                    //Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(windowFrame.Show());

                    if (window.Content is ClaudeTerminalControl control)
                        control.SendToClaude(message, true, false);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SendCommentLineCommand.Execute failed: {ex.Message}");
            }
        }

        private bool IsCommentLine(string lineText)
        {
            if (string.IsNullOrWhiteSpace(lineText))
            {
                return false;
            }

            string trimmed = lineText.TrimStart();

            return trimmed.StartsWith("//") ||
                   trimmed.StartsWith("/*") ||
                   trimmed.StartsWith("*") ||
                   trimmed.StartsWith("///");
        }
    }
}
