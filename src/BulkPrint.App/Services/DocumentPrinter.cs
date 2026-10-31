using System.Drawing;
using System.Drawing.Printing;
using BulkPrint.App.Rendering;
using BulkPrint.Core.Models;
using BulkPrint.Core.Services;

namespace BulkPrint.App.Services;

/// <summary>Sends one document to the Windows print spooler as a single print job.</summary>
internal sealed class DocumentPrinter : IDocumentPrinter
{
    private readonly PageSourceFactory _sources;
    private readonly IAppLogger _log;

    public DocumentPrinter(PageSourceFactory sources, IAppLogger log)
    {
        _sources = sources;
        _log = log;
    }

    /// <summary>For automated tests: print to a file instead of paper (used with "Microsoft Print to PDF").</summary>
    public Func<DocumentItem, string>? OutputFileOverride { get; set; }

    public PrintOutcome Print(DocumentItem item, DocumentPrintOptions o, GlobalPrintSettings global, bool separatorBefore,
        IPrintProgressSink progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!File.Exists(item.Path)) throw new FileNotFoundException("File not found", item.Path);

        using var doc = new PrintDocument();
        doc.PrintController = new StandardPrintController();
        doc.DocumentName = item.FileName;

        var ps = doc.PrinterSettings;
        ps.PrinterName = global.PrinterName;
        if (!ps.IsValid) throw new InvalidOperationException($"The printer \"{global.PrinterName}\" is not available.");

        if (OutputFileOverride is not null)
        {
            ps.PrintToFile = true;
            ps.PrintFileName = OutputFileOverride(item);
        }

        var page = doc.DefaultPageSettings;
        var paper = PaperCatalog.FindPaper(ps, global.PaperSizeName);
        if (paper is not null) page.PaperSize = paper;
        var source = PaperCatalog.FindSource(ps, global.PaperSourceName);
        if (source is not null) page.PaperSource = source;

        var warnings = new List<string>();
        bool wantColor = o.Color == ColorMode.Color;
        page.Color = wantColor && ps.SupportsColor;
        ApplyQuality(ps, page, global.Quality);

        if (o.Duplex != DuplexMode.Simplex && !ps.CanDuplex)
        {
            warnings.Add("Printed single-sided: this printer does not support double-sided printing.");
            ps.Duplex = Duplex.Simplex;
        }
        else
        {
            ps.Duplex = o.Duplex switch
            {
                DuplexMode.LongEdge => Duplex.Vertical,
                DuplexMode.ShortEdge => Duplex.Horizontal,
                _ => Duplex.Simplex,
            };
        }
        bool duplex = ps.Duplex is Duplex.Vertical or Duplex.Horizontal;

        bool isText = item.Kind is DocumentKind.Text or DocumentKind.Csv;
        bool textLandscape = o.Orientation == OrientationMode.Landscape;

        using var pages = _sources.Open(item, global, textLandscape, progress, ct);
        if (item.PageCount != pages.PageCount) item.PageCount = pages.PageCount;
        ct.ThrowIfCancellationRequested();

        var selected = PageRangeParser.SelectPages(pages.PageCount, o.PageSelection, o.PageRange);
        if (selected.Count == 0)
        {
            throw new InvalidOperationException(o.PageSelection == PageSelectionMode.Range
                ? $"The page range \"{o.PageRange}\" does not include any of this document's {pages.PageCount} page{(pages.PageCount == 1 ? "" : "s")}."
                : "No pages match the selected options (for example, even pages of a one-page document).");
        }

        bool driverCopies = global.UsePrinterCopies && o.Copies > 1 && ps.MaximumCopies >= o.Copies;
        var plan = PrintPlan.Build(selected, o.Copies, o.Collate, duplex, driverCopies, separatorBefore);
        ps.Copies = (short)Math.Clamp(plan.DriverCopies, 1, short.MaxValue);
        ps.Collate = plan.DriverCollate;

