using System.Text;

namespace BulkPrint.Core.Services;

/// <summary>Reads text files with sensible encoding detection.</summary>
public static class TextLoader
{
    static TextLoader()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static string ReadAllText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return Decode(bytes);
    }

    public static string Decode(byte[] bytes)
    {
        // BOM detection.
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // Older Windows text files (Notepad "ANSI").
            return Encoding.GetEncoding(1252).GetString(bytes);
        }
    }

    /// <summary>Splits text into lines, expands tabs and removes control characters.</summary>
    public static List<string> ToLines(string text, int tabSize = 4)
    {
        var lines = new List<string>();
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        foreach (var raw in normalized.Split('\n'))
        {
            var sb = new StringBuilder(raw.Length);
            foreach (var ch in raw)
            {
                if (ch == '\t')
                {
                    int spaces = tabSize - (sb.Length % tabSize);
                    sb.Append(' ', spaces);
                }
                else if (ch == '\f')
                {
                    // Form feed: handled by the paginator as a page break marker.
                    sb.Append('\f');
                }
                else if (!char.IsControl(ch))
                {
                    sb.Append(ch);
                }
            }
            lines.Add(sb.ToString());
        }
        // Drop a single trailing empty line created by a final newline.
        if (lines.Count > 1 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return lines;
    }
}

/// <summary>Turns CSV text into neatly aligned text columns for printing.</summary>
public static class CsvFormatter
{
    public static List<List<string>> Parse(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;
        char delimiter = DetectDelimiter(text);

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
            }
            else if (c == '"' && field.Length == 0) inQuotes = true;
            else if (c == delimiter) { row.Add(field.ToString()); field.Clear(); }
            else if (c == '\r') { }
            else if (c == '\n')
            {
                row.Add(field.ToString());
                field.Clear();
                rows.Add(row);
                row = new List<string>();
            }
            else field.Append(c);
        }
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }
        return rows;
    }

    /// <summary>Comma, semicolon (common in Europe/India Excel exports) or tab.</summary>
    public static char DetectDelimiter(string text)
    {
        int end = text.IndexOf('\n');
        var firstLine = end < 0 ? text : text[..end];
        int commas = firstLine.Count(c => c == ',');
        int semis = firstLine.Count(c => c == ';');
        int tabs = firstLine.Count(c => c == '\t');
        if (tabs > commas && tabs > semis) return '\t';
        if (semis > commas) return ';';
        return ',';
    }

    public static List<string> ToAlignedLines(string csvText, int maxColumnWidth = 40)
    {
        var rows = Parse(csvText);
        if (rows.Count == 0) return new List<string>();
        int columns = rows.Max(r => r.Count);
        var widths = new int[columns];
        foreach (var r in rows)
            for (int c = 0; c < r.Count; c++)
                widths[c] = Math.Min(maxColumnWidth, Math.Max(widths[c], Clean(r[c]).Length));

        var lines = new List<string>(rows.Count + 1);
        for (int ri = 0; ri < rows.Count; ri++)
        {
            var r = rows[ri];
            var sb = new StringBuilder();
            for (int c = 0; c < columns; c++)
            {
                var value = c < r.Count ? Clean(r[c]) : "";
                if (value.Length > widths[c]) value = value[..Math.Max(0, widths[c] - 1)] + "…";
                sb.Append(value.PadRight(widths[c]));
                if (c < columns - 1) sb.Append("  ");
            }
            lines.Add(sb.ToString().TrimEnd());
            if (ri == 0 && rows.Count > 1)
                lines.Add(string.Join("  ", widths.Select(w => new string('-', Math.Max(1, w)))));
        }
        return lines;
    }

    private static string Clean(string s) => s.Replace("\r", " ").Replace("\n", " ").Replace('\t', ' ').Trim();
}

/// <summary>Splits lines into pages: wraps long lines and honours form-feed page breaks.</summary>
public static class TextPaginator
{
    public static List<List<string>> Paginate(IEnumerable<string> lines, int charsPerLine, int linesPerPage)
    {
        charsPerLine = Math.Max(10, charsPerLine);
        linesPerPage = Math.Max(5, linesPerPage);
        var pages = new List<List<string>>();
        var current = new List<string>();

        void NewPage()
        {
            pages.Add(current);
            current = new List<string>();
        }

        foreach (var line in lines)
        {
            var segments = line.Split('\f');
            for (int s = 0; s < segments.Length; s++)
            {
                if (s > 0) NewPage();
                foreach (var wrapped in Wrap(segments[s], charsPerLine))
                {
                    if (current.Count >= linesPerPage) NewPage();
                    current.Add(wrapped);
                }
            }
        }
        if (current.Count > 0 || pages.Count == 0) pages.Add(current);
        return pages;
    }

    public static IEnumerable<string> Wrap(string line, int width)
    {
        if (line.Length <= width)
        {
            yield return line;
            yield break;
        }
        int pos = 0;
        while (pos < line.Length)
        {
            int remaining = line.Length - pos;
            if (remaining <= width)
            {
                yield return line[pos..];
                yield break;
            }
            int breakAt = line.LastIndexOf(' ', pos + width - 1, width);
            if (breakAt <= pos) breakAt = pos + width; // no space: hard break
            yield return line[pos..breakAt].TrimEnd();
            pos = breakAt;
            while (pos < line.Length && line[pos] == ' ') pos++;
        }
    }
}
