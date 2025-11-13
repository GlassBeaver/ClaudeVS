namespace ClaudeVS
{
    using System;
    using System.Runtime.InteropServices;
    using Microsoft.VisualStudio.Shell;
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
            if (pguidCmdGroup == VSConstants.GUID_VSStandardCommandSet97)
            {
                for (uint i = 0; i < cCmds; i++)
                {
                    if ((VSConstants.VSStd97CmdID)prgCmds[i].cmdID == VSConstants.VSStd97CmdID.PaneActivateDocWindow)
                    {
                        System.Diagnostics.Debug.WriteLine("PaneActivateDocWindow command blocked in QueryStatus");
                        return (int)Microsoft.VisualStudio.OLE.Interop.Constants.OLECMDERR_E_NOTSUPPORTED;
                    }
                }
            }

            IOleCommandTarget baseTarget = (IOleCommandTarget)base.GetService(typeof(IOleCommandTarget));
            if (baseTarget != null)
            {
                return baseTarget.QueryStatus(ref pguidCmdGroup, cCmds, prgCmds, pCmdText);
            }
            return (int)Microsoft.VisualStudio.OLE.Interop.Constants.OLECMDERR_E_UNKNOWNGROUP;
        }

        int IOleCommandTarget.Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
        {
            System.Diagnostics.Debug.WriteLine($"ClaudeTerminal.Exec: pguidCmdGroup={pguidCmdGroup}, nCmdID={nCmdID}");

            if (pguidCmdGroup == VSConstants.GUID_VSStandardCommandSet97)
            {
                System.Diagnostics.Debug.WriteLine($"VSStd97CmdID: {(VSConstants.VSStd97CmdID)nCmdID}");

                if ((VSConstants.VSStd97CmdID)nCmdID == VSConstants.VSStd97CmdID.PaneActivateDocWindow)
                {
                    System.Diagnostics.Debug.WriteLine("PaneActivateDocWindow (Escape) command intercepted in Exec");

                    if (conPtyTerminal != null && conPtyTerminal.IsRunning)
                    {
                        ConPtyTerminalConnection.NotifyEscapeHandled();
                        conPtyTerminal.WriteInput("\x1b");
                        System.Diagnostics.Debug.WriteLine("Escape key sent to terminal from Exec");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("Cannot send Escape - terminal not running");
                    }

                    return VSConstants.S_OK;
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
