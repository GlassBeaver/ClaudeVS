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

    internal sealed class SendFileLocationCommand
    {
        public const int CommandId = 0x0102;

        public static readonly Guid CommandSet = new Guid("a7c8e9d0-1234-5678-9abc-def012345678");

        private readonly AsyncPackage package;

        private SendFileLocationCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuItem = new MenuCommand(this.Execute, menuCommandID);
            commandService.AddCommand(menuItem);
        }

        public static SendFileLocationCommand Instance
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
            Instance = new SendFileLocationCommand(package, commandService);
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
                int lineNumber = selection?.CurrentLine ?? 1;
                string selectedText = selection?.Text;

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

                string message = $"@{relativePath} line {lineNumber}";
                if (!string.IsNullOrEmpty(selectedText))
                {
                    message += $"\n{selectedText}";
                }
                message += "\n\n";

                ToolWindowPane window = this.package.FindToolWindow(typeof(ClaudeTerminal), 0, false);
                if (window != null && window.Content is ClaudeTerminalControl control)
                {
                    control.SendToClaude(message, false);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SendFileLocationCommand.Execute failed: {ex.Message}");
            }
        }
    }
}
