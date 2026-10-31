using System.Text;
using BulkPrint.Core;
using BulkPrint.Core.Infrastructure;
using BulkPrint.Core.Models;
using BulkPrint.Core.Services;

int passed = 0, failed = 0;
var failures = new List<string>();

void Check(bool condition, string name)
{
    if (condition) passed++;
    else
    {
        failed++;
        failures.Add(name);
        Console.WriteLine("  FAIL: " + name);
    }
}

void Equal<T>(T expected, T actual, string name) =>
    Check(EqualityComparer<T>.Default.Equals(expected, actual), $"{name} (expected {expected}, got {actual})");

void Seq(IEnumerable<int> expected, IEnumerable<int> actual, string name) =>
    Check(expected.SequenceEqual(actual), $"{name} (expected [{string.Join(",", expected)}], got [{string.Join(",", actual)}])");

async Task Test(string name, Func<Task> body)
{
    Console.WriteLine("- " + name);
    try { await body(); }
    catch (Exception ex)
    {
        failed++;
        failures.Add($"{name}: exception {ex.GetType().Name}: {ex.Message}");
        Console.WriteLine("  EXCEPTION: " + ex);
    }
}

Task Sync(Action a) { a(); return Task.CompletedTask; }

// ---------------------------------------------------------------------------
await Test("App identity and developer credit", () => Sync(() =>
{
    Equal("Designed and Developed by Harish Vaishnav", AppInfo.DeveloperCredit, "credit text");
    Equal("BulkPrint Manager", AppInfo.Name, "name");
    Check(AppInfo.Copyright.Contains("Harish Vaishnav"), "copyright names developer");
    Equal("1.0.0", AppInfo.Version, "version");
}));

await Test("Page range parsing", () => Sync(() =>
{
    Check(PageRangeParser.TryParse("1-3, 5, 8-", out var r, out _), "valid range parses");
    Equal(3, r.Count, "range count");
    Check(!PageRangeParser.TryParse("", out _, out _), "empty invalid");
    Check(!PageRangeParser.TryParse("0", out _, out _), "zero invalid");
    Check(!PageRangeParser.TryParse("5-2", out _, out var e1) && e1.Contains("after"), "reversed invalid");
    Check(!PageRangeParser.TryParse("abc", out _, out _), "letters invalid");
    Check(!PageRangeParser.TryParse("1-2-3", out _, out _), "double dash invalid");
    Check(!PageRangeParser.TryParse("-", out _, out _), "lone dash invalid");
    Check(PageRangeParser.TryParse("2–4", out _, out _), "en dash accepted");

    Seq(new[] { 0, 1, 2, 4, 7, 8, 9 }, PageRangeParser.SelectPages(10, PageSelectionMode.Range, "1-3, 5, 8-"), "select range");
    Seq(new[] { 0, 1, 2, 3 }, PageRangeParser.SelectPages(10, PageSelectionMode.Range, "-4"), "open start");
    Seq(new[] { 4, 0 }, PageRangeParser.SelectPages(10, PageSelectionMode.Range, "5,1,5"), "order kept, duplicates removed");
    Seq(new[] { 2 }, PageRangeParser.SelectPages(3, PageSelectionMode.Range, "3-20"), "range clipped to page count");
    Seq(Array.Empty<int>(), PageRangeParser.SelectPages(3, PageSelectionMode.Range, "7-9"), "range beyond document");
    Seq(new[] { 0, 2, 4 }, PageRangeParser.SelectPages(5, PageSelectionMode.Odd, null), "odd pages");
    Seq(new[] { 1, 3 }, PageRangeParser.SelectPages(5, PageSelectionMode.Even, null), "even pages");
    Seq(new[] { 0, 1, 2 }, PageRangeParser.SelectPages(3, PageSelectionMode.All, null), "all pages");
    Seq(Array.Empty<int>(), PageRangeParser.SelectPages(1, PageSelectionMode.Even, null), "even of single page");
}));

