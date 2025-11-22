using System.Windows;

namespace ClaudeVS
{
    public partial class CommandInputDialog : Window
    {
        public string CommandName { get; private set; }

        public CommandInputDialog(string defaultCommand)
        {
            InitializeComponent();
            CommandTextBox.Text = defaultCommand;
            CommandTextBox.Focus();
            CommandTextBox.SelectAll();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            CommandName = CommandTextBox.Text;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}