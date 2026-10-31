namespace BulkPrint.Core.Models;

public enum SheetKind { Page, Blank, Separator }

/// <summary>One printed page side in the order it is sent to the printer.</summary>
public readonly record struct SheetSide(SheetKind Kind, int PageIndex)
{
    public static SheetSide Page(int index) => new(SheetKind.Page, index);
    public static readonly SheetSide Blank = new(SheetKind.Blank, -1);
    public static readonly SheetSide Separator = new(SheetKind.Separator, -1);
}

/// <summary>
/// Works out the exact sequence of page sides for one document, including copies,
/// collation, duplex padding and separator pages.
/// </summary>
public sealed class PrintPlan
{
    public IReadOnlyList<SheetSide> Sides { get; }

    /// <summary>Copies to request from the printer driver (1 unless the driver handles copies).</summary>
    public int DriverCopies { get; }

    public bool DriverCollate { get; }

    public int DocumentPagesSent => Sides.Count(s => s.Kind == SheetKind.Page) * DriverCopies;

    private PrintPlan(List<SheetSide> sides, int driverCopies, bool driverCollate)
    {
        Sides = sides;
        DriverCopies = driverCopies;
        DriverCollate = driverCollate;
    }

    public static PrintPlan Build(
        IReadOnlyList<int> pages,
        int copies,
        bool collate,
        bool duplex,
        bool driverHandlesCopies,
        bool separatorBefore)
    {
        if (pages.Count == 0) throw new ArgumentException("No pages to print.", nameof(pages));
        copies = Math.Clamp(copies, 1, DocumentPrintOptions.MaxCopies);
        var sides = new List<SheetSide>();

        if (separatorBefore)
        {
            sides.Add(SheetSide.Separator);
            // Keep the document starting on the front of a new sheet.
            if (duplex) sides.Add(SheetSide.Blank);
        }

        if (driverHandlesCopies || copies == 1)
        {
            sides.AddRange(pages.Select(SheetSide.Page));
            return new PrintPlan(sides, driverHandlesCopies ? copies : 1, collate);
        }

        if (collate)
        {
            for (int c = 0; c < copies; c++)
            {
                sides.AddRange(pages.Select(SheetSide.Page));
                // In duplex, an odd page count would put the next copy's first page on the back.
                if (duplex && pages.Count % 2 == 1 && c < copies - 1) sides.Add(SheetSide.Blank);
            }
        }
        else if (!duplex)
        {
            foreach (var p in pages)
                for (int c = 0; c < copies; c++) sides.Add(SheetSide.Page(p));
        }
        else
        {
            // Uncollated duplex: repeat each physical sheet (front + back) for every copy.
            for (int i = 0; i < pages.Count; i += 2)
            {
                var front = SheetSide.Page(pages[i]);
                var back = i + 1 < pages.Count ? SheetSide.Page(pages[i + 1]) : SheetSide.Blank;
                for (int c = 0; c < copies; c++)
                {
                    sides.Add(front);
                    sides.Add(back);
                }
            }
            // Drop a trailing blank back side: it adds nothing.
            if (sides.Count > 0 && sides[^1].Kind == SheetKind.Blank) sides.RemoveAt(sides.Count - 1);
        }

        return new PrintPlan(sides, 1, collate);
    }
}
