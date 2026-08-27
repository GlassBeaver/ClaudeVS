namespace ClaudeVS
{
    using System;
    using System.ComponentModel.Design;
    using System.Diagnostics;
    using System.Linq;
    using System.Windows;
    using EnvDTE;
    using EnvDTE80;
    using Microsoft.VisualStudio.Shell;
    using Task = System.Threading.Tasks.Task;

    /// <summary>
    /// Command handler for Agent Actions.
    /// </summary>
    internal sealed class AgentActionCommand
    {
        public const int AgentAction1Id = 0x0105;
        public const int AgentAction2Id = 0x0106;
        public const int AgentAction3Id = 0x0107;
        public const int AgentAction4Id = 0x0108;
        public const int AgentAction5Id = 0x0109;
        public const int AgentAction6Id = 0x010A;
        public const int AgentAction7Id = 0x010C;
        public const int QuickSwitch1Id = 0x010D;
        public const int QuickSwitch2Id = 0x010E;
        public const int QuickSwitch3Id = 0x010F;
        public const int AgentAction8Id = 0x0111;
        public const int QuickSwitch4Id = 0x0110;
        public const int PreviousAgentTabId = 0x0112;
        public const int NextAgentTabId = 0x0113;

        /// <summary>
        /// Command menu group (command set GUID).
        /// </summary>
        public static readonly Guid CommandSet = new Guid("a7c8e9d0-1234-5678-9abc-def012345678");

        /// <summary>
        /// VS Package that provides this command, not null.
        /// </summary>
        private readonly AsyncPackage package;

        /// <summary>
        /// Initializes a new instance of the <see cref="AgentActionCommand"/> class.
        /// Adds our command handlers for the agent actions.
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        /// <param name="commandService">Command service to add command to, not null.</param>
        private AgentActionCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            if (commandService == null)
            {
                throw new ArgumentNullException(nameof(commandService));
            }

            AddCommand(commandService, AgentAction1Id);
            AddCommand(commandService, AgentAction2Id);
            AddCommand(commandService, AgentAction3Id);
            AddCommand(commandService, AgentAction4Id);
            AddCommand(commandService, AgentAction5Id);
            AddCommand(commandService, AgentAction6Id);
            AddCommand(commandService, AgentAction7Id);
            AddCommand(commandService, AgentAction8Id);
            AddCommand(commandService, QuickSwitch1Id);
            AddCommand(commandService, QuickSwitch2Id);
            AddCommand(commandService, QuickSwitch3Id);
            AddCommand(commandService, QuickSwitch4Id);
            AddCommand(commandService, PreviousAgentTabId);
            AddCommand(commandService, NextAgentTabId);
        }

        private void AddCommand(OleMenuCommandService commandService, int commandId)
        {
            var menuCommandID = new CommandID(CommandSet, commandId);
            var menuItem = new MenuCommand(this.Execute, menuCommandID);
            commandService.AddCommand(menuItem);
        }

        /// <summary>
        /// Gets the instance of the command.
        /// </summary>
        public static AgentActionCommand Instance
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the service provider from the owner package.
        /// </summary>
        private IAsyncServiceProvider ServiceProvider
        {
            get { return this.package; }
        }

        /// <summary>
        /// Initializes the singleton instance of the command.
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        /// <returns>A task representing the async work of command initialization, or an already completed task if there is none. Do not return null from this method.</returns>
        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            OleMenuCommandService commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            Instance = new AgentActionCommand(package, commandService);
        }

        /// <summary>
        /// This function is the callback used to execute the command when the menu item is clicked.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event args.</param>
        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            
            var menuCommand = sender as MenuCommand;
            if (menuCommand == null) return;

            string inputToSend = null;

            if (menuCommand.CommandID.ID == PreviousAgentTabId || menuCommand.CommandID.ID == NextAgentTabId)
            {
                ToolWindowPane tabWindow = this.package.FindToolWindow(typeof(ClaudeTerminal), 0, false);
                var tabTerminalWindow = tabWindow as ClaudeTerminal;
                var tabControl = tabTerminalWindow?.Content as ClaudeTerminalControl;
                tabControl?.SwitchAgentTab(menuCommand.CommandID.ID == NextAgentTabId ? 1 : -1);
                return;
            }

            if (menuCommand.CommandID.ID >= QuickSwitch1Id && menuCommand.CommandID.ID <= QuickSwitch4Id)
            {
                int presetIndex = menuCommand.CommandID.ID - QuickSwitch1Id;
                ToolWindowPane qsWindow = this.package.FindToolWindow(typeof(ClaudeTerminal), 0, false);
                var qsTerminalWindow = qsWindow as ClaudeTerminal;
                var control = qsTerminalWindow?.Content as ClaudeTerminalControl;
                control?.ExecuteQuickSwitch(presetIndex);
                return;
            }

            if (menuCommand.CommandID.ID == AgentAction6Id)
            {
                // Handle paste with bracketed paste mode
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        string text = Clipboard.GetText();
                        inputToSend = "\x1b[200~" + text + "\x1b[201~";
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"AgentActionCommand: Exception accessing clipboard: {ex}");
                }
            }
            else
            {
                inputToSend = GetInputForCommand(menuCommand.CommandID.ID);
            }

            if (string.IsNullOrEmpty(inputToSend))
            {
                Debug.WriteLine($"AgentActionCommand: No valid input mapped for command {menuCommand.CommandID.ID}");
                return;
            }

            ToolWindowPane window = this.package.FindToolWindow(typeof(ClaudeTerminal), 0, false);
            if (window == null)
            {
                Debug.WriteLine($"AgentActionCommand triggered but terminal not found");
                return;
            }

            var terminalWindow = window as ClaudeTerminal;
            if (terminalWindow?.Terminal != null && terminalWindow.Terminal.IsRunning)
            {
                terminalWindow.Terminal.WriteInput(inputToSend);
                Debug.WriteLine($"AgentActionCommand sent input to terminal");
            }
            else
            {
                 Debug.WriteLine($"AgentActionCommand triggered but terminal not running");
            }
        }

        private string GetInputForCommand(int commandId)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            
            try
            {
                DTE2 dte = this.package.GetService<DTE, DTE2>();
                if (dte == null) return null;

                string suffix;
                switch (commandId)
                {
                    case AgentAction1Id: suffix = "AgentAction1"; break;
                    case AgentAction2Id: suffix = "AgentAction2"; break;
                case AgentAction3Id: suffix = "AgentAction3"; break;
                case AgentAction4Id: suffix = "AgentAction4"; break;
                case AgentAction5Id: suffix = "AgentAction5"; break;
                case AgentAction6Id: suffix = "AgentAction6"; break;
                case AgentAction7Id: suffix = "AgentAction7"; break;
                case AgentAction8Id: suffix = "AgentAction8"; break;
                default:
                    Debug.WriteLine($"AgentActionCommand: Unknown command ID {commandId}");
                    return null;
            }

                // VS commands are typically named "PackageName.CommandName" or just "CommandName" depending on registration.
                // The LocCanonicalName is ".ClaudeVS.AgentAction1" (dot at start?)
                // Usually "ClaudeVS.AgentAction1" or "ClaudeVS.ClaudeVS.AgentAction1" if package name is ClaudeVS.
                // VSCT strings say: <LocCanonicalName>.ClaudeVS.AgentAction1</LocCanonicalName>
                // So DTE.Commands.Item("ClaudeVS.AgentAction1") should work.
                string commandName = $"ClaudeVS.{suffix}";
                Command cmd = null;
                try
                {
                    cmd = dte.Commands.Item(commandName);
                }
                catch
                {
                    Debug.WriteLine($"Could not find command {commandName}");
                }

                if (cmd == null) return null;

                object[] bindings = cmd.Bindings as object[];
                if (bindings == null || bindings.Length == 0) return null;

                // Grab the first binding (e.g., "Claude Terminal::Ctrl+T" or "Global::Ctrl+T")
                string binding = bindings[0] as string;
                if (string.IsNullOrEmpty(binding)) return null;

                // Extract keys: "Scope::Keys" -> "Keys"
                int sepIndex = binding.IndexOf("::");
                string keyStr = (sepIndex >= 0) ? binding.Substring(sepIndex + 2) : binding;

                return ConvertKeyBindingToInput(keyStr);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Exception in GetInputForCommand: {ex}");
                return null;
            }
        }

        private string ConvertKeyBindingToInput(string keyStr)
        {
            // Expected format: "Ctrl+T", "Ctrl+Shift+R", "Alt+F4", etc.
            
            if (string.IsNullOrEmpty(keyStr)) return null;

            bool ctrl = keyStr.Contains("Ctrl");
            bool shift = keyStr.Contains("Shift");
            bool alt = keyStr.Contains("Alt");

            string[] parts = keyStr.Split('+');
            string key = parts.Last().Trim();

            if (key.Length == 1 && char.IsLetter(key[0]))
            {
                if (ctrl && !alt)
                {
                    // Ctrl+Letter (Shift doesn't change control code usually for A-Z)
                    char c = char.ToUpper(key[0]);
                    return ((char)(c - 'A' + 1)).ToString();
                }
                else if (alt && !ctrl)
                {
                    // Alt+Letter -> Escape + Letter
                    char c = key[0];
                    if (!shift)
                    {
                        c = char.ToLower(c);
                    }
                    return $"\x1b{c}";
                }
            }

            return null;
        }
    }
}
