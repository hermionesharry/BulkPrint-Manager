using System.Text.Json.Serialization;
using BulkPrint.Core.Infrastructure;

namespace BulkPrint.Core.Models;

/// <summary>Settings that can differ per document.</summary>
public class DocumentPrintOptions : ObservableObject
{
    public const int MaxCopies = 999;

    private int _copies = 1;
    private bool _collate = true;
    private PageSelectionMode _pageSelection = PageSelectionMode.All;
    private string _pageRange = "";
    private OrientationMode _orientation = OrientationMode.Auto;
    private DuplexMode _duplex = DuplexMode.Simplex;
    private ColorMode _color = ColorMode.Color;
    private ScalingMode _scaling = ScalingMode.FitToPage;

    public int Copies
    {
        get => _copies;
        set => Set(ref _copies, Math.Clamp(value, 1, MaxCopies));
    }

    public bool Collate { get => _collate; set => Set(ref _collate, value); }

    public PageSelectionMode PageSelection
    {
        get => _pageSelection;
        set
        {
            if (Set(ref _pageSelection, value)) OnPropertyChanged(nameof(IsRangeSelection));
        }
    }

    /// <summary>For example "1-3, 5, 8-". Used when <see cref="PageSelection"/> is Range.</summary>
    public string PageRange { get => _pageRange; set => Set(ref _pageRange, value ?? ""); }

    public OrientationMode Orientation { get => _orientation; set => Set(ref _orientation, value); }
    public DuplexMode Duplex { get => _duplex; set => Set(ref _duplex, value); }
    public ColorMode Color { get => _color; set => Set(ref _color, value); }
    public ScalingMode Scaling { get => _scaling; set => Set(ref _scaling, value); }

    [JsonIgnore]
    public bool IsRangeSelection => PageSelection == PageSelectionMode.Range;

    public DocumentPrintOptions CloneOptions()
    {
        var o = new DocumentPrintOptions();
        o.CopyOptionsFrom(this);
        return o;
    }

    public void CopyOptionsFrom(DocumentPrintOptions other)
    {
        Copies = other.Copies;
        Collate = other.Collate;
        PageSelection = other.PageSelection;
        PageRange = other.PageRange;
        Orientation = other.Orientation;
        Duplex = other.Duplex;
        Color = other.Color;
        Scaling = other.Scaling;
    }

    public bool SameOptionsAs(DocumentPrintOptions other) =>
        Copies == other.Copies && Collate == other.Collate && PageSelection == other.PageSelection &&
        string.Equals(PageRange.Trim(), other.PageRange.Trim(), StringComparison.Ordinal) &&
        Orientation == other.Orientation && Duplex == other.Duplex && Color == other.Color && Scaling == other.Scaling;

    /// <summary>Returns an error message if the options are invalid, otherwise null.</summary>
    public string? Validate()
    {
        if (PageSelection == PageSelectionMode.Range && !PageRangeParser.TryParse(PageRange, out _, out var err))
            return err;
        return null;
    }

    public string Summary()
    {
        var parts = new List<string>
        {
            Copies == 1 ? "1 copy" : $"{Copies} copies",
            PageSelection == PageSelectionMode.Range ? $"pages {PageRange}" : EnumLabels.Of(PageSelection).ToLowerInvariant(),
            EnumLabels.Of(Duplex).ToLowerInvariant(),
            EnumLabels.Of(Color).ToLowerInvariant(),
        };
        return string.Join(" · ", parts);
    }
}

/// <summary>Batch-wide settings: printer, paper and job handling, plus the default document options.</summary>
public sealed class GlobalPrintSettings : DocumentPrintOptions
{
    private string? _printerName;
    private string? _paperSizeName;
    private string? _paperSourceName;
    private PrintQuality _quality = PrintQuality.Normal;
    private SeparatorMode _separator = SeparatorMode.None;
    private int _delayBetweenDocumentsSeconds;
    private int _maxJobsInPrinterQueue = 20;
    private bool _usePrinterCopies;
    private bool _saveOutput = true;
    private string? _outputFolder;

    public string? PrinterName { get => _printerName; set => Set(ref _printerName, value); }
    public string? PaperSizeName { get => _paperSizeName; set => Set(ref _paperSizeName, value); }
    public string? PaperSourceName { get => _paperSourceName; set => Set(ref _paperSourceName, value); }
    public PrintQuality Quality { get => _quality; set => Set(ref _quality, value); }
    public SeparatorMode Separator { get => _separator; set => Set(ref _separator, value); }

    public int DelayBetweenDocumentsSeconds
    {
        get => _delayBetweenDocumentsSeconds;
        set => Set(ref _delayBetweenDocumentsSeconds, Math.Clamp(value, 0, 600));
    }

    /// <summary>Wait before sending more when the printer already has this many jobs waiting. 0 = no limit.</summary>
    public int MaxJobsInPrinterQueue
    {
        get => _maxJobsInPrinterQueue;
        set => Set(ref _maxJobsInPrinterQueue, Math.Clamp(value, 0, 1000));
    }

    /// <summary>
    /// When true, copies and collation are passed to the printer driver (faster, smaller spool files).
    /// When false (default), BulkPrint Manager sends each copy itself, which works the same on every driver.
    /// </summary>
    public bool UsePrinterCopies { get => _usePrinterCopies; set => Set(ref _usePrinterCopies, value); }

    /// <summary>
    /// Save Print Output (On/Off). When On and the printer saves to a file (for example
    /// "Microsoft Print to PDF"), each document is saved automatically into <see cref="OutputFolder"/>
    /// with its own name, so Windows never asks "Save Print Output As" for every document.
    /// </summary>
    public bool SaveOutput { get => _saveOutput; set => Set(ref _saveOutput, value); }

    /// <summary>Folder for saved print output. Null or empty means Documents\BulkPrint Output.</summary>
    public string? OutputFolder
    {
        get => _outputFolder;
        set
        {
            if (Set(ref _outputFolder, value)) OnPropertyChanged(nameof(EffectiveOutputFolder));
        }
    }

    [JsonIgnore]
    public string EffectiveOutputFolder => string.IsNullOrWhiteSpace(OutputFolder) ? AppInfo.DefaultOutputFolder : OutputFolder!;

    public GlobalPrintSettings CloneGlobal()
    {
        var g = new GlobalPrintSettings();
        g.CopyGlobalFrom(this);
        return g;
    }

    public void CopyGlobalFrom(GlobalPrintSettings other)
    {
        CopyOptionsFrom(other);
        PrinterName = other.PrinterName;
        PaperSizeName = other.PaperSizeName;
        PaperSourceName = other.PaperSourceName;
        Quality = other.Quality;
        Separator = other.Separator;
        DelayBetweenDocumentsSeconds = other.DelayBetweenDocumentsSeconds;
        MaxJobsInPrinterQueue = other.MaxJobsInPrinterQueue;
        UsePrinterCopies = other.UsePrinterCopies;
        SaveOutput = other.SaveOutput;
        OutputFolder = other.OutputFolder;
    }
}
