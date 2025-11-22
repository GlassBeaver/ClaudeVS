using System.Windows;
using System.Windows.Controls;

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

        private void CommandPresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CommandPresetCombo.SelectedItem is ComboBoxItem selectedItem)
            {
                string value = selectedItem.Tag?.ToString() ?? selectedItem.Content.ToString();
                CommandTextBox.Text = value;
            }
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