        bool autoOrientation = o.Orientation == OrientationMode.Auto && !isText;
        page.Landscape = isText
            ? textLandscape
            : o.Orientation switch
            {
                OrientationMode.Landscape => true,
                OrientationMode.Portrait => false,
                _ => PageDrawing.IsLandscape(pages.GetPageSizeInches(selected[0])),
            };
        bool defaultLandscape = page.Landscape;
        bool monochrome = !wantColor;
        float qualityDpi = PageDrawing.QualityDpi(global.Quality);
        int index = 0, pagesSent = 0;
        Exception? drawError = null;

        doc.QueryPageSettings += (_, e) =>
        {
            if (index >= plan.Sides.Count) return;
            var side = plan.Sides[index];
            e.PageSettings.Landscape = autoOrientation && side.Kind == SheetKind.Page
                ? PageDrawing.IsLandscape(pages.GetPageSizeInches(side.PageIndex))
                : defaultLandscape;
        };

        doc.PrintPage += (_, e) =>
        {
            if (ct.IsCancellationRequested || e.Graphics is null)
            {
                e.Cancel = true;
                e.HasMorePages = false;
                return;
            }
            try
            {
                var side = plan.Sides[index];
                var layout = LayoutFromEvent(e);
                float dpi = Math.Min(Math.Max(96f, e.Graphics.DpiX), qualityDpi);
                progress.SetStatus(PrintStatus.Sending, $"Sending page {index + 1} of {plan.Sides.Count}");
                switch (side.Kind)
                {
                    case SheetKind.Page:
                        pages.Draw(e.Graphics, side.PageIndex, layout, o.Scaling, monochrome, dpi);
                        for (int c = 0; c < plan.DriverCopies; c++)
                        {
                            pagesSent++;
                            progress.PageSent();
                        }
                        break;
                    case SheetKind.Separator:
                        PageDrawing.DrawSeparator(e.Graphics, layout, item);
                        break;
                    case SheetKind.Blank:
                        break;
                }
                index++;
                e.HasMorePages = index < plan.Sides.Count;
            }
            catch (Exception ex)
            {
                // Stop this job cleanly; the error is reported for this document only.
                drawError = ex;
                e.Cancel = true;
                e.HasMorePages = false;
            }
        };

        try
        {
            doc.Print();
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }

        if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
        if (drawError is not null)
        {
            _log.Error($"Drawing failed for '{item.Path}'.", drawError);
            throw new InvalidOperationException($"Page {index + 1} could not be printed: {drawError.Message}", drawError);
        }

        if (!page.Color && wantColor && !ps.SupportsColor)
            warnings.Add("Printed in black and white: this printer does not support colour.");

        return new PrintOutcome(pagesSent, warnings.Count == 0 ? null : string.Join(" ", warnings));
    }

    /// <summary>Paper and printable area in the coordinates of the printer Graphics (origin at the hard margin).</summary>
    private static SheetLayout LayoutFromEvent(PrintPageEventArgs e)
    {
        var g = e.Graphics!;
        float hardX = e.PageSettings.HardMarginX, hardY = e.PageSettings.HardMarginY;
        var paper = new RectangleF(-hardX, -hardY, e.PageBounds.Width, e.PageBounds.Height);
        var clip = g.VisibleClipBounds;
        var printable = RectangleF.Intersect(clip, paper);
        if (printable.Width <= 1 || printable.Height <= 1) printable = new RectangleF(0, 0, paper.Width - 2 * hardX, paper.Height - 2 * hardY);
        return new SheetLayout(paper, printable);
    }

    private static void ApplyQuality(PrinterSettings ps, PageSettings page, PrintQuality quality)
    {
        var kind = quality switch
        {
            PrintQuality.Draft => PrinterResolutionKind.Draft,
            PrintQuality.High => PrinterResolutionKind.High,
            _ => (PrinterResolutionKind?)null,
        };
        if (kind is null) return;
        try
        {
            foreach (PrinterResolution r in ps.PrinterResolutions)
            {
                if (r.Kind == kind)
                {
                    page.PrinterResolution = r;
                    return;
                }
            }
        }
        catch
        {
            // Some drivers do not report resolutions; the default is used.
        }
    }
}
