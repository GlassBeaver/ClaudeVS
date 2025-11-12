namespace ClaudeVS
{
    using System;
    using System.Windows.Controls;
    using System.Windows.Input;
    using Microsoft.Terminal.Wpf;

    /// <summary>
    /// Interaction logic for ClaudeTerminalControl.xaml
    /// </summary>
    public partial class ClaudeTerminalControl : UserControl
    {
        private ConPtyTerminal conPtyTerminal;
        private ConPtyTerminalConnection terminalConnection;

        /// <summary>
        /// Initializes a new instance of the <see cref="ClaudeTerminalControl"/> class.
        /// </summary>
        public ClaudeTerminalControl()
        {
            this.InitializeComponent();
            this.Loaded += ClaudeTerminalControl_Loaded;
            this.Unloaded += ClaudeTerminalControl_Unloaded;
            System.Diagnostics.Debug.WriteLine("ClaudeTerminalControl constructed");
        }

        private void ClaudeTerminalControl_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            try
            {
                System.IO.File.AppendAllText(@"c:\temp\conpty-debug.txt", "ClaudeTerminalControl_Loaded fired!\n");
                System.Diagnostics.Debug.WriteLine("ClaudeTerminalControl_Loaded starting");
                InitializeConPtyTerminal();

                // Set focus to the terminal control so it can capture keyboard input
                System.Diagnostics.Debug.WriteLine("Setting focus to TerminalControl");
                TerminalControl.Focus();
                System.Diagnostics.Debug.WriteLine($"Focus set to TerminalControl");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing terminal: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private void InitializeConPtyTerminal()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("InitializeConPtyTerminal starting");

                // Create and initialize ConPTY terminal
                System.Diagnostics.Debug.WriteLine("Creating new ConPtyTerminal instance");
                conPtyTerminal = new ConPtyTerminal(rows: 30, columns: 120);
                System.Diagnostics.Debug.WriteLine("ConPtyTerminal instance created successfully");

                string workingDir = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
                System.Diagnostics.Debug.WriteLine($"Initializing ConPTY with working directory: {workingDir}");

                bool initialized = conPtyTerminal.Initialize(workingDir);
                System.Diagnostics.Debug.WriteLine($"ConPTY Initialize returned: {initialized}");

                if (!initialized)
                {
                    System.Diagnostics.Debug.WriteLine("FAILED: ConPTY terminal initialization returned false");
                    conPtyTerminal?.Dispose();
                    conPtyTerminal = null;
                    return;
                }

                System.Diagnostics.Debug.WriteLine("SUCCESS: ConPTY terminal initialized successfully");

                // Create the connection bridge between ConPTY and TerminalControl
                System.Diagnostics.Debug.WriteLine("Creating ConPtyTerminalConnection");
                terminalConnection = new ConPtyTerminalConnection(conPtyTerminal);

                // Connect the terminal control to the ConPTY backend
                System.Diagnostics.Debug.WriteLine("Setting TerminalControl.Connection");
                TerminalControl.Connection = terminalConnection;
                System.Diagnostics.Debug.WriteLine("TerminalControl.Connection set successfully");

                // Start the connection (this may be required for the terminal to become active)
                System.Diagnostics.Debug.WriteLine("Starting the terminal connection");
                terminalConnection.Start();
                System.Diagnostics.Debug.WriteLine("Terminal connection started");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EXCEPTION in InitializeConPtyTerminal: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                conPtyTerminal?.Dispose();
                conPtyTerminal = null;
            }
        }


        private void ClaudeTerminalControl_Unloaded(object sender, System.Windows.RoutedEventArgs e)
        {
            try
            {
                conPtyTerminal?.Dispose();
                conPtyTerminal = null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error disposing ConPTY terminal: {ex.Message}");
            }
        }
    }
}
