namespace BulkPrint.Core.Models;

public enum DocumentKind { Pdf, Word, Excel, PowerPoint, Image, Text, Csv, Unsupported }

public enum PrintStatus
{
    Pending,
    NeedsPassword,
    Unsupported,
    Converting,
    Rendering,
    Sending,
    Printed,
    Skipped,
    Failed,
}

public enum PageSelectionMode { All, Range, Odd, Even }

public enum OrientationMode { Auto, Portrait, Landscape }

public enum DuplexMode { Simplex, LongEdge, ShortEdge }

public enum ColorMode { Color, BlackAndWhite }

public enum ScalingMode
{
    /// <summary>Fit the page inside the printer's printable area (keeps proportions).</summary>
    FitToPage,
    /// <summary>Print at 100% size, centred on the paper.</summary>
    ActualSize,
    /// <summary>Scale to the full paper edge (keeps proportions; printer margins may clip edges).</summary>
    FitToPaper,
}

public enum PrintQuality { Draft, Normal, High }

public enum SeparatorMode { None, BlankPage, FileNamePage }

public enum SortMode
{
    NameAscending,
    NameDescending,
    FullPath,
    DateOldestFirst,
    DateNewestFirst,
    FileType,
    SizeSmallestFirst,
}

public static class EnumLabels
{
    public static string Of(PrintStatus s) => s switch
    {
        PrintStatus.Pending => "Pending",
        PrintStatus.NeedsPassword => "Password needed",
        PrintStatus.Unsupported => "Cannot print",
        PrintStatus.Converting => "Converting…",
        PrintStatus.Rendering => "Rendering…",
        PrintStatus.Sending => "Sending…",
        PrintStatus.Printed => "Printed",
        PrintStatus.Skipped => "Skipped",
        PrintStatus.Failed => "Failed",
        _ => s.ToString(),
    };

    public static string Of(PageSelectionMode m) => m switch
    {
        PageSelectionMode.All => "All pages",
        PageSelectionMode.Range => "Page range",
        PageSelectionMode.Odd => "Odd pages only",
        PageSelectionMode.Even => "Even pages only",
        _ => m.ToString(),
    };

    public static string Of(OrientationMode m) => m switch
    {
        OrientationMode.Auto => "Automatic",
        OrientationMode.Portrait => "Portrait",
        OrientationMode.Landscape => "Landscape",
        _ => m.ToString(),
    };

    public static string Of(DuplexMode m) => m switch
    {
        DuplexMode.Simplex => "Single-sided",
        DuplexMode.LongEdge => "Double-sided (flip on long edge)",
        DuplexMode.ShortEdge => "Double-sided (flip on short edge)",
        _ => m.ToString(),
    };

    public static string Of(ColorMode m) => m == ColorMode.Color ? "Colour" : "Black and white";

    public static string Of(ScalingMode m) => m switch
    {
        ScalingMode.FitToPage => "Fit to page (printable area)",
        ScalingMode.ActualSize => "Actual size (100%)",
        ScalingMode.FitToPaper => "Scale to fit paper (edge to edge)",
        _ => m.ToString(),
    };

    public static string Of(PrintQuality q) => q switch
    {
        PrintQuality.Draft => "Draft (faster)",
        PrintQuality.Normal => "Normal",
        PrintQuality.High => "High",
        _ => q.ToString(),
    };

    public static string Of(SeparatorMode m) => m switch
    {
        SeparatorMode.None => "None",
        SeparatorMode.BlankPage => "Blank page",
        SeparatorMode.FileNamePage => "Page showing the file name",
        _ => m.ToString(),
    };

    public static string Of(SortMode m) => m switch
    {
        SortMode.NameAscending => "Name (A to Z)",
        SortMode.NameDescending => "Name (Z to A)",
        SortMode.FullPath => "Folder and file name",
        SortMode.DateOldestFirst => "Date modified (oldest first)",
        SortMode.DateNewestFirst => "Date modified (newest first)",
        SortMode.FileType => "File type",
        SortMode.SizeSmallestFirst => "Size (smallest first)",
        _ => m.ToString(),
    };
}
