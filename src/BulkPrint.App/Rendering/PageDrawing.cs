using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using BulkPrint.Core.Models;

namespace BulkPrint.App.Rendering;

/// <summary>
/// Paper and printable area for one side of a sheet, in hundredths of an inch, expressed in the
/// coordinate space of the Graphics object being drawn on.
/// </summary>
internal readonly record struct SheetLayout(RectangleF Paper, RectangleF Printable)
{
    /// <summary>Area used for text pages: the printable area, kept at least 0.5 inch from the paper edge.</summary>
    public RectangleF TextArea
    {
        get
        {
            var inset = RectangleF.Inflate(Paper, -50, -50);
            var r = RectangleF.Intersect(inset, Printable);
            return r.Width > 100 && r.Height > 100 ? r : Printable;
        }
    }
}

/// <summary>Paper size and printable area for a printer + paper combination (portrait, 1/100 inch).</summary>
internal sealed record PaperInfo(string Name, SizeF Size, RectangleF Printable)
{
    public static readonly PaperInfo A4Fallback = new("A4", new SizeF(827, 1169), new RectangleF(25, 25, 777, 1119));

    public SheetLayout Layout(bool landscape)
    {
        if (!landscape) return new SheetLayout(new RectangleF(0, 0, Size.Width, Size.Height), Printable);
        return new SheetLayout(
            new RectangleF(0, 0, Size.Height, Size.Width),
            new RectangleF(Printable.Y, Printable.X, Printable.Height, Printable.Width));
    }
}

internal static class PaperCatalog
{
    private static readonly ConcurrentDictionary<string, PaperInfo> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Reads paper dimensions from the printer driver (cached). Falls back to A4.</summary>
    public static PaperInfo Get(string? printerName, string? paperName)
    {
        var key = $"{printerName}|{paperName}";
        return Cache.GetOrAdd(key, _ => Read(printerName, paperName));
    }

    private static PaperInfo Read(string? printerName, string? paperName)
    {
        try
        {
            var ps = new PrinterSettings();
            if (!string.IsNullOrEmpty(printerName)) ps.PrinterName = printerName;
            if (!ps.IsValid) return A4Fallback;
            var page = new PageSettings(ps) { Landscape = false };
            var size = FindPaper(ps, paperName);
            if (size is not null) page.PaperSize = size;
            var paper = page.PaperSize;
            var printable = page.PrintableArea;
            float w = paper.Width, h = paper.Height;
            if (w <= 0 || h <= 0) return A4Fallback;
            if (printable.Width <= 0 || printable.Height <= 0) printable = new RectangleF(25, 25, w - 50, h - 50);
            return new PaperInfo(paper.PaperName, new SizeF(w, h), printable);
        }
        catch
        {
            return A4Fallback;
        }
    }

    public static PaperSize? FindPaper(PrinterSettings ps, string? paperName)
    {
        if (string.IsNullOrEmpty(paperName)) return null;
        foreach (PaperSize p in ps.PaperSizes)
            if (string.Equals(p.PaperName, paperName, StringComparison.OrdinalIgnoreCase)) return p;
        return null;
    }

    public static PaperSource? FindSource(PrinterSettings ps, string? sourceName)
    {
        if (string.IsNullOrEmpty(sourceName)) return null;
        foreach (PaperSource s in ps.PaperSources)
            if (string.Equals(s.SourceName, sourceName, StringComparison.OrdinalIgnoreCase)) return s;
        return null;
    }

    public static void Clear() => Cache.Clear();
}

internal static class PageDrawing
{
    public const long MaxRenderPixels = 40_000_000;

    public static bool IsLandscape(SizeF pageInches) => pageInches.Width > pageInches.Height * 1.02f;

