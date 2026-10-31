using BulkPrint.Core.Models;

namespace BulkPrint.Core.Services;

public sealed record PrinterCapabilities(
    IReadOnlyList<string> PaperSizes,
    IReadOnlyList<string> PaperSources,
    bool SupportsDuplex,
    bool SupportsColor,
    int MaxCopies,
    bool IsValid,
    string? DefaultPaperSize = null,
    string? DefaultPaperSource = null)
{
    public static readonly PrinterCapabilities None = new(Array.Empty<string>(), Array.Empty<string>(), false, false, 1, false);
}

public sealed record PrinterStatusInfo(bool IsReady, bool HasProblem, string Text, int JobsInQueue)
{
    public static PrinterStatusInfo Unknown(string text = "Status not reported by this printer") => new(true, false, text, 0);
}

public interface IPrinterService
{
    IReadOnlyList<string> GetPrinterNames();
    string? GetDefaultPrinterName();
    PrinterCapabilities GetCapabilities(string printerName);
    PrinterStatusInfo GetStatus(string printerName);
}

public sealed record PrintOutcome(int PagesSent, string? Warning = null);

/// <summary>Reports progress of one document back to the engine.</summary>
public interface IPrintProgressSink
{
    void SetStatus(PrintStatus status, string? detail = null);
    void PageSent();
}

public interface IDocumentPrinter
{
    /// <summary>Prints one document synchronously. Throws on failure, OperationCanceledException on cancel.</summary>
    PrintOutcome Print(
        DocumentItem item,
        DocumentPrintOptions options,
        GlobalPrintSettings global,
        bool separatorBefore,
        IPrintProgressSink progress,
        CancellationToken cancellationToken);
}

public interface IDocumentInspector
{
    bool OfficeEngineAvailable { get; }

    /// <summary>Reads page count and detects password protection. Updates the item.</summary>
    Task InspectAsync(DocumentItem item, GlobalPrintSettings global, CancellationToken cancellationToken);

    /// <summary>Tries a password. On success sets the password and page count and returns true.</summary>
    Task<bool> TryUnlockAsync(DocumentItem item, string password, CancellationToken cancellationToken);

    /// <summary>Deletes temporary converted files.</summary>
    void CleanUp();
}

public sealed record PreviewImage(object? Image, int SheetNumber, int SheetCount, string Description, string? Error);

public interface IPreviewService
{
    /// <summary>Renders one selected page on a sheet of the chosen paper. Image is a UI image object.</summary>
    Task<PreviewImage> RenderAsync(DocumentItem item, DocumentPrintOptions options, GlobalPrintSettings global,
        int selectedPageNumber, double pixelsPerInch, bool allowConversion, CancellationToken cancellationToken);
}

public sealed record PasswordPromptResult(string? Password, bool ApplyToAll, bool SkipAll);

public sealed record DocumentSettingsResult(DocumentPrintOptions? Options, bool ResetToGlobal);

public sealed record ProfileChoice(string Name, bool Delete);

public interface IDialogService
{
    IReadOnlyList<string>? PickFiles(string filter, string? initialDirectory);
    string? PickFolder(string? initialDirectory, out bool includeSubfolders, bool includeSubfoldersDefault);
    string? PickSaveFile(string filter, string defaultFileName);
    string? PickOpenFile(string filter);
    PasswordPromptResult AskPassword(string fileName, int otherProtectedCount, bool lastAttemptFailed);
    string? AskText(string title, string prompt, string defaultValue);
    bool Confirm(string title, string message);
    /// <summary>Returns true for Yes, false for No, null for Cancel.</summary>
    bool? AskYesNoCancel(string title, string message);
    void Info(string title, string message);
    void Warning(string title, string message);
    void Error(string title, string message);
    DocumentSettingsResult? EditDocumentSettings(int documentCount, DocumentPrintOptions initial, bool supportsDuplex, bool supportsColor);
    ProfileChoice? ChooseProfile(IReadOnlyList<string> profileNames);
    void ShowPreview(DocumentItem item, DocumentPrintOptions options, GlobalPrintSettings global);
    /// <summary>Shows the batch report. Returns true if the user asked to retry the failed documents.</summary>
    bool ShowReport(BatchReport report);
    void ShowAbout();
    void OpenFolder(string path);
}

public interface IUiDispatcher
{
    void Post(Action action);
    Task InvokeAsync(Action action);
}