await Test("Print plan: copies, collation, duplex, separators", () => Sync(() =>
{
    IEnumerable<int> Ix(PrintPlan p) => p.Sides.Select(s => s.Kind == SheetKind.Page ? s.PageIndex : s.Kind == SheetKind.Blank ? -1 : -2);

    var pages = new[] { 0, 1, 2 };
    Seq(new[] { 0, 1, 2 }, Ix(PrintPlan.Build(pages, 1, true, false, false, false)), "single copy");
    Seq(new[] { 0, 1, 2, 0, 1, 2 }, Ix(PrintPlan.Build(pages, 2, true, false, false, false)), "collated");
    Seq(new[] { 0, 0, 1, 1, 2, 2 }, Ix(PrintPlan.Build(pages, 2, false, false, false, false)), "uncollated");
    Seq(new[] { 0, 1, 2, -1, 0, 1, 2 }, Ix(PrintPlan.Build(pages, 2, true, true, false, false)), "collated duplex pads odd copies");
    Seq(new[] { 0, 1, 0, 1, 2, -1, 2 }, Ix(PrintPlan.Build(pages, 2, false, true, false, false)), "uncollated duplex repeats sheets");
    var driver = PrintPlan.Build(pages, 5, true, false, true, false);
    Seq(new[] { 0, 1, 2 }, Ix(driver), "driver copies sends once");
    Equal(5, driver.DriverCopies, "driver copy count");
    Equal(15, driver.DocumentPagesSent, "driver pages sent");
    Seq(new[] { -2, 0, 1, 2 }, Ix(PrintPlan.Build(pages, 1, true, false, false, true)), "separator simplex");
    Seq(new[] { -2, -1, 0, 1, 2 }, Ix(PrintPlan.Build(pages, 1, true, true, false, true)), "separator duplex adds blank back");
    bool threw = false;
    try { PrintPlan.Build(Array.Empty<int>(), 1, true, false, false, false); } catch (ArgumentException) { threw = true; }
    Check(threw, "empty pages rejected");
}));

await Test("Natural sorting", () => Sync(() =>
{
    var names = new[] { "Invoice 10.pdf", "invoice 2.pdf", "Invoice 1.pdf", "Invoice 01.pdf", "a.pdf" };
    var sorted = names.OrderBy(n => n, NaturalStringComparer.Instance).ToArray();
    Check(sorted.SequenceEqual(new[] { "a.pdf", "Invoice 1.pdf", "Invoice 01.pdf", "invoice 2.pdf", "Invoice 10.pdf" }),
        "natural order: " + string.Join(" | ", sorted));
}));

await Test("File types", () => Sync(() =>
{
    Equal(DocumentKind.Pdf, FileTypes.GetKind(".PDF"), "pdf upper case");
    Equal(DocumentKind.Word, FileTypes.GetKind(".rtf"), "rtf is word");
    Equal(DocumentKind.Csv, FileTypes.GetKind(".csv"), "csv");
    Equal(DocumentKind.Image, FileTypes.GetKind(".tif"), "tif");
    Equal(DocumentKind.Unsupported, FileTypes.GetKind(".zip"), "zip unsupported");
    Check(FileTypes.NeedsOfficeEngine(DocumentKind.PowerPoint), "ppt needs engine");
    Check(!FileTypes.NeedsOfficeEngine(DocumentKind.Csv), "csv printed natively");
    Equal(16, FileTypes.SupportedExtensions.Count, "16 extensions");
    Equal("1.5 MB", FileTypes.FormatSize(1572864), "size format");
}));

await Test("Text and CSV formatting", () => Sync(() =>
{
    var lines = TextLoader.ToLines("a\tb\r\nline2\n\nend\n");
    Seq(new[] { 5, 5, 0, 3 }, lines.Select(l => l.Length), "tab expansion & line split");
    var latin = TextLoader.Decode(new byte[] { 0x93, 0x48, 0x69, 0x94 });
    Equal("\u201CHi\u201D", latin, "windows-1252 fallback");
    Equal("héllo", TextLoader.Decode(Encoding.UTF8.GetBytes("héllo")), "utf8");

    var rows = CsvFormatter.Parse("name,amount\n\"Smith, J\",\"1,200\"\n\"say \"\"hi\"\"\",5\n");
    Equal(3, rows.Count, "csv rows");
    Equal("Smith, J", rows[1][0], "quoted comma");
    Equal("say \"hi\"", rows[2][0], "escaped quote");
    Equal(';', CsvFormatter.DetectDelimiter("a;b;c\n1;2;3"), "semicolon delimiter");
    var aligned = CsvFormatter.ToAlignedLines("name,amount\nAlice,10\nBob,2000\n");
    Equal(4, aligned.Count, "aligned has header rule");
    Check(aligned[1].StartsWith("-----"), "header rule");
    Check(aligned[2].IndexOf("10") == aligned[0].IndexOf("amount"), "columns aligned");

    var wrapped = TextPaginator.Wrap("the quick brown fox jumps over the lazy dog", 12).ToList();
    Check(wrapped.All(w => w.Length <= 12), "wrap width");
    Equal("the quick brown fox jumps over the lazy dog", string.Join(" ", wrapped), "wrap keeps words");
    var pages = TextPaginator.Paginate(Enumerable.Range(1, 25).Select(i => $"line {i}"), 80, 10);
    Equal(3, pages.Count, "25 lines / 10 per page");
    var ff = TextPaginator.Paginate(new[] { "a", "b\fc" }, 80, 10);
    Equal(2, ff.Count, "form feed page break");
    Equal(1, TextPaginator.Paginate(Array.Empty<string>(), 80, 10).Count, "empty text still one page");
}));

