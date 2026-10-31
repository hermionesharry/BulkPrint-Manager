using System.Diagnostics;
using System.Windows;
using BulkPrint.App.Views;
using BulkPrint.Core;
using BulkPrint.Core.Models;
using BulkPrint.Core.Services;
using Microsoft.Win32;

namespace BulkPrint.App.Services;

internal sealed class WpfDialogService : IDialogService
{
    private readonly IPreviewService _preview;

    public WpfDialogService(IPreviewService preview) => _preview = preview;

    private static Window? Owner =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;

    private static bool ShowCommon(CommonDialog dialog)
    {
        var owner = Owner;
        return (owner is not null && owner.IsVisible ? dialog.ShowDialog(owner) : dialog.ShowDialog()) == true;
    }

    private static T WithOwner<T>(T window) where T : Window
    {
        var owner = Owner;
        if (owner is not null && !ReferenceEquals(owner, window) && owner.IsVisible) window.Owner = owner;
        else window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return window;
    }

    public IReadOnlyList<string>? PickFiles(string filter, string? initialDirectory)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Add documents to print",
            Filter = filter,
            Multiselect = true,
            CheckFileExists = true,
        };
        if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory)) dialog.InitialDirectory = initialDirectory;
        return ShowCommon(dialog) ? dialog.FileNames : null;
    }

    public string? PickFolder(string? initialDirectory, out bool includeSubfolders, bool includeSubfoldersDefault)
    {
        includeSubfolders = includeSubfoldersDefault;
        var dialog = new OpenFolderDialog
        {
            Title = includeSubfoldersDefault
                ? "Choose a folder (documents in its subfolders are included)"
                : "Choose a folder (subfolders are not included)",
            Multiselect = false,
        };
        if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory)) dialog.InitialDirectory = initialDirectory;
        return ShowCommon(dialog) ? dialog.FolderName : null;
    }

    public string? PickSaveFile(string filter, string defaultFileName)
    {
        var dialog = new SaveFileDialog { Filter = filter, FileName = defaultFileName, AddExtension = true, OverwritePrompt = true };
        return ShowCommon(dialog) ? dialog.FileName : null;
    }

    public string? PickOpenFile(string filter)
    {
        var dialog = new OpenFileDialog { Filter = filter, Multiselect = false, CheckFileExists = true };
        return ShowCommon(dialog) ? dialog.FileName : null;
    }

    public PasswordPromptResult AskPassword(string fileName, int otherProtectedCount, bool lastAttemptFailed)
    {
        var w = WithOwner(new PasswordWindow(fileName, otherProtectedCount, lastAttemptFailed));
        w.ShowDialog();
        return w.Result;
    }

    public string? AskText(string title, string prompt, string defaultValue)
    {
        var w = WithOwner(new TextInputWindow(title, prompt, defaultValue));
        return w.ShowDialog() == true ? w.Value : null;
    }

    public bool Confirm(string title, string message) =>
        Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public bool? AskYesNoCancel(string title, string message) =>
        Show(message, title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question) switch
        {
            MessageBoxResult.Yes => true,
            MessageBoxResult.No => false,
            _ => null,
        };

    public void Info(string title, string message) => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    public void Warning(string title, string message) => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    public void Error(string title, string message) => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    private static MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage image)
    {
        var owner = Owner;
        return owner is not null && owner.IsVisible
            ? MessageBox.Show(owner, message, $"{title} - {AppInfo.Name}", buttons, image)
            : MessageBox.Show(message, $"{title} - {AppInfo.Name}", buttons, image);
    }

    public DocumentSettingsResult? EditDocumentSettings(int documentCount, DocumentPrintOptions initial, bool supportsDuplex, bool supportsColor)
    {
        var w = WithOwner(new DocumentSettingsWindow(documentCount, initial, supportsDuplex, supportsColor));
        return w.ShowDialog() == true ? w.Result : null;
    }

    public ProfileChoice? ChooseProfile(IReadOnlyList<string> profileNames)
    {
        var w = WithOwner(new ProfileWindow(profileNames));
        return w.ShowDialog() == true ? w.Result : null;
    }

    public void ShowPreview(DocumentItem item, DocumentPrintOptions options, GlobalPrintSettings global)
    {
        var w = WithOwner(new PreviewWindow(_preview, item, options, global));
        w.ShowDialog();
    }

    public bool ShowReport(BatchReport report)
    {
        var w = WithOwner(new ReportWindow(report));
        w.ShowDialog();
        return w.RetryRequested;
    }

    public void ShowAbout()
    {
        var w = WithOwner(new AboutWindow());
        w.ShowDialog();
    }

    public void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Error("Could not open folder", ex.Message);
        }
    }
}
