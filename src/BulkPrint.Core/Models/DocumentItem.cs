using BulkPrint.Core.Infrastructure;

namespace BulkPrint.Core.Models;

/// <summary>One document in the print queue.</summary>
public sealed class DocumentItem : ObservableObject
{
    private int? _pageCount;
    private PrintStatus _status = PrintStatus.Pending;
    private string _statusDetail = "";
    private DocumentPrintOptions? _overrides;
    private bool _isPasswordProtected;
    private int _pagesSent;
    private GlobalPrintSettings? _global;

    public DocumentItem(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        FileName = System.IO.Path.GetFileName(Path);
        Folder = System.IO.Path.GetDirectoryName(Path) ?? "";
        Extension = System.IO.Path.GetExtension(Path).ToLowerInvariant();
        Kind = FileTypes.GetKind(Extension);
        TypeLabel = FileTypes.Label(Kind, Extension);
    }

    public string Path { get; }
    public string FileName { get; }
    public string Folder { get; }
    public string Extension { get; }
    public DocumentKind Kind { get; }
    public string TypeLabel { get; }

    public long SizeBytes { get; set; }
    public DateTime Modified { get; set; }
    public string SizeText => FileTypes.FormatSize(SizeBytes);

    public int? PageCount
    {
        get => _pageCount;
        set
        {
            if (Set(ref _pageCount, value)) OnPropertyChanged(nameof(PagesText));
        }
    }

    public string PagesText => PageCount?.ToString() ?? "—";

    public PrintStatus Status
    {
        get => _status;
        set
        {
            if (Set(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(IsBusy));
            }
        }
    }

    public string StatusText => EnumLabels.Of(Status);

    /// <summary>Extra information shown as a tooltip, such as the reason a document failed.</summary>
    public string StatusDetail
    {
        get => _statusDetail;
        set => Set(ref _statusDetail, value ?? "");
    }

    public bool IsBusy => Status is PrintStatus.Converting or PrintStatus.Rendering or PrintStatus.Sending;

    public int PagesSent { get => _pagesSent; set => Set(ref _pagesSent, value); }

    public bool IsPasswordProtected
    {
        get => _isPasswordProtected;
        set => Set(ref _isPasswordProtected, value);
    }

    /// <summary>Kept in memory only. Never written to disk.</summary>
    public string? Password { get; set; }

    /// <summary>Path of a temporary PDF made from an Office file, reused for preview and printing.</summary>
    public string? ConvertedPdfPath { get; set; }

    /// <summary>Per-document settings. Null means the document uses the global settings.</summary>
    public DocumentPrintOptions? Overrides
    {
        get => _overrides;
        set
        {
            if (Set(ref _overrides, value))
            {
                OnPropertyChanged(nameof(HasCustomSettings));
                OnPropertyChanged(nameof(Copies));
                OnPropertyChanged(nameof(SettingsSummary));
            }
        }
    }

    public bool HasCustomSettings => Overrides is not null;

    /// <summary>Links the document to the shared global settings so effective values can be shown.</summary>
    public void AttachGlobal(GlobalPrintSettings global) => _global = global;

    public DocumentPrintOptions EffectiveOptions =>
        Overrides ?? _global ?? new DocumentPrintOptions();

    /// <summary>Copies shown and edited in the queue. Editing creates a per-document override.</summary>
    public int Copies
    {
        get => EffectiveOptions.Copies;
        set
        {
            value = Math.Clamp(value, 1, DocumentPrintOptions.MaxCopies);
            if (value == Copies) return;
            if (Overrides is null)
            {
                var o = (_global ?? new DocumentPrintOptions()).CloneOptions();
                o.Copies = value;
                Overrides = o;
            }
            else
            {
                Overrides.Copies = value;
                OnPropertyChanged(nameof(Copies));
                OnPropertyChanged(nameof(SettingsSummary));
            }
        }
    }

    public string SettingsSummary => HasCustomSettings
        ? "Custom settings: " + EffectiveOptions.Summary()
        : "Uses the global settings";

    /// <summary>Call when the global settings change so the row shows the new effective values.</summary>
    public void NotifyEffectiveChanged()
    {
        OnPropertyChanged(nameof(Copies));
        OnPropertyChanged(nameof(SettingsSummary));
    }

    public void ResetForReprint()
    {
        if (Status is PrintStatus.NeedsPassword or PrintStatus.Unsupported) return;
        Status = PrintStatus.Pending;
        StatusDetail = "";
        PagesSent = 0;
    }
}