await Test("Queue operations", () => Sync(() =>
{
    var tmp = Directory.CreateTempSubdirectory("bpm-test-");
    try
    {
        Directory.CreateDirectory(Path.Combine(tmp.FullName, "sub"));
        File.WriteAllText(Path.Combine(tmp.FullName, "doc 10.pdf"), "x");
        File.WriteAllText(Path.Combine(tmp.FullName, "doc 2.pdf"), "xx");
        File.WriteAllText(Path.Combine(tmp.FullName, "notes.txt"), "xxx");
        File.WriteAllText(Path.Combine(tmp.FullName, "archive.zip"), "z");
        File.WriteAllText(Path.Combine(tmp.FullName, "~$lock.docx"), "z");
        File.WriteAllText(Path.Combine(tmp.FullName, "sub", "inner.png"), "p");

        var withSub = QueueOperations.Expand(new[] { tmp.FullName }, includeSubfolders: true);
        Equal(4, withSub.Items.Count, "recursive import count");
        Equal(1, withSub.UnsupportedCount, "zip reported unsupported");
        Equal("doc 2.pdf", withSub.Items[0].FileName, "natural folder order");
        var noSub = QueueOperations.Expand(new[] { tmp.FullName }, includeSubfolders: false);
        Equal(3, noSub.Items.Count, "non-recursive import count");
        var missing = QueueOperations.Expand(new[] { Path.Combine(tmp.FullName, "missing.pdf") }, false);
        Equal(1, missing.UnreadableCount, "missing path counted");

        var items = withSub.Items.Concat(new[] { new DocumentItem(withSub.Items[0].Path) }).ToList();
        Equal(1, QueueOperations.FindDuplicates(items).Count, "duplicate found");

        var byName = QueueOperations.Sort(withSub.Items, SortMode.NameAscending).Select(i => i.FileName).ToList();
        Check(byName.SequenceEqual(new[] { "doc 2.pdf", "doc 10.pdf", "inner.png", "notes.txt" }), "sort by name: " + string.Join(",", byName));
        var bySize = QueueOperations.Sort(withSub.Items, SortMode.SizeSmallestFirst).Select(i => i.SizeBytes).ToList();
        Check(bySize.SequenceEqual(bySize.OrderBy(x => x)), "sort by size");

        var a = new DocumentItem("/tmp/a.pdf"); var b = new DocumentItem("/tmp/b.pdf");
        var c = new DocumentItem("/tmp/c.pdf"); var d = new DocumentItem("/tmp/d.pdf");
        var all = new List<DocumentItem> { a, b, c, d };
        Check(QueueOperations.MoveTo(all, new[] { a }, 3).SequenceEqual(new[] { b, c, a, d }), "move down by drag");
        Check(QueueOperations.MoveTo(all, new[] { d }, 0).SequenceEqual(new[] { d, a, b, c }), "move to top");
        Check(QueueOperations.MoveTo(all, new[] { b, d }, 4).SequenceEqual(new[] { a, c, b, d }), "move several to end");
        Check(QueueOperations.Shift(all, new[] { c }, -1).SequenceEqual(new[] { a, c, b, d }), "shift up");
        Check(QueueOperations.Shift(all, new[] { a, b }, -1).SequenceEqual(all), "shift up at top does nothing");
        Check(QueueOperations.Shift(all, new[] { a }, 1).SequenceEqual(new[] { b, a, c, d }), "shift down");
    }
    finally { tmp.Delete(true); }
}));

