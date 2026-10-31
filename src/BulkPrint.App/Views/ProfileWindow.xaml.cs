using System.Windows;
using BulkPrint.Core;
using BulkPrint.Core.Services;

namespace BulkPrint.App.Views;

public partial class ProfileWindow : Window
{
    public ProfileWindow(IReadOnlyList<string> names)
    {
        InitializeComponent();
        Profiles.ItemsSource = names;
        if (names.Count > 0) Profiles.SelectedIndex = 0;
    }

    internal ProfileChoice? Result { get; private set; }

    private string? Selected => Profiles.SelectedItem as string;

    private void OnLoad(object sender, RoutedEventArgs e)
    {
        if (Selected is null) return;
        Result = new ProfileChoice(Selected, false);
        DialogResult = true;
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (Selected is null) return;
        if (MessageBox.Show(this, $"Delete the profile \"{Selected}\"?", AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        Result = new ProfileChoice(Selected, true);
        DialogResult = true;
    }
}
