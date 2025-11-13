namespace ClaudeVS
{
    using System;
    using System.Runtime.InteropServices;
    using Microsoft.VisualStudio.Shell;

    /// <summary>
    /// This class implements the tool window exposed by this package and hosts a user control.
    ///
    /// In Visual Studio tool windows are composed of a frame (implemented by the shell) and a pane,
    /// usually implemented by the package implementer.
    ///
    /// This class derives from the ToolWindowPane class provided from the MPF in order to use its
    /// implementation of the IVsUIElementPane interface.
    /// </summary>
    [Guid("f4c7b9e2-3a5d-6c8f-1b2e-4a9d7c5f3e8b")]
    public class ClaudeTerminal : ToolWindowPane
    {
        private ConPtyTerminal conPtyTerminal;
        private ConPtyTerminalConnection terminalConnection;

        public ConPtyTerminal Terminal => conPtyTerminal;
        public ConPtyTerminalConnection TerminalConnection => terminalConnection;

        /// <summary>
        /// Initializes a new instance of the <see cref="ClaudeTerminal"/> class.
        /// </summary>
        public ClaudeTerminal() : base(null)
        {
            this.Caption = "ClaudeVS";

            // This is the user control hosted by the tool window; Note that, even if this class implements IDisposable,
            // we are not calling Dispose on this object as this is lifetime managed by the
            // shell so the copy instance will be reused.
            this.Content = new ClaudeTerminalControl(this);
        }

        public void SetTerminalInstances(ConPtyTerminal terminal, ConPtyTerminalConnection connection)
        {
            conPtyTerminal = terminal;
            terminalConnection = connection;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                conPtyTerminal?.Dispose();
                terminalConnection = null;
                conPtyTerminal = null;
            }
            base.Dispose(disposing);
        }
    }
}
