using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace ClaudeVS
{
    public partial class ClaudeToolWindowControl : UserControl
    {
        private ObservableCollection<ChatMessage> messages;
        private ClaudeCliManager cliManager;
        private ChatMessage currentResponseMessage;
        private ToolWindowPane toolWindowPane;

        public ClaudeToolWindowControl(ToolWindowPane toolWindowPane = null)
        {
            System.Diagnostics.Debug.WriteLine("DSADSADSA");
            this.toolWindowPane = toolWindowPane;
            InitializeComponent();

            messages = new ObservableCollection<ChatMessage>();
            ChatMessages.ItemsSource = messages;

            cliManager = new ClaudeCliManager();
            cliManager.ChunkReceived += OnChunkReceived;
            cliManager.ResponseReceived += OnResponseReceived;
            cliManager.ErrorOccurred += OnErrorOccurred;

            AddMessage(false, "Welcome to Claude Code! Type your message below.");
        }

        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            SendMessage();
        }

        private void InputTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            {
                SendMessage();
                e.Handled = true;
            }
        }

        private async void SendMessage()
        {
            System.Diagnostics.Debug.WriteLine("SendMessage: CALLED");
            string message = InputTextBox.Text.Trim();
            if (string.IsNullOrEmpty(message))
                return;

            AddMessage(true, message);
            InputTextBox.Clear();

            currentResponseMessage = new ChatMessage { IsUser = false, Message = "" };
            messages.Add(currentResponseMessage);

            LoadingIndicator.Visibility = Visibility.Visible;
            SendButton.IsEnabled = false;

            try
            {
                System.Diagnostics.Debug.WriteLine("SendMessage: About to call GetActiveProjectDirectory");
                string workingDir = GetActiveProjectDirectory();
                System.Diagnostics.Debug.WriteLine($"SendMessage: Working directory result = {workingDir ?? "null"}");
                if (!string.IsNullOrEmpty(workingDir))
                {
                    System.Diagnostics.Debug.WriteLine($"SendMessage: Setting working directory to: {workingDir}");
                    cliManager.SetWorkingDirectory(workingDir);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("SendMessage: No working directory set, using default");
                }

                System.Diagnostics.Debug.WriteLine($"SendMessage: About to call SendMessageAsync with message: {message}");
                await cliManager.SendMessageAsync(message);
            }
            catch (Exception ex)
            {
                if (currentResponseMessage != null)
                {
                    currentResponseMessage.Message = $"Error: {ex.Message}";
                }
                else
                {
                    AddMessage(false, $"Error: {ex.Message}");
                }
                LoadingIndicator.Visibility = Visibility.Collapsed;
                SendButton.IsEnabled = true;
            }
        }

        private string GetActiveProjectDirectory()
        {
            try
            {
                DTE2 dte = null;
                if (toolWindowPane != null)
                {
                    dte = toolWindowPane.GetService<EnvDTE.DTE, EnvDTE.DTE>() as DTE2;
                }

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
                System.Diagnostics.Debug.WriteLine($"GetActiveProjectDirectory: Exception: {ex}");
            }

            System.Diagnostics.Debug.WriteLine("GetActiveProjectDirectory: Returning null (no project found)");
            return null;
        }

        private void OnChunkReceived(object sender, string chunk)
        {
            Dispatcher.Invoke(() =>
            {
                if (currentResponseMessage != null)
                {
                    currentResponseMessage.Message += chunk;

                    if (ChatMessages.Items.Count > 0)
                    {
                        var border = ChatMessages.ItemContainerGenerator.ContainerFromIndex(ChatMessages.Items.Count - 1) as FrameworkElement;
                        border?.BringIntoView();
                    }
                }
            });
        }

        private void OnResponseReceived(object sender, string response)
        {
            Dispatcher.Invoke(() =>
            {
                LoadingIndicator.Visibility = Visibility.Collapsed;
                SendButton.IsEnabled = true;
                currentResponseMessage = null;
            });
        }

        private void OnErrorOccurred(object sender, string error)
        {
            Dispatcher.Invoke(() =>
            {
                if (currentResponseMessage != null)
                {
                    currentResponseMessage.Message = $"Error: {error}";
                }
                else
                {
                    AddMessage(false, $"Error: {error}");
                }
                LoadingIndicator.Visibility = Visibility.Collapsed;
                SendButton.IsEnabled = true;
            });
        }

        private void AddMessage(bool isUser, string message)
        {
            messages.Add(new ChatMessage { IsUser = isUser, Message = message });

            Dispatcher.InvokeAsync(() =>
            {
                if (ChatMessages.Items.Count > 0)
                {
                    var border = ChatMessages.ItemContainerGenerator.ContainerFromIndex(ChatMessages.Items.Count - 1) as FrameworkElement;
                    border?.BringIntoView();
                }
            }, System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    public class ChatMessage : INotifyPropertyChanged
    {
        private bool isUser;
        private string message;

        public bool IsUser
        {
            get => isUser;
            set
            {
                isUser = value;
                OnPropertyChanged(nameof(IsUser));
            }
        }

        public string Message
        {
            get => message;
            set
            {
                message = value;
                OnPropertyChanged(nameof(Message));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
