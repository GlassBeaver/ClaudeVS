using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ClaudeVS
{
    public partial class ClaudeToolWindowControl : UserControl
    {
        private ObservableCollection<ChatMessage> messages;
        private ClaudeCliManager cliManager;

        public ClaudeToolWindowControl()
        {
            InitializeComponent();

            messages = new ObservableCollection<ChatMessage>();
            ChatMessages.ItemsSource = messages;

            cliManager = new ClaudeCliManager();
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
            // Send on Ctrl+Enter
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            {
                SendMessage();
                e.Handled = true;
            }
        }

        private async void SendMessage()
        {
            string message = InputTextBox.Text.Trim();
            if (string.IsNullOrEmpty(message))
                return;

            // Add user message to chat
            AddMessage(true, message);
            InputTextBox.Clear();

            // Show loading indicator
            LoadingIndicator.Visibility = Visibility.Visible;
            SendButton.IsEnabled = false;

            try
            {
                await cliManager.SendMessageAsync(message);
            }
            catch (Exception ex)
            {
                AddMessage(false, $"Error: {ex.Message}");
                LoadingIndicator.Visibility = Visibility.Collapsed;
                SendButton.IsEnabled = true;
            }
        }

        private void OnResponseReceived(object sender, string response)
        {
            Dispatcher.Invoke(() =>
            {
                AddMessage(false, response);
                LoadingIndicator.Visibility = Visibility.Collapsed;
                SendButton.IsEnabled = true;
            });
        }

        private void OnErrorOccurred(object sender, string error)
        {
            Dispatcher.Invoke(() =>
            {
                AddMessage(false, $"Error: {error}");
                LoadingIndicator.Visibility = Visibility.Collapsed;
                SendButton.IsEnabled = true;
            });
        }

        private void AddMessage(bool isUser, string message)
        {
            messages.Add(new ChatMessage { IsUser = isUser, Message = message });

            // Auto-scroll to bottom
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