await Test("Per-document overrides and copies", () => Sync(() =>
{
    var global = new GlobalPrintSettings { Copies = 2 };
    var item = new DocumentItem("/tmp/x.pdf");
    item.AttachGlobal(global);
    Equal(2, item.Copies, "inherits global copies");
    Check(!item.HasCustomSettings, "no override yet");
    item.Copies = 5;
    Check(item.HasCustomSettings, "editing copies creates override");
    Equal(5, item.EffectiveOptions.Copies, "override copies");
    Equal(2, global.Copies, "global unchanged");
    global.Copies = 3;
    Equal(5, item.Copies, "override wins over global");
    item.Overrides = null;
    Equal(3, item.Copies, "reset follows global");
    global.Copies = 5000;
    Equal(999, global.Copies, "copies clamped");
    var o = new DocumentPrintOptions { PageSelection = PageSelectionMode.Range, PageRange = "x" };
    Check(o.Validate() is not null, "invalid range detected");
    Check(global.SameOptionsAs(global.CloneOptions()), "clone equal");
}));

await Test("Settings, profiles and session round-trip", () => Sync(() =>
{
    var tmp = Directory.CreateTempSubdirectory("bpm-store-");
    try
    {
        var store = new SettingsStore(new StoragePaths(Path.Combine(tmp.FullName, "roam"), Path.Combine(tmp.FullName, "local")));
        var s = store.LoadSettings();
        s.Global.PrinterName = "Office Printer";
        s.Global.Duplex = DuplexMode.LongEdge;
        s.Global.PageSelection = PageSelectionMode.Range;
        s.Global.PageRange = "1-2";
        s.DarkTheme = true;
        store.SaveSettings(s);
        var s2 = store.LoadSettings();
        Equal("Office Printer", s2.Global.PrinterName, "printer saved");
        Equal(DuplexMode.LongEdge, s2.Global.Duplex, "duplex saved");
        Equal("1-2", s2.Global.PageRange, "range saved");
        Check(s2.DarkTheme, "theme saved");
        Check(File.ReadAllText(store.Paths.SettingsFile).Contains("\"LongEdge\""), "enums saved as text");

        store.SaveProfiles(new List<PrintProfile> { new() { Name = "A4 Duplex B&W", Settings = s2.Global } });
        Equal("A4 Duplex B&W", store.LoadProfiles()[0].Name, "profile saved");

        var f = Path.Combine(tmp.FullName, "a.pdf");
        File.WriteAllText(f, "x");
        var item = new DocumentItem(f) { Status = PrintStatus.Sending, PageCount = 4, Password = "secret" };
        item.Overrides = new DocumentPrintOptions { Copies = 3 };
        var printed = new DocumentItem(f) { Status = PrintStatus.Printed };
        var gone = new DocumentItem(Path.Combine(tmp.FullName, "gone.pdf"));
        store.SaveSession(QueueOperations.ToProject(new[] { item, printed, gone }, s2.Global, true));
        var raw = File.ReadAllText(store.Paths.SessionFile);
        Check(!raw.Contains("secret"), "password never saved");
        var session = store.LoadSession()!;
        Check(session.BatchInProgress, "batch flag saved");
        var restored = QueueOperations.FromProject(session, keepStatuses: true);
        Equal(PrintStatus.Pending, restored[0].Status, "interrupted document becomes pending");
        Equal(3, restored[0].Overrides!.Copies, "override restored");
        Equal(4, restored[0].PageCount, "page count restored");
        Equal(PrintStatus.Printed, restored[1].Status, "printed status kept");
        Equal(PrintStatus.Failed, restored[2].Status, "missing file flagged");
        Check(store.LoadSettings() is not null && JsonStore.Load<AppSettings>(Path.Combine(tmp.FullName, "nope.json")) is null, "missing file returns null");
        File.WriteAllText(Path.Combine(tmp.FullName, "bad.json"), "{not json");
        Check(JsonStore.Load<AppSettings>(Path.Combine(tmp.FullName, "bad.json")) is null, "corrupt file returns null");
    }
    finally { tmp.Delete(true); }
}));

