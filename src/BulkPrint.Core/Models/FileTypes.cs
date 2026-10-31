namespace BulkPrint.Core.Models;

public static class FileTypes
{
    private static readonly Dictionary<string, DocumentKind> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = DocumentKind.Pdf,
        [".doc"] = DocumentKind.Word,
        [".docx"] = DocumentKind.Word,
        [".rtf"] = DocumentKind.Word,
        [".xls"] = DocumentKind.Excel,
        [".xlsx"] = DocumentKind.Excel,
        [".csv"] = DocumentKind.Csv,
        [".ppt"] = DocumentKind.PowerPoint,
        [".pptx"] = DocumentKind.PowerPoint,
        [".jpg"] = DocumentKind.Image,
        [".jpeg"] = DocumentKind.Image,
        [".png"] = DocumentKind.Image,
        [".bmp"] = DocumentKind.Image,
        [".tif"] = DocumentKind.Image,
        [".tiff"] = DocumentKind.Image,
        [".txt"] = DocumentKind.Text,
    };

    public static IReadOnlyCollection<string> SupportedExtensions => Map.Keys;

    public static DocumentKind GetKind(string extension) =>
        Map.TryGetValue(extension, out var kind) ? kind : DocumentKind.Unsupported;

    public static bool IsSupported(string path) =>
        GetKind(Path.GetExtension(path)) != DocumentKind.Unsupported;

    /// <summary>Word, Excel and PowerPoint files need the bundled office conversion engine.</summary>
    public static bool NeedsOfficeEngine(DocumentKind kind) =>
        kind is DocumentKind.Word or DocumentKind.Excel or DocumentKind.PowerPoint;

    public static string Label(DocumentKind kind, string extension)
    {
        var ext = extension.TrimStart('.').ToUpperInvariant();
        return kind switch
        {
            DocumentKind.Pdf => "PDF",
            DocumentKind.Word => $"Word ({ext})",
            DocumentKind.Excel => $"Excel ({ext})",
            DocumentKind.Csv => "CSV",
            DocumentKind.PowerPoint => $"PowerPoint ({ext})",
            DocumentKind.Image => $"Image ({ext})",
            DocumentKind.Text => "Text",
            _ => ext,
        };
    }

    /// <summary>Filter string for the Windows open-file dialog.</summary>
    public static string OpenDialogFilter =>
        "All supported documents|*.pdf;*.doc;*.docx;*.rtf;*.xls;*.xlsx;*.csv;*.ppt;*.pptx;*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff;*.txt" +
        "|PDF documents (*.pdf)|*.pdf" +
        "|Word documents (*.doc;*.docx;*.rtf)|*.doc;*.docx;*.rtf" +
        "|Excel and CSV (*.xls;*.xlsx;*.csv)|*.xls;*.xlsx;*.csv" +
        "|PowerPoint (*.ppt;*.pptx)|*.ppt;*.pptx" +
        "|Images (*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff)|*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff" +
        "|Text files (*.txt)|*.txt" +
        "|All files (*.*)|*.*";

    public static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double kb = bytes / 1024.0;
        if (kb < 1024) return $"{kb:0.#} KB";
        double mb = kb / 1024.0;
        if (mb < 1024) return $"{mb:0.#} MB";
        return $"{mb / 1024.0:0.##} GB";
    }
}
