using System.Windows;
using BulkPrint.Core;

namespace BulkPrint.App.Views;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
        VersionText.Text = "Version " + AppInfo.Version;
    }
}
