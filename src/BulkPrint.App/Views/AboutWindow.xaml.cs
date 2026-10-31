using System.Diagnostics;
using System.Windows;
using BulkPrint.Core;

namespace BulkPrint.App.Views;

public partial class AboutWindow : Window
{
    private readonly string _noticesPath = Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.txt");

    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = $"Version {AppInfo.Version}";
        BuildText.Text = string.IsNullOrEmpty(AppInfo.BuildDate) ? "" : $"Build date: {AppInfo.BuildDate}";

        string engine = File.Exists(Path.Combine(AppContext.BaseDirectory, "engine", "program", "soffice.exe"))
            ? "Office engine: installed (offline Word, Excel and PowerPoint printing)"
            : "Office engine: not installed (Word, Excel and PowerPoint files cannot be printed)";

        string notices;
        try
        {
            notices = File.Exists(_noticesPath)
                ? File.ReadAllText(_noticesPath)
                : "The third-party notices file was not found next to the program.";
        }
        catch (Exception ex)
        {
            notices = "The third-party notices could not be read: " + ex.Message;
        }
        NoticesText.Text = engine + Environment.NewLine + Environment.NewLine + notices;
    }

    private void OnOpenLicenses(object sender, RoutedEventArgs e)
    {
        try
        {
            if (File.Exists(_noticesPath)) Process.Start(new ProcessStartInfo(_noticesPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
