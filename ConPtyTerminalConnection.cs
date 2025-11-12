namespace ClaudeVS
{
    using System;
    using System.Text;
    using Microsoft.Terminal.Wpf;

    /// <summary>
    /// Custom TerminalConnection that bridges ConPtyTerminal with Microsoft.Terminal.Wpf.TerminalControl
    /// </summary>
    public class ConPtyTerminalConnection : ITerminalConnection
    {
        private readonly ConPtyTerminal conPtyTerminal;

        public ConPtyTerminalConnection(ConPtyTerminal terminal)
        {
            conPtyTerminal = terminal ?? throw new ArgumentNullException(nameof(terminal));

            System.Diagnostics.Debug.WriteLine("ConPtyTerminalConnection constructor: wiring up event handlers");

            // Wire up ConPTY output to terminal control input
            conPtyTerminal.OutputReceived += (sender, output) =>
            {
                System.Diagnostics.Debug.WriteLine($"ConPtyTerminalConnection: OutputReceived event fired, data length: {output?.Length ?? 0}");
                // Forward ConPTY output to the terminal control
                if (TerminalOutput != null)
                {
                    TerminalOutput.Invoke(this, new TerminalOutputEventArgs(output));
                    System.Diagnostics.Debug.WriteLine("TerminalOutput event invoked successfully");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("WARNING: TerminalOutput event is null, no subscribers");
                }
            };

            conPtyTerminal.ProcessExited += (sender, exitCode) =>
            {
                System.Diagnostics.Debug.WriteLine($"ConPtyTerminalConnection: ProcessExited event fired, exit code: {exitCode}");
                // Notify when process exits
                Closed?.Invoke(this, EventArgs.Empty);
            };

            System.Diagnostics.Debug.WriteLine("ConPtyTerminalConnection constructor: event handlers wired up successfully");
        }

        public event EventHandler<TerminalOutputEventArgs> TerminalOutput;
        public event EventHandler Closed;

        public void WriteInput(string data)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"ConPtyTerminalConnection.WriteInput called with: '{data}' (length: {data?.Length ?? 0})");

                // Forward user input from the terminal control to ConPTY
                if (conPtyTerminal != null && conPtyTerminal.IsRunning)
                {
                    conPtyTerminal.WriteInput(data);
                    System.Diagnostics.Debug.WriteLine("Input successfully written to ConPTY");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Cannot write input: conPtyTerminal is null={conPtyTerminal == null}, IsRunning={conPtyTerminal?.IsRunning}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error writing input to ConPTY: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        public void Resize(uint rows, uint columns)
        {
            // TODO: Implement resize support if needed
            System.Diagnostics.Debug.WriteLine($"Terminal resize requested: {columns}x{rows}");
        }

        public void Close()
        {
            try
            {
                conPtyTerminal?.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error closing ConPTY: {ex.Message}");
            }
        }

        public void Start()
        {
            // Already started in ConPtyTerminal.Initialize()
            System.Diagnostics.Debug.WriteLine("ConPtyTerminalConnection.Start() called (already initialized)");
        }
    }
}
