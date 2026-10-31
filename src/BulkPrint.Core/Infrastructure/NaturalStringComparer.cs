namespace BulkPrint.Core.Infrastructure;

/// <summary>
/// Compares strings the way Windows Explorer does: "Invoice 2" comes before "Invoice 10".
/// Case-insensitive.
/// </summary>
public sealed class NaturalStringComparer : IComparer<string?>
{
    public static readonly NaturalStringComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            char cx = x[i], cy = y[j];
            if (char.IsDigit(cx) && char.IsDigit(cy))
            {
                int si = i, sj = j;
                while (si < x.Length && x[si] == '0') si++;
                while (sj < y.Length && y[sj] == '0') sj++;
                int ei = si, ej = sj;
                while (ei < x.Length && char.IsDigit(x[ei])) ei++;
                while (ej < y.Length && char.IsDigit(y[ej])) ej++;

                int lenX = ei - si, lenY = ej - sj;
                if (lenX != lenY) return lenX.CompareTo(lenY);
                int cmp = string.CompareOrdinal(x, si, y, sj, lenX);
                if (cmp != 0) return cmp;
                // Same number: fewer leading zeros first, then continue.
                int zx = si - i, zy = sj - j;
                if (zx != zy) return zx.CompareTo(zy);
                i = ei;
                j = ej;
            }
            else
            {
                int cmp = char.ToUpperInvariant(cx).CompareTo(char.ToUpperInvariant(cy));
                if (cmp != 0) return cmp;
                i++;
                j++;
            }
        }
        return (x.Length - i).CompareTo(y.Length - j);
    }
}
