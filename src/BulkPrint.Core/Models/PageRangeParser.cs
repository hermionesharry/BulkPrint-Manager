namespace BulkPrint.Core.Models;

/// <summary>
/// Parses page ranges such as "1-3, 5, 8-" (page 8 to the end) or "-4" (pages 1 to 4).
/// Page numbers are 1-based for the user; results are 0-based page indexes.
/// </summary>
public static class PageRangeParser
{
    public readonly record struct Range(int Start, int? End);

    public static bool TryParse(string? text, out List<Range> ranges, out string error)
    {
        ranges = new List<Range>();
        error = "";
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Enter the pages to print, for example 1-3, 5, 8-";
            return false;
        }

        var parts = text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            error = "Enter the pages to print, for example 1-3, 5, 8-";
            return false;
        }

        foreach (var raw in parts)
        {
            var part = raw.Replace('–', '-').Replace('—', '-').Replace(" ", "");
            int dash = part.IndexOf('-');
            if (dash < 0)
            {
                if (!TryPage(part, out int p))
                {
                    error = $"\"{raw}\" is not a valid page number.";
                    return false;
                }
                ranges.Add(new Range(p, p));
                continue;
            }

            if (part.IndexOf('-', dash + 1) >= 0)
            {
                error = $"\"{raw}\" is not a valid page range.";
                return false;
            }

            string left = part[..dash], right = part[(dash + 1)..];
            int start = 1;
            int? end = null;
            if (left.Length > 0 && !TryPage(left, out start))
            {
                error = $"\"{raw}\" is not a valid page range.";
                return false;
            }
            if (right.Length > 0)
            {
                if (!TryPage(right, out int e))
                {
                    error = $"\"{raw}\" is not a valid page range.";
                    return false;
                }
                end = e;
            }
            if (left.Length == 0 && right.Length == 0)
            {
                error = $"\"{raw}\" is not a valid page range.";
                return false;
            }
            if (end is int endValue && endValue < start)
            {
                error = $"In \"{raw}\" the first page is after the last page.";
                return false;
            }
            ranges.Add(new Range(start, end));
        }
        return true;
    }

    private static bool TryPage(string s, out int page)
    {
        if (int.TryParse(s, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out page) && page >= 1)
            return true;
        page = 0;
        return false;
    }

    /// <summary>
    /// Returns the 0-based page indexes to print, in order, without duplicates.
    /// Pages beyond the document's length are ignored.
    /// </summary>
    public static IReadOnlyList<int> SelectPages(int pageCount, PageSelectionMode mode, string? rangeText)
    {
        var result = new List<int>();
        if (pageCount <= 0) return result;

        switch (mode)
        {
            case PageSelectionMode.All:
                for (int i = 0; i < pageCount; i++) result.Add(i);
                break;
            case PageSelectionMode.Odd:
                for (int i = 0; i < pageCount; i += 2) result.Add(i);
                break;
            case PageSelectionMode.Even:
                for (int i = 1; i < pageCount; i += 2) result.Add(i);
                break;
            case PageSelectionMode.Range:
                if (!TryParse(rangeText, out var ranges, out var error))
                    throw new FormatException(error);
                var seen = new HashSet<int>();
                foreach (var r in ranges)
                {
                    int last = Math.Min(r.End ?? pageCount, pageCount);
                    for (int p = r.Start; p <= last; p++)
                        if (seen.Add(p - 1)) result.Add(p - 1);
                }
                break;
        }
        return result;
    }
}
