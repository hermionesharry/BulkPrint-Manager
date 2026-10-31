using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Media.Imaging;
using BulkPrint.App.Rendering;
using BulkPrint.Core.Models;
using BulkPrint.Core.Services;

namespace BulkPrint.App.Services;

/// <summary>
/// Draws a selected page onto a picture of the chosen paper, using exactly the same layout code
/// as printing, so the preview reflects paper size, orientation and scaling.
/// </summary>
internal sealed class PreviewService : IPreviewService
{
    private readonly PageSourceFactory _sources;

    public PreviewService(PageSourceFactory sources) => _sources = sources;

    public Task<PreviewImage> RenderAsync(DocumentItem item, DocumentPrintOptions options, GlobalPrintSettings global,
        int selectedPageNumber, double pixelsPerInch, bool allowConversion, CancellationToken ct) =>
        Task.Run(() => Render(item, options, global, selectedPageNumber, (float)pixelsPerInch, allowConversion, ct), ct);

    private PreviewImage Render(DocumentItem item, DocumentPrintOptions options, GlobalPrintSettings global,
        int selectedPageNumber, float ppi, bool allowConversion, CancellationToken ct)
    {
        switch (item.Status)
        {
            case PrintStatus.NeedsPassword:
                return Message("This PDF is password-protected. Unlock it (Settings > Unlock password-protected files) to preview it.");
            case PrintStatus.Unsupported:
                return Message(item.StatusDetail.Length > 0 ? item.StatusDetail : "This document cannot be printed.");
        }
        if (!File.Exists(item.Path)) return Message("The file could not be found.");
        if (FileTypes.NeedsOfficeEngine(item.Kind))
        {
            if (!_sources.Office.IsAvailable) return Message("The built-in Office engine is not installed.");
            if (!allowConversion && !_sources.HasConvertedPdf(item))
                return Message("Click Preview to convert and view this Office document.");
        }

        bool isText = item.Kind is DocumentKind.Text or DocumentKind.Csv;
        bool textLandscape = options.Orientation == OrientationMode.Landscape;
        using var source = _sources.Open(item, global, textLandscape, null, ct);
        if (item.PageCount != source.PageCount) item.PageCount = source.PageCount;
        ct.ThrowIfCancellationRequested();

        IReadOnlyList<int> pages;
        try
        {
            pages = PageRangeParser.SelectPages(source.PageCount, options.PageSelection, options.PageRange);
        }
        catch (FormatException ex)
        {
            return Message(ex.Message);
        }
        if (pages.Count == 0) return Message("No pages match the selected page options.");

        int number = Math.Clamp(selectedPageNumber, 1, pages.Count);
        int pageIndex = pages[number - 1];
        bool landscape = isText
            ? textLandscape
            : options.Orientation switch
            {
                OrientationMode.Landscape => true,
                OrientationMode.Portrait => false,
                _ => PageDrawing.IsLandscape(source.GetPageSizeInches(pageIndex)),
            };

        var paper = PaperCatalog.Get(global.PrinterName, global.PaperSizeName);
        var layout = paper.Layout(landscape);
        int width = Math.Max(1, (int)Math.Round(layout.Paper.Width / 100f * ppi));
        int height = Math.Max(1, (int)Math.Round(layout.Paper.Height / 100f * ppi));

        using var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        bitmap.SetResolution(ppi, ppi);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.White);
            g.PageUnit = GraphicsUnit.Inch;
            g.PageScale = 0.01f; // drawing units are 1/100 inch, the same as the printer
            g.SmoothingMode = SmoothingMode.HighQuality;
            source.Draw(g, pageIndex, layout, options.Scaling, options.Color == ColorMode.BlackAndWhite, Math.Max(ppi * 1.5f, 96f));

            // Show the printer's printable area as a faint dashed line.
            using var pen = new Pen(Color.FromArgb(150, 120, 140, 170), 0.8f) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(pen, layout.Printable.X, layout.Printable.Y, layout.Printable.Width, layout.Printable.Height);
        }

        var image = ToBitmapSource(bitmap);
        var description = $"{paper.Name} · {(landscape ? "Landscape" : "Portrait")} · {EnumLabels.Of(options.Scaling)} · " +
                          $"page {pageIndex + 1} of {source.PageCount}";
        return new PreviewImage(image, number, pages.Count, description, null);
    }

    private static PreviewImage Message(string text) => new(null, 0, 0, "", text);

    private static BitmapSource ToBitmapSource(Bitmap bitmap)
    {
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        ms.Position = 0;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = ms;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
