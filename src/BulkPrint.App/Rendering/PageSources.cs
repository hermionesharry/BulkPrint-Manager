using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using BulkPrint.Core.Models;
using BulkPrint.Core.Services;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace BulkPrint.App.Rendering;

/// <summary>A document opened for drawing page by page onto a printer or preview bitmap.</summary>
internal interface IPageSource : IDisposable
{
    int PageCount { get; }
    SizeF GetPageSizeInches(int pageIndex);
    void Draw(Graphics g, int pageIndex, SheetLayout layout, ScalingMode scaling, bool monochrome, float renderDpi);
}

/// <summary>PDF pages rendered by Windows.Data.Pdf, the PDF engine built into Windows 10 and 11.</summary>
internal sealed class PdfPageSource : IPageSource
{
    private readonly PdfDocument _doc;
    private readonly SizeF[] _sizes;

    private PdfPageSource(PdfDocument doc)
    {
        _doc = doc;
        _sizes = new SizeF[(int)doc.PageCount];
    }

    public static PdfPageSource Open(string path, string? password)
    {
        var doc = PdfLoader.Load(path, password);
        return new PdfPageSource(doc);
    }

    public int PageCount => _sizes.Length;

    public SizeF GetPageSizeInches(int pageIndex)
    {
        if (_sizes[pageIndex].IsEmpty)
        {
            using var page = _doc.GetPage((uint)pageIndex);
            var s = page.Size; // device-independent pixels (1/96 inch), rotation applied
            _sizes[pageIndex] = new SizeF((float)(s.Width / 96.0), (float)(s.Height / 96.0));
        }
        return _sizes[pageIndex];
    }

    public void Draw(Graphics g, int pageIndex, SheetLayout layout, ScalingMode scaling, bool monochrome, float renderDpi)
    {
        var target = PageDrawing.ComputeTarget(GetPageSizeInches(pageIndex), layout, scaling);
        var px = PageDrawing.PixelSize(target, renderDpi);
        using var bitmap = RenderPage(pageIndex, px.Width, px.Height);
        PageDrawing.DrawImage(g, bitmap, target, monochrome);
    }

    public Bitmap RenderPage(int pageIndex, int width, int height)
    {
        using var page = _doc.GetPage((uint)pageIndex);
        using var stream = new InMemoryRandomAccessStream();
        var options = new PdfPageRenderOptions
        {
            DestinationWidth = (uint)width,
            DestinationHeight = (uint)height,
            BitmapEncoderId = BitmapEncoder.BmpEncoderId,
        };
        page.RenderToStreamAsync(stream, options).AsTask().GetAwaiter().GetResult();
        stream.Seek(0);
        using var netStream = stream.AsStreamForRead();
        using var memory = new MemoryStream();
        netStream.CopyTo(memory);
        memory.Position = 0;
        using var decoded = Image.FromStream(memory);
        return new Bitmap(decoded);
    }

    public void Dispose()
    {
        // PdfDocument holds no unmanaged handle that needs closing; pages are disposed after each use.
    }
}

internal static class PdfLoader
{
    private const int WrongPasswordHResult = unchecked((int)0x8007052B);

    public static PdfDocument Load(string path, string? password) =>
        LoadAsync(path, password).GetAwaiter().GetResult();

    public static async Task<PdfDocument> LoadAsync(string path, string? password)
    {
        var file = await StorageFile.GetFileFromPathAsync(path).AsTask().ConfigureAwait(false);
        return string.IsNullOrEmpty(password)
            ? await PdfDocument.LoadFromFileAsync(file).AsTask().ConfigureAwait(false)
            : await PdfDocument.LoadFromFileAsync(file, password).AsTask().ConfigureAwait(false);
    }

    public static bool IsWrongPassword(Exception ex) =>
        ex.HResult == WrongPasswordHResult ||
        ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase);
}

/// <summary>JPG, PNG, BMP and (multi-page) TIFF images via GDI+.</summary>
internal sealed class ImagePageSource : IPageSource
{
    private readonly FileStream _stream;
    private readonly Image _image;
    private readonly FrameDimension? _dimension;
    private int _currentFrame;

    public ImagePageSource(string path)
    {
        _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        try
        {
            _image = Image.FromStream(_stream, useEmbeddedColorManagement: true, validateImageData: true);
        }
        catch
        {
            _stream.Dispose();
            throw new InvalidDataException("This image could not be opened. It may be damaged or in an unsupported format.");
        }
        var dims = _image.FrameDimensionsList;
        _dimension = dims.Length > 0 ? new FrameDimension(dims[0]) : null;
        PageCount = _dimension is null ? 1 : Math.Max(1, _image.GetFrameCount(_dimension));
        if (PageCount == 1) ApplyExifOrientation(_image);
    }

