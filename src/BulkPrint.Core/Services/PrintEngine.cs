using System.Diagnostics;
using BulkPrint.Core.Models;

namespace BulkPrint.Core.Services;

/// <summary>Lets a running batch be paused between documents.</summary>
public sealed class PauseGate
{
    private readonly object _lock = new();
    private TaskCompletionSource _tcs = NewCompleted();

    public bool IsPaused { get; private set; }
    public event Action<bool>? Changed;

    private static TaskCompletionSource NewCompleted()
    {
        var t = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        t.SetResult();
        return t;
    }

    public void Pause()
    {
        lock (_lock)
        {
            if (IsPaused) return;
            IsPaused = true;
            _tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        Changed?.Invoke(true);
    }

    public void Resume()
    {
        TaskCompletionSource t;
        lock (_lock)
        {
            if (!IsPaused) return;
            IsPaused = false;
            t = _tcs;
        }
        t.TrySetResult();
        Changed?.Invoke(false);
    }

    public Task WaitAsync(CancellationToken ct)
    {
        Task t;
        lock (_lock) t = _tcs.Task;
        return t.WaitAsync(ct);
    }
}

public sealed record BatchProgress(
    int Completed,
    int Total,
    int PagesSent,
    int? EstimatedTotalPages,
    TimeSpan? Remaining,
    string? CurrentFile);

/// <summary>
/// Runs a batch: one print job per document, with pause/resume, cancel, skip,
/// printer problem detection and printer-queue throttling.
/// </summary>
public sealed class PrintEngine
{
    private readonly IDocumentPrinter _printer;
    private readonly IPrinterService _printers;
    private readonly IAppLogger _log;
    private CancellationTokenSource? _skipCurrent;
    private readonly object _skipLock = new();

    public PrintEngine(IDocumentPrinter printer, IPrinterService printers, IAppLogger log)
    {
        _printer = printer;
        _printers = printers;
        _log = log;
    }

    public PauseGate Gate { get; } = new();

    /// <summary>How long to wait between printer-queue checks while throttling.</summary>
    public TimeSpan ThrottlePollInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Raised (on a background thread) when the batch pauses itself because of a printer problem.</summary>
    public event Action<string>? AutoPaused;

    /// <summary>Raised (on a background thread) while waiting for the printer queue to drain.</summary>
    public event Action<string>? Waiting;

    /// <summary>Stops the document that is printing now and marks it Skipped. The batch continues.</summary>
    public void SkipCurrent()
    {
        lock (_skipLock) _skipCurrent?.Cancel();
    }

    public static bool IsPrintable(DocumentItem item) => item.Status == PrintStatus.Pending;

    public async Task<BatchReport> RunAsync(
        IReadOnlyList<DocumentItem> items,
        GlobalPrintSettings global,
        Func<Task> persist,
        IProgress<BatchProgress>? progress,
        CancellationToken ct)
    {
        var printerName = global.PrinterName ?? throw new InvalidOperationException("No printer is selected.");
        var report = new BatchReport { PrinterName = printerName, StartTime = DateTime.Now };

        // Save Print Output: for printers that write files (Microsoft Print to PDF and similar), name and
        // save every document automatically so Windows never asks "Save Print Output As".
        OutputNaming? naming = null;
        string? extension = null;
        try { extension = _printers.GetFileOutputExtension(printerName); }
        catch (Exception ex) { _log.Warn("Could not check the printer type: " + ex.Message); }
        if (extension is not null && global.SaveOutput)
        {
            var folder = global.EffectiveOutputFolder;
            try { Directory.CreateDirectory(folder); }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"The output folder \"{folder}\" cannot be used: {ex.Message}", ex);
            }
            naming = new OutputNaming(folder, extension);
            report.OutputFolder = folder;
            _log.Info($"Saving print output to '{folder}' as {extension} files.");
        }
        var reported = new HashSet<DocumentItem>();
        void Add(DocumentItem item, PrintStatus status, int pages, string message, TimeSpan duration)
        {
            reported.Add(item);
            report.Entries.Add(new ReportEntry(item.FileName, item.Path, status, pages, message, duration));
        }
        var toPrint = items.Where(IsPrintable).ToList();
        int total = toPrint.Count;
        int completed = 0, pagesSent = 0;
        var timer = Stopwatch.StartNew();
        bool first = true;

