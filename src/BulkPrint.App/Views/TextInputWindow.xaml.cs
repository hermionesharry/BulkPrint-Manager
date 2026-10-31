using System.Windows;

namespace BulkPrint.App.Views;

public partial class TextInputWindow : Window
{
    public TextInputWindow(string title, string prompt, string defaultValue)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        Input.Text = defaultValue;
        Input.SelectAll();
    }

    public string Value => Input.Text.Trim();

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (Value.Length == 0)
        {
            Input.Focus();
            return;
        }
        DialogResult = true;
    }
}