    /// <summary>Where the page lands on the sheet for the chosen scaling (1/100 inch).</summary>
    public static RectangleF ComputeTarget(SizeF pageInches, SheetLayout layout, ScalingMode scaling)
    {
        float w = Math.Max(1f, pageInches.Width * 100f);
        float h = Math.Max(1f, pageInches.Height * 100f);
        return scaling switch
        {
            ScalingMode.ActualSize => new RectangleF(
                layout.Paper.X + (layout.Paper.Width - w) / 2f,
                layout.Paper.Y + (layout.Paper.Height - h) / 2f, w, h),
            ScalingMode.FitToPaper => Fit(w, h, layout.Paper),
            _ => Fit(w, h, layout.Printable),
        };
    }

    private static RectangleF Fit(float w, float h, RectangleF box)
    {
        float s = Math.Min(box.Width / w, box.Height / h);
        float sw = w * s, sh = h * s;
        return new RectangleF(box.X + (box.Width - sw) / 2f, box.Y + (box.Height - sh) / 2f, sw, sh);
    }

    /// <summary>Pixel size to render a page so it is sharp at the given resolution, with a memory cap.</summary>
    public static Size PixelSize(RectangleF target, float dpi)
    {
        double w = target.Width / 100.0 * dpi, h = target.Height / 100.0 * dpi;
        double pixels = w * h;
        if (pixels > MaxRenderPixels)
        {
            double s = Math.Sqrt(MaxRenderPixels / pixels);
            w *= s;
            h *= s;
        }
        return new Size(Math.Max(1, (int)Math.Round(w)), Math.Max(1, (int)Math.Round(h)));
    }

    public static ImageAttributes? Attributes(bool monochrome)
    {
        if (!monochrome) return null;
        var matrix = new ColorMatrix(new[]
        {
            new[] { 0.299f, 0.299f, 0.299f, 0f, 0f },
            new[] { 0.587f, 0.587f, 0.587f, 0f, 0f },
            new[] { 0.114f, 0.114f, 0.114f, 0f, 0f },
            new[] { 0f, 0f, 0f, 1f, 0f },
            new[] { 0f, 0f, 0f, 0f, 1f },
        });
        var attributes = new ImageAttributes();
        attributes.SetColorMatrix(matrix);
        return attributes;
    }

    public static void DrawImage(Graphics g, Image image, RectangleF target, bool monochrome)
    {
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.SmoothingMode = SmoothingMode.HighQuality;
        var dest = new[]
        {
            new PointF(target.Left, target.Top),
            new PointF(target.Right, target.Top),
            new PointF(target.Left, target.Bottom),
        };
        var src = new RectangleF(0, 0, image.Width, image.Height);
        using var attributes = Attributes(monochrome);
        g.DrawImage(image, dest, src, GraphicsUnit.Pixel, attributes);
    }

    public static float QualityDpi(PrintQuality quality) => quality switch
    {
        PrintQuality.Draft => 150f,
        PrintQuality.High => 450f,
        _ => 300f,
    };

    /// <summary>Separator page between documents: shows the next file's name. Never shows any branding.</summary>
    public static void DrawSeparator(Graphics g, SheetLayout layout, DocumentItem item)
    {
        var area = layout.TextArea;
        using var title = new Font("Segoe UI", 12f, FontStyle.Regular, GraphicsUnit.Point);
        using var name = new Font("Segoe UI", 20f, FontStyle.Bold, GraphicsUnit.Point);
        using var small = new Font("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Point);
        using var format = new StringFormat { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisPath };
        float y = area.Top + area.Height * 0.35f;
        g.DrawString("Next document", title, Brushes.Black, new RectangleF(area.Left, y, area.Width, 40), format);
        y += 45;
        g.DrawString(item.FileName, name, Brushes.Black, new RectangleF(area.Left, y, area.Width, 120), format);
        y += 125;
        g.DrawString(item.Folder, small, Brushes.Black, new RectangleF(area.Left, y, area.Width, 60), format);
        y += 40;
        g.DrawString($"Printed {DateTime.Now:dd MMM yyyy, HH:mm}", small, Brushes.Black, new RectangleF(area.Left, y, area.Width, 40), format);
    }
}