    public int PageCount { get; }

    private void SelectFrame(int index)
    {
        if (_dimension is null || PageCount == 1 || index == _currentFrame) return;
        _image.SelectActiveFrame(_dimension, index);
        _currentFrame = index;
    }

    public SizeF GetPageSizeInches(int pageIndex)
    {
        SelectFrame(pageIndex);
        float dx = _image.HorizontalResolution, dy = _image.VerticalResolution;
        if (dx < 30 || dx > 2400) dx = 96;
        if (dy < 30 || dy > 2400) dy = dx;
        return new SizeF(_image.Width / dx, _image.Height / dy);
    }

    public void Draw(Graphics g, int pageIndex, SheetLayout layout, ScalingMode scaling, bool monochrome, float renderDpi)
    {
        SelectFrame(pageIndex);
        var target = PageDrawing.ComputeTarget(GetPageSizeInches(pageIndex), layout, scaling);
        PageDrawing.DrawImage(g, _image, target, monochrome);
    }

    /// <summary>Phone photos store rotation in EXIF; apply it so pictures print upright.</summary>
    private static void ApplyExifOrientation(Image image)
    {
        const int OrientationId = 0x0112;
        try
        {
            if (Array.IndexOf(image.PropertyIdList, OrientationId) < 0) return;
            var item = image.GetPropertyItem(OrientationId);
            if (item?.Value is null || item.Value.Length < 2) return;
            int orientation = BitConverter.ToUInt16(item.Value, 0);
            RotateFlipType? flip = orientation switch
            {
                2 => RotateFlipType.RotateNoneFlipX,
                3 => RotateFlipType.Rotate180FlipNone,
                4 => RotateFlipType.Rotate180FlipX,
                5 => RotateFlipType.Rotate90FlipX,
                6 => RotateFlipType.Rotate90FlipNone,
                7 => RotateFlipType.Rotate270FlipX,
                8 => RotateFlipType.Rotate270FlipNone,
                _ => null,
            };
            if (flip is RotateFlipType f)
            {
                image.RotateFlip(f);
                image.RemovePropertyItem(OrientationId);
            }
        }
        catch
        {
            // Orientation is a nicety; never fail printing because of it.
        }
    }

    public void Dispose()
    {
        _image.Dispose();
        _stream.Dispose();
    }
}

/// <summary>TXT and CSV files drawn as text in a fixed-width font.</summary>
internal sealed class TextPageSource : IPageSource
{
    public const float FontPoints = 9.5f;
    private const float CharWidthEm = 0.55f;
    private const float LineHeightEm = 1.18f;
    private readonly List<List<string>> _pages;
    private readonly SizeF _paperInches;

    public TextPageSource(IReadOnlyList<string> lines, SheetLayout paginationLayout)
    {
        var area = paginationLayout.TextArea;
        int charsPerLine = (int)Math.Floor(area.Width / 100f * 72f / (FontPoints * CharWidthEm));
        int linesPerPage = (int)Math.Floor(area.Height / 100f * 72f / (FontPoints * LineHeightEm));
        _pages = TextPaginator.Paginate(lines, charsPerLine, linesPerPage);
        _paperInches = new SizeF(paginationLayout.Paper.Width / 100f, paginationLayout.Paper.Height / 100f);
    }

    public static List<string> ReadLines(DocumentItem item)
    {
        var text = TextLoader.ReadAllText(item.Path);
        return item.Kind == DocumentKind.Csv ? CsvFormatter.ToAlignedLines(text) : TextLoader.ToLines(text);
    }

    public int PageCount => _pages.Count;

    public SizeF GetPageSizeInches(int pageIndex) => _paperInches;

    public void Draw(Graphics g, int pageIndex, SheetLayout layout, ScalingMode scaling, bool monochrome, float renderDpi)
    {
        var area = layout.TextArea;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var font = new Font("Consolas", FontPoints, FontStyle.Regular, GraphicsUnit.Point);
        float lineHeight = FontPoints * LineHeightEm / 72f * 100f;
        float y = area.Top;
        using var format = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces };
        foreach (var line in _pages[pageIndex])
        {
            if (line.Length > 0) g.DrawString(line, font, Brushes.Black, area.Left, y, format);
            y += lineHeight;
        }
    }

    public void Dispose() { }
}
