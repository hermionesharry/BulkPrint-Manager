using BulkPrint.Core.Models;
using BulkPrint.Core.Services;

namespace BulkPrint.App.Rendering;

/// <summary>Opens any supported document as an <see cref="IPageSource"/>.</summary>
internal sealed class PageSourceFactory
{
    private readonly OfficeConverter _office;

    public PageSourceFactory(OfficeConverter office) => _office = office;

    public OfficeConverter Office => _office;

    /// <param name="textLandscape">Orientation used to lay out TXT/CSV pages.</param>
    public IPageSource Open(DocumentItem item, GlobalPrintSettings global, bool textLandscape, IPrintProgressSink? progress, CancellationToken ct)
    {
        if (!File.Exists(item.Path)) throw new FileNotFoundException("File not found", item.Path);

        switch (item.Kind)
        {
            case DocumentKind.Pdf:
                progress?.SetStatus(PrintStatus.Rendering);
                return PdfPageSource.Open(item.Path, item.Password);

            case DocumentKind.Image:
                progress?.SetStatus(PrintStatus.Rendering);
                return new ImagePageSource(item.Path);

            case DocumentKind.Text:
            case DocumentKind.Csv:
            {
                progress?.SetStatus(PrintStatus.Rendering);
                var paper = PaperCatalog.Get(global.PrinterName, global.PaperSizeName);
                return new TextPageSource(TextPageSource.ReadLines(item), paper.Layout(textLandscape));
            }

            case DocumentKind.Word:
            case DocumentKind.Excel:
            case DocumentKind.PowerPoint:
            {
                var pdf = GetConvertedPdf(item, progress, ct);
                progress?.SetStatus(PrintStatus.Rendering);
                return PdfPageSource.Open(pdf, null);
            }

            default:
                throw new NotSupportedException($"{item.Extension} files are not supported.");
        }
    }

    public bool HasConvertedPdf(DocumentItem item) =>
        item.ConvertedPdfPath is { } p && File.Exists(p);

    public string GetConvertedPdf(DocumentItem item, IPrintProgressSink? progress, CancellationToken ct)
    {
        if (HasConvertedPdf(item)) return item.ConvertedPdfPath!;
        progress?.SetStatus(PrintStatus.Converting, "Converting with the built-in Office engine…");
        var pdf = _office.ConvertToPdf(item.Path, ct);
        item.ConvertedPdfPath = pdf;
        return pdf;
    }
}
