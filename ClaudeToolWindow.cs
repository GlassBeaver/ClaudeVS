using Microsoft.VisualStudio.Shell;
using System;
using System.Runtime.InteropServices;

namespace ClaudeVS
{
    [Guid("e3c7a8d9-2f4b-5c6e-8d9f-1a2b3c4d5e6f")]
    public class ClaudeToolWindow : ToolWindowPane
    {
        public ClaudeToolWindow() : base(null)
        {
            this.Caption = "Claude Code";
            this.Content = new ClaudeToolWindowControl();
        }
    }
}
