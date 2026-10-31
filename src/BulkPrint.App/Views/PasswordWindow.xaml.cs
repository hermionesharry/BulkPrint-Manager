using System.Windows;
using BulkPrint.Core.Services;

namespace BulkPrint.App.Views;

public partial class PasswordWindow : Window
{
    public PasswordWindow(string fileName, int otherProtectedCount, bool lastAttemptFailed)
    {
        InitializeComponent();
        FileText.Text = $"Enter the password for “{fileName}”.";
        WrongText.Visibility = lastAttemptFailed ? Visibility.Visible : Visibility.Collapsed;
        if (otherProtectedCount > 0)
        {
            ApplyToAll.Content = $"Also try this password for the other {otherProtectedCount} protected file{(otherProtectedCount == 1 ? "" : "s")}";
            ApplyToAll.IsChecked = true;
        }
        else
        {
            ApplyToAll.Visibility = Visibility.Collapsed;
        }
        Result = new PasswordPromptResult(null, false, false);
    }

    internal PasswordPromptResult Result { get; private set; }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (PasswordInput.Password.Length == 0)
        {
            PasswordInput.Focus();
            return;
        }
        Result = new PasswordPromptResult(PasswordInput.Password, ApplyToAll.IsChecked == true, false);
        DialogResult = true;
    }

    private void OnSkip(object sender, RoutedEventArgs e)
    {
        Result = new PasswordPromptResult(null, false, false);
        DialogResult = false;
    }

    private void OnSkipAll(object sender, RoutedEventArgs e)
    {
        Result = new PasswordPromptResult(null, false, true);
        DialogResult = false;
    }
}