await Test("Report export (CSV and PDF)", () => Sync(() =>
{
    var report = new BatchReport { PrinterName = "HP LaserJet", StartTime = new DateTime(2026, 10, 31, 10, 0, 0) };
    report.Entries.Add(new ReportEntry("a.pdf", @"C:\docs\a.pdf", PrintStatus.Printed, 3, "", TimeSpan.FromSeconds(4)));
    report.Entries.Add(new ReportEntry("=evil,\"name\".pdf", @"C:\docs\b.pdf", PrintStatus.Failed, 0, "The file could not be found.", TimeSpan.FromSeconds(1)));
    report.Entries.Add(new ReportEntry("c.pdf", @"C:\docs\c.pdf", PrintStatus.Skipped, 0, "Skipped by user", TimeSpan.Zero));
    report.EndTime = report.StartTime.AddMinutes(2);
    Equal(1, report.Printed, "printed count");
    Equal(1, report.Failed, "failed count");
    Equal(1, report.Skipped, "skipped count");
    Equal(3, report.TotalPages, "page total");
    Equal("2 min 0 s", report.DurationText, "duration");
    var csv = report.ToCsv();
    Check(csv.Contains("\"'=evil,\"\"name\"\".pdf\""), "csv escaping and formula guard");
    Check(!csv.Contains("Harish"), "credit not added to report output");

    var tmp = Path.Combine(Path.GetTempPath(), "bpm-report-test.pdf");
    report.SavePdf(tmp);
    var bytes = File.ReadAllBytes(tmp);
    Check(Encoding.ASCII.GetString(bytes, 0, 8) == "%PDF-1.4", "pdf header");
    Check(Encoding.ASCII.GetString(bytes[^6..]).Contains("%%EOF"), "pdf trailer");
    Console.WriteLine("  report PDF written to " + tmp);

    var big = new SimplePdfWriter();
    for (int i = 0; i < 200; i++) big.Line($"Line {i} — “quotes” (brackets) \\ back‑slash ₹");
    var bigBytes = big.ToBytes();
    Check(Encoding.ASCII.GetString(bigBytes).Contains("/Count 5"), "long report spans 5 pages");
    File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "bpm-report-long.pdf"), bigBytes);
}));

await Test("Logger rotation", () => Sync(() =>
{
    var dir = Directory.CreateTempSubdirectory("bpm-log-");
    try
    {
        var log = new FileLogger(dir.FullName, maxBytes: 2000, keepFiles: 3);
        for (int i = 0; i < 200; i++) log.Info($"message number {i} with some padding text");
        var files = Directory.GetFiles(dir.FullName, "*.log");
        Check(files.Length == 3, $"rotation keeps 3 files (got {files.Length})");
        Check(new FileInfo(log.CurrentFile).Length < 2200, "current log stays small");
    }
    finally { dir.Delete(true); }
}));

// ---- Print engine with fake printer ------------------------------------------------------
await Test("Print engine: normal batch, failures, skip and statuses", async () =>
{
    var docs = Enumerable.Range(1, 5).Select(i => new DocumentItem($"/tmp/doc{i}.pdf") { PageCount = 2 }).ToList();
    var global = new GlobalPrintSettings { PrinterName = "Fake" };
    foreach (var d in docs) d.AttachGlobal(global);
    docs[2].Status = PrintStatus.Printed;     // already printed: must not print again
    docs[3].Status = PrintStatus.NeedsPassword;
    var printer = new FakePrinter { FailOn = "doc2.pdf" };
    var engine = new PrintEngine(printer, new FakePrinterService(), NullLogger.Instance);
    int persisted = 0;
    var report = await engine.RunAsync(docs, global, () => { persisted++; return Task.CompletedTask; }, null, CancellationToken.None);
    Check(printer.Printed.SequenceEqual(new[] { "doc1.pdf", "doc5.pdf" }), "only pending printed: " + string.Join(",", printer.Printed));
    Equal(PrintStatus.Printed, docs[0].Status, "doc1 printed");
    Equal(PrintStatus.Failed, docs[1].Status, "doc2 failed");
    Check(docs[1].StatusDetail.Contains("Paper jam"), "failure reason kept");
    Equal(PrintStatus.NeedsPassword, docs[3].Status, "locked doc untouched");
    Equal(2, report.Printed, "report printed");
    Equal(1, report.Failed, "report failed");
    Equal(4, report.TotalPages, "report pages");
    Equal(3, persisted, "session saved after each document");
    Equal(1, report.Skipped, "locked document listed as not printed");
    Check(report.Entries.Any(e => e.FileName == "doc4.pdf" && e.Message.Contains("password")), "report explains why it was skipped");
    Check(printer.SeparatorFlags.All(f => !f), "no separators by default");
});

