using System.Globalization;
using System.Text;
using BulkPrint.Core.Models;

namespace BulkPrint.Core.Services;

public sealed record ReportEntry(string FileName, string Path, PrintStatus Status, int PagesSent, string Message, TimeSpan Duration)
{
    public string StatusText => EnumLabels.Of(Status);
    public string DurationText => Duration.TotalSeconds < 1 ? "<1 s" : $"{Duration.TotalSeconds:0} s";
}

/// <summary>Summary shown after a batch finishes. Can be exported to CSV or PDF.</summary>
public sealed class BatchReport
{
    public string PrinterName { get; init; } = "";
    public DateTime StartTime { get; init; }
    public DateTime EndTime { get; set; }
    public bool WasCancelled { get; set; }
    public List<ReportEntry> Entries { get; } = new();

    public int TotalDocuments => Entries.Count;
    public int Printed => Entries.Count(e => e.Status == PrintStatus.Printed);
    public int Skipped => Entries.Count(e => e.Status is PrintStatus.Skipped or PrintStatus.NeedsPassword or PrintStatus.Unsupported or PrintStatus.Pending);
    public int Failed => Entries.Count(e => e.Status == PrintStatus.Failed);
    public int TotalPages => Entries.Sum(e => e.PagesSent);
    public TimeSpan Duration => EndTime - StartTime;
    public IEnumerable<ReportEntry> FailedEntries => Entries.Where(e => e.Status == PrintStatus.Failed);

    public string DurationText
    {
        get
        {
            var d = Duration;
            if (d.TotalHours >= 1) return $"{(int)d.TotalHours} h {d.Minutes} min";
            if (d.TotalMinutes >= 1) return $"{(int)d.TotalMinutes} min {d.Seconds} s";
            return $"{Math.Max(0, (int)d.TotalSeconds)} s";
        }
    }

    public string Headline => WasCancelled
        ? $"Batch cancelled: {Printed} of {TotalDocuments} documents printed"
        : Failed == 0
            ? $"Batch complete: {Printed} of {TotalDocuments} documents printed"
            : $"Batch finished with {Failed} failed document{(Failed == 1 ? "" : "s")}";

    public IEnumerable<string> SummaryLines()
    {
        yield return $"Printer: {PrinterName}";
        yield return $"Started: {StartTime:dd MMM yyyy, HH:mm:ss}";
        yield return $"Finished: {EndTime:dd MMM yyyy, HH:mm:ss}  (duration {DurationText})";
        yield return $"Documents: {TotalDocuments}  ·  Printed: {Printed}  ·  Skipped: {Skipped}  ·  Failed: {Failed}";
        yield return $"Pages sent to printer: {TotalPages}";
    }

    public string ToCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine("File name,Folder path,Status,Pages sent,Duration (s),Message");
        foreach (var e in Entries)
        {
            sb.Append(Csv(e.FileName)).Append(',')
              .Append(Csv(e.Path)).Append(',')
              .Append(Csv(e.StatusText)).Append(',')
              .Append(e.PagesSent.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(((int)Math.Round(e.Duration.TotalSeconds)).ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(Csv(e.Message)).AppendLine();
        }
        sb.AppendLine();
        foreach (var line in SummaryLines()) sb.AppendLine(Csv(line));
        sb.AppendLine(Csv($"Report created by {AppInfo.Name} {AppInfo.Version}"));
        return sb.ToString();
    }

    private static string Csv(string value)
    {
        value ??= "";
        // Prevent spreadsheet formula injection from file names.
        if (value.Length > 0 && "=+-@".Contains(value[0])) value = "'" + value;
        if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }

    public void SaveCsv(string path) =>
        File.WriteAllText(path, ToCsv(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

    public void SavePdf(string path)
    {
        var w = new SimplePdfWriter();
        w.Heading($"{AppInfo.Name} — Print Report");
        w.Line(Headline, bold: true);
        w.Blank();
        foreach (var line in SummaryLines()) w.Line(line);
        w.Blank();

        var failed = FailedEntries.ToList();
        if (failed.Count > 0)
        {
            w.Line("Failed documents", bold: true);
            foreach (var e in failed)
            {
                w.Line($"  {e.FileName}");
                w.Line($"      {e.Message}", small: true);
            }
            w.Blank();
        }

        w.Line("All documents", bold: true);
        int n = 1;
        foreach (var e in Entries)
        {
            w.Line($"{n++,4}. {Truncate(e.FileName, 60),-60}  {e.StatusText,-15} {e.PagesSent,5} pages", mono: true);
        }
        w.Blank();
        w.Line($"Report created by {AppInfo.Name} {AppInfo.Version}", small: true);
        w.Save(path);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