        int? EstimatedPages()
        {
            if (toPrint.Any(i => i.PageCount is null)) return null;
            long sum = 0;
            foreach (var i in toPrint)
            {
                var o = i.EffectiveOptions;
                int pages = i.PageCount ?? 0;
                if (o.PageSelection == PageSelectionMode.Odd) pages = (pages + 1) / 2;
                else if (o.PageSelection == PageSelectionMode.Even) pages /= 2;
                else if (o.PageSelection == PageSelectionMode.Range)
                {
                    try { pages = PageRangeParser.SelectPages(pages, o.PageSelection, o.PageRange).Count; }
                    catch { }
                }
                sum += (long)pages * o.Copies;
            }
            return (int)Math.Min(int.MaxValue, sum);
        }

        int? estimate = EstimatedPages();

        void Report(string? current)
        {
            TimeSpan? remaining = null;
            if (completed > 0 && completed < total)
                remaining = TimeSpan.FromTicks(timer.Elapsed.Ticks / completed * (total - completed));
            progress?.Report(new BatchProgress(completed, total, pagesSent, estimate, remaining, current));
        }

        // Documents that cannot be printed are listed in the report so nothing is silently left out.
        foreach (var item in items.Where(i => i.Status is PrintStatus.NeedsPassword or PrintStatus.Unsupported))
        {
            Add(item, PrintStatus.Skipped, 0,
                item.Status == PrintStatus.NeedsPassword ? "Not printed: the PDF password was not entered" : "Not printed: " + item.StatusDetail,
                TimeSpan.Zero);
        }

        _log.Info($"Batch started: {total} documents to '{printerName}'.");
        Report(null);