await Test("Print engine: separators between documents only", async () =>
{
    var docs = Enumerable.Range(1, 3).Select(i => new DocumentItem($"/tmp/s{i}.pdf")).ToList();
    var global = new GlobalPrintSettings { PrinterName = "Fake", Separator = SeparatorMode.BlankPage };
    var printer = new FakePrinter();
    var engine = new PrintEngine(printer, new FakePrinterService(), NullLogger.Instance);
    await engine.RunAsync(docs, global, () => Task.CompletedTask, null, CancellationToken.None);
    Check(printer.SeparatorFlags.SequenceEqual(new[] { false, true, true }), "separator before 2nd and 3rd");
});

await Test("Print engine: pause, resume and cancel", async () =>
{
    var docs = Enumerable.Range(1, 6).Select(i => new DocumentItem($"/tmp/p{i}.pdf")).ToList();
    var global = new GlobalPrintSettings { PrinterName = "Fake" };
    var printer = new FakePrinter { DelayMs = 60 };
    var engine = new PrintEngine(printer, new FakePrinterService(), NullLogger.Instance);
    using var cts = new CancellationTokenSource();
    var run = engine.RunAsync(docs, global, () => Task.CompletedTask, null, cts.Token);
    await Task.Delay(90);
    engine.Gate.Pause();
    await Task.Delay(250);
    int whilePaused = printer.Printed.Count;
    await Task.Delay(200);
    Equal(whilePaused, printer.Printed.Count, "nothing printed while paused");
    Check(whilePaused is >= 1 and <= 3, $"paused after current document ({whilePaused})");
    engine.Gate.Resume();
    await Task.Delay(100);
    cts.Cancel();
    var report = await run;
    Check(report.WasCancelled, "report marked cancelled");
    Check(printer.Printed.Count < 6, "cancel stopped the batch");
    Check(docs.Count(d => d.Status == PrintStatus.Pending) >= 1, "unprinted documents stay pending");
    Equal(6, report.Entries.Count, "report lists every document");
});

await Test("Print engine: skip current document", async () =>
{
    var docs = Enumerable.Range(1, 3).Select(i => new DocumentItem($"/tmp/k{i}.pdf")).ToList();
    var global = new GlobalPrintSettings { PrinterName = "Fake" };
    var printer = new FakePrinter { DelayMs = 300, Cancellable = true };
    var engine = new PrintEngine(printer, new FakePrinterService(), NullLogger.Instance);
    var run = engine.RunAsync(docs, global, () => Task.CompletedTask, null, CancellationToken.None);
    await Task.Delay(100);
    engine.SkipCurrent();
    docs[2].Status = PrintStatus.Skipped; // user skips a waiting one
    var report = await run;
    Equal(PrintStatus.Skipped, docs[0].Status, "current document skipped");
    Equal(PrintStatus.Printed, docs[1].Status, "next document printed");
    Equal(PrintStatus.Skipped, docs[2].Status, "waiting document skipped");
    Equal(2, report.Skipped, "report skipped count");
});

