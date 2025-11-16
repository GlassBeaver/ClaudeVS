namespace ClaudeVS
{
    using System;
    using System.Runtime.InteropServices;
    using System.Threading.Tasks;
    using Microsoft.VisualStudio.Shell;
    using Microsoft.VisualStudio.Shell.Interop;
    using Microsoft.VisualStudio.OLE.Interop;
    using Microsoft.VisualStudio;

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
    public class ClaudeTerminal : ToolWindowPane, IOleCommandTarget
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

            if (conPtyTerminal != null)
            {
                System.Diagnostics.Debug.WriteLine("ClaudeTerminal constructor: Window being recreated, disposing old terminal");
                conPtyTerminal?.Dispose();
                conPtyTerminal = null;
                terminalConnection = null;
            }

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

        int IOleCommandTarget.QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText)
        {
            IOleCommandTarget baseTarget = (IOleCommandTarget)base.GetService(typeof(IOleCommandTarget));
            if (baseTarget != null)
            {
                return baseTarget.QueryStatus(ref pguidCmdGroup, cCmds, prgCmds, pCmdText);
            }
            return (int)Microsoft.VisualStudio.OLE.Interop.Constants.OLECMDERR_E_UNKNOWNGROUP;
        }

        int IOleCommandTarget.Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
        {
            if (pguidCmdGroup == Microsoft.VisualStudio.VSConstants.GUID_VSStandardCommandSet97)
            {
                if ((VSConstants.VSStd97CmdID)nCmdID == VSConstants.VSStd97CmdID.PaneActivateDocWindow)
                {
                    System.Diagnostics.Debug.WriteLine("Escape key pressed, forwarding to Claude");
                    if (conPtyTerminal != null && conPtyTerminal.IsRunning)
                    {
						System.Diagnostics.Debug.WriteLine("Writing Esc...");
						conPtyTerminal.WriteInput("\x1b");
                    }
					return (int)Microsoft.VisualStudio.VSConstants.S_OK;
                }
            }

            IOleCommandTarget baseTarget = (IOleCommandTarget)base.GetService(typeof(IOleCommandTarget));
            if (baseTarget != null)
            {
                return baseTarget.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
            }
            return (int)Microsoft.VisualStudio.OLE.Interop.Constants.OLECMDERR_E_NOTSUPPORTED;
        }
    }
}
