using System.Globalization;
using System.Text;

namespace BulkPrint.Core.Services;

/// <summary>
/// Minimal text-only PDF writer (A4, standard PDF fonts) used for exporting print reports.
/// No external library is needed.
/// </summary>
public sealed class SimplePdfWriter
{
    private const double PageWidth = 595.28, PageHeight = 841.89, Margin = 50;
    private readonly List<StringBuilder> _pages = new();
    private StringBuilder _current = null!;
    private double _y;
    private static readonly Encoding Ansi;

    static SimplePdfWriter()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Ansi = Encoding.GetEncoding(1252, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);
    }

    public SimplePdfWriter() => NewPage();

    private void NewPage()
    {
        _current = new StringBuilder();
        _pages.Add(_current);
        _y = PageHeight - Margin;
    }

    public void Heading(string text) => Write(text, "F2", 16, 24);

    public void Line(string text, bool bold = false, bool mono = false, bool small = false)
    {
        string font = mono ? "F3" : bold ? "F2" : "F1";
        double size = small ? 8.5 : mono ? 8.5 : 10.5;
        double maxChars = mono ? (PageWidth - 2 * Margin) / (size * 0.6) : (PageWidth - 2 * Margin) / (size * 0.5);
        foreach (var part in TextPaginator.Wrap(text, Math.Max(20, (int)maxChars)))
            Write(part, font, size, size * 1.45);
    }

    public void Blank() => _y -= 8;

    private void Write(string text, string font, double size, double leading)
    {
        if (_y - leading < Margin) NewPage();
        _y -= leading;
        _current.Append("BT /").Append(font).Append(' ').Append(Num(size)).Append(" Tf ")
            .Append(Num(Margin)).Append(' ').Append(Num(_y)).Append(" Td (")
            .Append(Escape(text)).Append(") Tj ET\n");
    }

    private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Escape(string text)
    {
        var bytes = Ansi.GetBytes(text);
        var sb = new StringBuilder(bytes.Length + 8);
        foreach (var b in bytes)
        {
            char c = (char)b;
            if (c is '(' or ')' or '\\') sb.Append('\\').Append(c);
            else if (b < 32) sb.Append(' ');
            else if (b > 126) sb.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
            else sb.Append(c);
        }
        return sb.ToString();
    }

    public byte[] ToBytes()
    {
        var objects = new List<string>();
        // 1: catalog, 2: pages, 3-5: fonts, then page/content pairs.
        int pageCount = _pages.Count;
        int firstPageObj = 6;
        var kids = string.Join(" ", Enumerable.Range(0, pageCount).Select(i => $"{firstPageObj + i * 2} 0 R"));
        objects.Add("<< /Type /Catalog /Pages 2 0 R >>");
        objects.Add($"<< /Type /Pages /Kids [{kids}] /Count {pageCount} >>");
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>");

        for (int i = 0; i < pageCount; i++)
        {
            var footer = $"BT /F1 8 Tf {Num(Margin)} 30 Td (Page {i + 1} of {pageCount}) Tj ET\n";
            var content = _pages[i] + footer;
            int contentObj = firstPageObj + i * 2 + 1;
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Num(PageWidth)} {Num(PageHeight)}] " +
                        $"/Resources << /Font << /F1 3 0 R /F2 4 0 R /F3 5 0 R >> >> /Contents {contentObj} 0 R >>");
            objects.Add($"<< /Length {Ansi.GetByteCount(content)} >>\nstream\n{content}endstream");
        }

        using var ms = new MemoryStream();
        void W(string s)
        {
            var b = Ansi.GetBytes(s);
            ms.Write(b, 0, b.Length);
        }

        W("%PDF-1.4\n%âãÏÓ\n");
        var offsets = new List<long>();
        for (int i = 0; i < objects.Count; i++)
        {
            offsets.Add(ms.Position);
            W($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        long xref = ms.Position;
        W($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var off in offsets) W($"{off:D10} 00000 n \n");
        W($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }

    public void Save(string path) => File.WriteAllBytes(path, ToBytes());
}
