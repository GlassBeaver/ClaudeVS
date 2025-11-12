namespace ClaudeVS
{
    using System;
    using System.IO;
    using System.Windows.Controls;
    using System.Windows.Input;
    using EnvDTE;
    using EnvDTE80;
    using Microsoft.Terminal.Wpf;
    using Microsoft.VisualStudio.Shell;

    /// <summary>
    /// Interaction logic for ClaudeTerminalControl.xaml
    /// </summary>
    public partial class ClaudeTerminalControl : UserControl
    {
        private ConPtyTerminal conPtyTerminal;
        private ConPtyTerminalConnection terminalConnection;
        private ToolWindowPane toolWindowPane;

        /// <summary>
        /// Initializes a new instance of the <see cref="ClaudeTerminalControl"/> class.
        /// </summary>
        public ClaudeTerminalControl(ToolWindowPane toolWindowPane = null)
        {
            this.toolWindowPane = toolWindowPane;
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

                // Get the active project directory, or fallback to user's home folder
                string workingDir = GetActiveProjectDirectory();
                if (string.IsNullOrEmpty(workingDir))
                {
                    workingDir = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
                    System.Diagnostics.Debug.WriteLine($"No active project found, using default working directory: {workingDir}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Using active project directory: {workingDir}");
                }

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

        private string GetActiveProjectDirectory()
        {
            try
            {
                // Get DTE (Visual Studio automation model) from tool window's service provider
                DTE2 dte = null;
                if (toolWindowPane != null)
                {
                    dte = toolWindowPane.GetService<EnvDTE.DTE, EnvDTE.DTE>() as DTE2;
                }

                // Fallback to Marshal.GetActiveObject if service provider fails
                if (dte == null)
                {
                    System.Diagnostics.Debug.WriteLine("GetActiveProjectDirectory: Service provider DTE is null, trying Marshal.GetActiveObject");
                    dte = (DTE2)System.Runtime.InteropServices.Marshal.GetActiveObject("VisualStudio.DTE.18.0");
                }

                if (dte == null || dte.Solution == null)
                {
                    System.Diagnostics.Debug.WriteLine("GetActiveProjectDirectory: DTE or Solution is null");
                    return null;
                }

                // Try to get the project from the active document
                System.Diagnostics.Debug.WriteLine($"GetActiveProjectDirectory: ActiveDocument = {dte.ActiveDocument?.Name ?? "null"}");
                if (dte.ActiveDocument != null && dte.ActiveDocument.ProjectItem != null)
                {
                    Project project = dte.ActiveDocument.ProjectItem.ContainingProject;
                    if (project != null && !string.IsNullOrEmpty(project.FileName))
                    {
                        string dir = Path.GetDirectoryName(project.FileName);
                        System.Diagnostics.Debug.WriteLine($"GetActiveProjectDirectory: Using active document project: {dir}");
                        return dir;
                    }
                }

                System.Diagnostics.Debug.WriteLine("GetActiveProjectDirectory: No active document with project, falling back to startup project");

                // Fall back to startup project if no active document
                var startupProjects = (Array)dte.Solution.SolutionBuild.StartupProjects;
                if (startupProjects != null && startupProjects.Length > 0)
                {
                    string projectName = startupProjects.GetValue(0).ToString();
                    System.Diagnostics.Debug.WriteLine($"GetActiveProjectDirectory: Startup project name = {projectName}");
                    Project project = dte.Solution.Projects.Item(projectName);
                    if (project != null && !string.IsNullOrEmpty(project.FileName))
                    {
                        string dir = Path.GetDirectoryName(project.FileName);
                        System.Diagnostics.Debug.WriteLine($"GetActiveProjectDirectory: Using startup project: {dir}");
                        return dir;
                    }
                }

                System.Diagnostics.Debug.WriteLine("GetActiveProjectDirectory: No startup project, falling back to first project");

                // Final fallback to first project
                if (dte.Solution.Projects.Count > 0)
                {
                    var project = dte.Solution.Projects.Item(1);
                    if (project != null && !string.IsNullOrEmpty(project.FileName))
                    {
                        string dir = Path.GetDirectoryName(project.FileName);
                        System.Diagnostics.Debug.WriteLine($"GetActiveProjectDirectory: Using first project: {dir}");
                        return dir;
                    }
                }
            }
            catch (Exception ex)
            {
                // If we can't get DTE, return null and use default
                System.Diagnostics.Debug.WriteLine($"GetActiveProjectDirectory: Exception: {ex}");
            }

            System.Diagnostics.Debug.WriteLine("GetActiveProjectDirectory: Returning null (no project found)");
            return null;
        }
    }
}