await Test("Print engine: auto-pause on printer problem and queue throttling", async () =>
{
    var docs = Enumerable.Range(1, 2).Select(i => new DocumentItem($"/tmp/q{i}.pdf")).ToList();
    var global = new GlobalPrintSettings { PrinterName = "Fake", MaxJobsInPrinterQueue = 3 };
    var status = new FakePrinterService();
    status.Sequence.Enqueue(new PrinterStatusInfo(false, true, "Out of paper", 0));
    status.Sequence.Enqueue(new PrinterStatusInfo(true, false, "Ready", 5));
    status.Sequence.Enqueue(new PrinterStatusInfo(true, false, "Ready", 1));
    var printer = new FakePrinter();
    var engine = new PrintEngine(printer, status, NullLogger.Instance) { ThrottlePollInterval = TimeSpan.FromMilliseconds(20) };
    string? pausedMessage = null;
    int waits = 0;
    engine.AutoPaused += m => pausedMessage = m;
    engine.Waiting += _ => waits++;
    var run = engine.RunAsync(docs, global, () => Task.CompletedTask, null, CancellationToken.None);
    await Task.Delay(150);
    Check(engine.Gate.IsPaused, "batch paused itself");
    Check(pausedMessage?.Contains("Out of paper") == true, "problem reported to user");
    Equal(0, printer.Printed.Count, "nothing printed while printer has a problem");
    engine.Gate.Resume();
    await run;
    Equal(2, printer.Printed.Count, "printed after resume");
    Check(waits >= 1, "waited for full printer queue");
});

await Test("Print engine: progress and estimate", async () =>
{
    var docs = Enumerable.Range(1, 4).Select(i => new DocumentItem($"/tmp/e{i}.pdf") { PageCount = 3 }).ToList();
    var global = new GlobalPrintSettings { PrinterName = "Fake", Copies = 2 };
    foreach (var d in docs) d.AttachGlobal(global);
    docs[0].Overrides = new DocumentPrintOptions { PageSelection = PageSelectionMode.Odd, Copies = 1 };
    var updates = new List<BatchProgress>();
    var engine = new PrintEngine(new FakePrinter { PagesPerDoc = 3 }, new FakePrinterService(), NullLogger.Instance);
    await engine.RunAsync(docs, global, () => Task.CompletedTask, new SyncProgress(updates), CancellationToken.None);
    Equal(2 + 3 * 3 * 2, updates[0].EstimatedTotalPages, "estimate uses selection and copies");
    Equal(4, updates[^1].Completed, "final progress complete");
    Equal(12, updates[^1].PagesSent, "pages counted");
});

Console.WriteLine();
Console.WriteLine($"Passed: {passed}   Failed: {failed}");
if (failed > 0)
{
    Console.WriteLine("Failures:");
    foreach (var f in failures) Console.WriteLine("  " + f);
}
return failed == 0 ? 0 : 1;

// ---------------------------------------------------------------------------
sealed class SyncProgress(List<BatchProgress> list) : IProgress<BatchProgress>
{
    public void Report(BatchProgress value) { lock (list) list.Add(value); }
}

sealed class FakePrinter : IDocumentPrinter
{
    public List<string> Printed { get; } = new();
    public List<bool> SeparatorFlags { get; } = new();
    public string? FailOn { get; set; }
    public int DelayMs { get; set; }
    public bool Cancellable { get; set; }
    public int PagesPerDoc { get; set; } = 2;

    public PrintOutcome Print(DocumentItem item, DocumentPrintOptions options, GlobalPrintSettings global, bool separatorBefore,
        IPrintProgressSink progress, CancellationToken ct)
    {
        progress.SetStatus(PrintStatus.Rendering);
        if (DelayMs > 0)
        {
            if (Cancellable) Task.Delay(DelayMs, ct).GetAwaiter().GetResult();
            else Thread.Sleep(DelayMs);
        }
        ct.ThrowIfCancellationRequested();
        if (item.FileName == FailOn) throw new InvalidOperationException("Paper jam reported by driver");
        progress.SetStatus(PrintStatus.Sending);
        for (int i = 0; i < PagesPerDoc; i++) progress.PageSent();
        lock (Printed)
        {
            Printed.Add(item.FileName);
            SeparatorFlags.Add(separatorBefore);
        }
        return new PrintOutcome(PagesPerDoc);
    }
}

sealed class FakePrinterService : IPrinterService
{
    public Queue<PrinterStatusInfo> Sequence { get; } = new();
    public IReadOnlyList<string> GetPrinterNames() => new[] { "Fake" };
    public string? GetDefaultPrinterName() => "Fake";
    public PrinterCapabilities GetCapabilities(string printerName) =>
        new(new[] { "A4" }, new[] { "Auto" }, true, true, 99, true, "A4", "Auto");
    public PrinterStatusInfo GetStatus(string printerName)
    {
        lock (Sequence) return Sequence.Count > 0 ? Sequence.Dequeue() : new PrinterStatusInfo(true, false, "Ready", 0);
    }
}