        try
        {
            foreach (var item in toPrint)
            {
                await Gate.WaitAsync(ct);
                ct.ThrowIfCancellationRequested();

                // The user may have skipped or removed it while the batch was running.
                if (item.Status != PrintStatus.Pending)
                {
                    if (item.Status == PrintStatus.Skipped)
                        Add(item, PrintStatus.Skipped, 0, "Skipped by user", TimeSpan.Zero);
                    completed++;
                    Report(null);
                    continue;
                }

                await WaitForHealthyPrinterAsync(printerName, global, ct);

                var options = item.EffectiveOptions;
                var docTimer = Stopwatch.StartNew();
                var sink = new Sink(item, () => Interlocked.Increment(ref pagesSent), () => Report(item.FileName));
                using var skipCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                lock (_skipLock) _skipCurrent = skipCts;
                // Separator pages make no sense inside separately saved files.
                bool separator = !first && global.Separator != SeparatorMode.None && naming is null;
                string? outputFile = naming?.Next(item.FileName);
                first = false;

                try
                {
                    item.PagesSent = 0;
                    item.StatusDetail = "";
                    Report(item.FileName);
                    var outcome = await Task.Run(() => _printer.Print(item, options, global, separator, outputFile, sink, skipCts.Token), CancellationToken.None);
                    item.Status = PrintStatus.Printed;
                    var saved = outcome.OutputFile ?? outputFile;
                    var savedNote = saved is null ? null : $"Saved as {saved}";
                    item.StatusDetail = string.Join(" ", new[] { savedNote, outcome.Warning }.Where(s => !string.IsNullOrEmpty(s)))
                                        is { Length: > 0 } detail
                        ? detail
                        : $"{outcome.PagesSent} page{(outcome.PagesSent == 1 ? "" : "s")} sent at {DateTime.Now:HH:mm:ss}";
                    Add(item, PrintStatus.Printed, outcome.PagesSent,
                        string.Join(" ", new[] { savedNote, outcome.Warning }.Where(s => !string.IsNullOrEmpty(s))), docTimer.Elapsed);
                    _log.Info($"Printed '{item.Path}' ({outcome.PagesSent} pages).");
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    item.Status = PrintStatus.Pending;
                    item.StatusDetail = "Cancelled before this document finished. It will print again next time.";
                    throw;
                }
                catch (OperationCanceledException)
                {
                    item.Status = PrintStatus.Skipped;
                    item.StatusDetail = "Skipped by user while printing";
                    Add(item, PrintStatus.Skipped, item.PagesSent, "Skipped by user while printing", docTimer.Elapsed);
                    _log.Info($"Skipped '{item.Path}' while printing.");
                }
                catch (Exception ex)
                {
                    item.Status = PrintStatus.Failed;
                    item.StatusDetail = FriendlyError(ex);
                    Add(item, PrintStatus.Failed, item.PagesSent, item.StatusDetail, docTimer.Elapsed);
                    _log.Error($"Failed to print '{item.Path}'.", ex);
                }
                finally
                {
                    lock (_skipLock) _skipCurrent = null;
                }

                completed++;
                Report(null);
                try { await persist(); } catch (Exception ex) { _log.Warn("Could not save session: " + ex.Message); }

                if (global.DelayBetweenDocumentsSeconds > 0 && completed < total)
                    await Task.Delay(TimeSpan.FromSeconds(global.DelayBetweenDocumentsSeconds), ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            report.WasCancelled = true;
            _log.Info("Batch cancelled by user.");
        }

        // Anything not reached is listed so the report is complete.
        foreach (var item in toPrint.Where(i => !reported.Contains(i)))
        {
            if (item.Status == PrintStatus.Skipped)
                Add(item, PrintStatus.Skipped, 0, "Skipped by user", TimeSpan.Zero);
            else
                Add(item, PrintStatus.Pending, 0, "Not printed (batch cancelled)", TimeSpan.Zero);
        }

        report.EndTime = DateTime.Now;
        _log.Info($"Batch finished: printed {report.Printed}, failed {report.Failed}, skipped {report.Skipped}, pages {report.TotalPages}.");
        return report;
    }

    private async Task WaitForHealthyPrinterAsync(string printerName, GlobalPrintSettings global, CancellationToken ct)
    {
        while (true)
        {
            var status = SafeStatus(printerName);
            if (status.HasProblem)
            {
                Gate.Pause();
                _log.Warn($"Printer problem, batch paused: {status.Text}");
                AutoPaused?.Invoke($"The printer reports a problem: {status.Text}. Fix the printer, then click Resume.");
                await Gate.WaitAsync(ct);
                continue;
            }

            int max = global.MaxJobsInPrinterQueue;
            if (max > 0 && status.JobsInQueue >= max)
            {
                Waiting?.Invoke($"Waiting for the printer: {status.JobsInQueue} jobs already in its queue…");
                await Task.Delay(ThrottlePollInterval, ct);
                await Gate.WaitAsync(ct);
                continue;
            }
            return;
        }
    }

    private PrinterStatusInfo SafeStatus(string printerName)
    {
        try { return _printers.GetStatus(printerName); }
        catch (Exception ex)
        {
            _log.Warn("Could not read printer status: " + ex.Message);
            return PrinterStatusInfo.Unknown();
        }
    }

    public static string FriendlyError(Exception ex)
    {
        var msg = ex switch
        {
            FileNotFoundException => "The file could not be found. It may have been moved or deleted.",
            DirectoryNotFoundException => "The folder containing this file could not be found.",
            UnauthorizedAccessException => "Windows denied access to this file.",
            IOException io when io.Message.Contains("being used", StringComparison.OrdinalIgnoreCase)
                => "The file is open in another program. Close it and try again.",
            FormatException f => f.Message,
            _ => ex.Message,
        };
        return string.IsNullOrWhiteSpace(msg) ? "An unknown error occurred." : msg.Trim();
    }

    private sealed class Sink : IPrintProgressSink
    {
        private readonly DocumentItem _item;
        private readonly Action _pageSent;
        private readonly Action _changed;

        public Sink(DocumentItem item, Action pageSent, Action changed)
        {
            _item = item;
            _pageSent = pageSent;
            _changed = changed;
        }

        public void SetStatus(PrintStatus status, string? detail = null)
        {
            _item.Status = status;
            if (detail is not null) _item.StatusDetail = detail;
        }

        public void PageSent()
        {
            _item.PagesSent++;
            _pageSent();
            _changed();
        }
    }
}
