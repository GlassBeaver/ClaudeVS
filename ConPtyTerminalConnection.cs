namespace ClaudeVS
{
    using System;
    using System.Text;
    using System.Threading;
    using Microsoft.Terminal.Wpf;

    /// <summary>
    /// Custom TerminalConnection that bridges ConPtyTerminal with Microsoft.Terminal.Wpf.TerminalControl
    /// </summary>
    public class ConPtyTerminalConnection : ITerminalConnection
    {
        private readonly ConPtyTerminal conPtyTerminal;
        private readonly ManualResetEventSlim connectionReadyEvent = new ManualResetEventSlim(false);

        public ConPtyTerminalConnection(ConPtyTerminal terminal)
        {
            conPtyTerminal = terminal ?? throw new ArgumentNullException(nameof(terminal));

            System.Diagnostics.Debug.WriteLine("ConPtyTerminalConnection constructor: wiring up event handlers");

            conPtyTerminal.OutputReceived += (sender, output) =>
            {
                System.Diagnostics.Debug.WriteLine($"ConPtyTerminalConnection: OutputReceived event fired, data length: {output?.Length ?? 0}");
                if (terminalOutputEvent != null)
                {
                    terminalOutputEvent.Invoke(this, new TerminalOutputEventArgs(output));
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
                Closed?.Invoke(this, EventArgs.Empty);
            };

            System.Diagnostics.Debug.WriteLine("ConPtyTerminalConnection constructor: event handlers wired up successfully");
        }

        public event EventHandler<TerminalOutputEventArgs> TerminalOutput
        {
            add
            {
                terminalOutputEvent += value;
                System.Diagnostics.Debug.WriteLine("ConPtyTerminalConnection: TerminalOutput subscriber added, signaling ready");
                connectionReadyEvent.Set();
            }
            remove
            {
                terminalOutputEvent -= value;
            }
        }

        private event EventHandler<TerminalOutputEventArgs> terminalOutputEvent;

        public event EventHandler Closed;

        public void WaitForConnectionReady()
        {
            System.Diagnostics.Debug.WriteLine("ConPtyTerminalConnection: Waiting for TerminalOutput subscriber...");
            bool ready = connectionReadyEvent.Wait(TimeSpan.FromSeconds(5));
            if (ready)
            {
                System.Diagnostics.Debug.WriteLine("ConPtyTerminalConnection: TerminalOutput subscriber registered, connection ready");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("ConPtyTerminalConnection: WARNING - Timeout waiting for TerminalOutput subscriber!");
            }
        }

        public void WriteInput(string data)
        {
            try
            {
                if (conPtyTerminal != null && conPtyTerminal.IsRunning)
                    conPtyTerminal.WriteInput(data);
                else
                    System.Diagnostics.Debug.WriteLine($"Cannot write input: conPtyTerminal is null={conPtyTerminal == null}, IsRunning={conPtyTerminal?.IsRunning}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error writing input to ConPTY: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        public void Resize(uint rows, uint columns)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"ConPtyTerminalConnection.Resize: Terminal resize requested: {columns}x{rows}");

                if (conPtyTerminal != null && conPtyTerminal.IsRunning)
                {
                    conPtyTerminal.Resize((ushort)rows, (ushort)columns);
                    System.Diagnostics.Debug.WriteLine($"ConPtyTerminalConnection.Resize: Terminal resized successfully");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"ConPtyTerminalConnection.Resize: Cannot resize - terminal not running or null");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ConPtyTerminalConnection.Resize failed: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
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
            System.Diagnostics.Debug.WriteLine("ConPtyTerminalConnection.Start() called (already initialized)");
        }
    }
}
