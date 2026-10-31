using System.Diagnostics;
using System.Text;
using System.Windows.Media.Imaging;
using BulkPrint.App.Rendering;
using BulkPrint.Core;
using BulkPrint.Core.Models;
using BulkPrint.Core.Services;

namespace BulkPrint.App.Services;

/// <summary>
/// Automated check of the installed program, used by the build pipeline on a clean Windows machine:
///   BulkPrintManager.exe --selftest "C:\samples" "C:\output"
/// Verifies the developer credit in the .exe properties, opens and previews every sample document,
/// and prints them to "Microsoft Print to PDF" when that printer exists. Results go to results.txt.
/// </summary>
internal static class SelfTestRunner
{
    public const string SamplePassword = "test1234";

    public static async Task<int> RunAsync(string[] args, IAppLogger log)
    {
        string samples = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "samples");
        string output = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "BulkPrintManagerSelfTest");
        Directory.CreateDirectory(output);
        var sb = new StringBuilder();
        int failures = 0, checks = 0;

        void Line(string text)
        {
            sb.AppendLine(text);
            log.Info("[selftest] " + text);
        }

        void Check(bool ok, string what)
        {
            checks++;
            if (!ok) failures++;
            Line((ok ? "PASS  " : "FAIL  ") + what);
        }

        Line($"{AppInfo.Name} {AppInfo.Version} self-test, {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        Line($"Windows: {Environment.OSVersion.VersionString}, 64-bit process: {Environment.Is64BitProcess}");
        Line($"Program folder: {AppContext.BaseDirectory}");
        Line("");

        // 1. Developer credit in the program and its file properties.
        Check(AppInfo.DeveloperCredit == "Designed and Developed by Harish Vaishnav", "Credit text is \"Designed and Developed by Harish Vaishnav\"");
        var exe = Environment.ProcessPath;
        if (exe is not null && File.Exists(exe))
        {
            var info = FileVersionInfo.GetVersionInfo(exe);
            Line($"EXE properties: Product='{info.ProductName}', Company='{info.CompanyName}', Description='{info.FileDescription}', Copyright='{info.LegalCopyright}', Version='{info.FileVersion}'");
            Check(info.CompanyName == "Harish Vaishnav", "EXE Company is Harish Vaishnav");
            Check(info.ProductName == "BulkPrint Manager", "EXE Product name is BulkPrint Manager");
            Check(info.LegalCopyright?.Contains("Harish Vaishnav") == true, "EXE Copyright names Harish Vaishnav");
            Check(info.FileDescription?.Contains("BulkPrint Manager") == true, "EXE File description names BulkPrint Manager");
        }
        else
        {
            Check(false, "Could not locate the running .exe");
        }
        Check(File.Exists(Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.txt")), "THIRD-PARTY-NOTICES.txt is installed");

        // 2. Engines and printers.
        var paths = new StoragePaths(Path.Combine(output, "data-roaming"), Path.Combine(output, "data-local"));
        var office = new OfficeConverter(paths, log, () => 300);
        Line($"Office engine: {(office.IsAvailable ? office.SofficePath : "NOT INSTALLED")}");
        var factory = new PageSourceFactory(office);
        var inspector = new DocumentInspector(factory, log);
        var preview = new PreviewService(factory);
        var printerService = new PrinterService();
        var printers = printerService.GetPrinterNames();
        Line("Printers: " + (printers.Count == 0 ? "(none)" : string.Join("; ", printers)));
        var pdfPrinter = printers.FirstOrDefault(p => p.Equals("Microsoft Print to PDF", StringComparison.OrdinalIgnoreCase));
        if (pdfPrinter is null) Line("NOTE  'Microsoft Print to PDF' is not installed on this machine, so print-to-file checks are skipped.");

        var global = new GlobalPrintSettings { PrinterName = pdfPrinter ?? printerService.GetDefaultPrinterName() };
        if (global.PrinterName is not null)
        {
            var caps = printerService.GetCapabilities(global.PrinterName);
            global.PaperSizeName = caps.PaperSizes.FirstOrDefault(p => p.StartsWith("A4", StringComparison.OrdinalIgnoreCase)) ?? caps.DefaultPaperSize;
            Line($"Using printer '{global.PrinterName}', paper '{global.PaperSizeName}', status '{printerService.GetStatus(global.PrinterName).Text}'");
        }
        Line("");

        if (!Directory.Exists(samples))
        {
            Check(false, "Samples folder exists: " + samples);
            return Finish();
        }

        var printer = new DocumentPrinter(factory, log)
        {
            OutputFileOverride = item => Path.Combine(output, $"printed_{Path.GetFileNameWithoutExtension(item.FileName)}_{item.Extension.TrimStart('.')}.pdf"),
        };

        foreach (var file in Directory.GetFiles(samples).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            if (!FileTypes.IsSupported(file)) continue;
            var name = Path.GetFileName(file);
            var item = new DocumentItem(file) { SizeBytes = new FileInfo(file).Length, Modified = File.GetLastWriteTime(file) };
            item.AttachGlobal(global);
            bool expectFailure = name.Contains("corrupt", StringComparison.OrdinalIgnoreCase);
            var timer = Stopwatch.StartNew();
            try
            {
                await inspector.InspectAsync(item, global, CancellationToken.None);
                if (expectFailure)
                {
                    Check(item.Status == PrintStatus.Failed, $"{name}: damaged file is flagged ({item.StatusDetail})");
                    continue;
                }
                if (item.Status == PrintStatus.NeedsPassword)
                {
                    Check(!await inspector.TryUnlockAsync(item, "wrong-password", CancellationToken.None), $"{name}: wrong password rejected");
                    Check(await inspector.TryUnlockAsync(item, SamplePassword, CancellationToken.None), $"{name}: unlocked with the correct password");
                }
                if (item.Status != PrintStatus.Pending)
                {
                    Check(false, $"{name}: ready to print (status {item.Status}: {item.StatusDetail})");
                    continue;
                }

                var image = await preview.RenderAsync(item, item.EffectiveOptions, global, 1, 60, allowConversion: true, CancellationToken.None);
                bool previewOk = image.Error is null && image.Image is BitmapSource;
                if (image.Image is BitmapSource bmp) SavePng(bmp, Path.Combine(output, $"preview_{Path.GetFileNameWithoutExtension(name)}_{item.Extension.TrimStart('.')}.png"));
                Check(previewOk, $"{name}: preview rendered, {item.PageCount} page(s){(image.Error is null ? "" : " - " + image.Error)}");

                if (pdfPrinter is not null)
                {
                    var outcome = printer.Print(item, item.EffectiveOptions, global, false, null, new NullSink(), CancellationToken.None);
                    var target = printer.OutputFileOverride!(item);
                    bool written = await WaitForFileAsync(target, TimeSpan.FromSeconds(60));
                    Check(outcome.PagesSent > 0 && written,
                        $"{name}: printed {outcome.PagesSent} page(s) to PDF in {timer.Elapsed.TotalSeconds:0.0}s{(written ? "" : " - output file not created")}");
                }
            }
            catch (Exception ex)
            {
                Check(false, $"{name}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // 3. Option handling end to end: page range, copies, collation, separator through the batch engine.
        var pdfSample = Directory.GetFiles(samples, "*.pdf").FirstOrDefault(f => !f.Contains("protected", StringComparison.OrdinalIgnoreCase) && !f.Contains("corrupt", StringComparison.OrdinalIgnoreCase));
        if (pdfPrinter is not null && pdfSample is not null)
        {
            try
            {
                var a = new DocumentItem(pdfSample) { SizeBytes = new FileInfo(pdfSample).Length };
                var b = new DocumentItem(pdfSample) { SizeBytes = new FileInfo(pdfSample).Length };
                a.AttachGlobal(global);
                b.AttachGlobal(global);
                a.Overrides = new DocumentPrintOptions { PageSelection = PageSelectionMode.Range, PageRange = "2-3", Copies = 2, Collate = true };
                b.Overrides = new DocumentPrintOptions { PageSelection = PageSelectionMode.Odd };
                var batchPrinter = new DocumentPrinter(factory, log);
                var engine = new PrintEngine(batchPrinter, printerService, log);
                var batchGlobal = global.CloneGlobal();
                batchGlobal.Separator = SeparatorMode.FileNamePage;
                batchGlobal.MaxJobsInPrinterQueue = 0;
                batchGlobal.SaveOutput = true;
                batchGlobal.OutputFolder = Path.Combine(output, "saved-output");
                var report = await engine.RunAsync(new[] { a, b }, batchGlobal, () => Task.CompletedTask, null, CancellationToken.None);
                Check(report.Printed == 2, $"Batch engine printed both documents (printed {report.Printed}, failed {report.Failed})");
                Check(report.Entries[0].PagesSent == 4, $"Page range 2-3 with 2 collated copies sends 4 pages (sent {report.Entries[0].PagesSent})");
                Line($"Batch report: {report.Headline}; pages {report.TotalPages}");
                report.SavePdf(Path.Combine(output, "batch-report.pdf"));
                report.SaveCsv(Path.Combine(output, "batch-report.csv"));
                Check(File.Exists(Path.Combine(output, "batch-report.pdf")), "Report exported to PDF and CSV");
                Check(printerService.GetFileOutputExtension(pdfPrinter) == ".pdf", "Microsoft Print to PDF is recognised as a save-to-file printer");
                var savedName = Path.GetFileNameWithoutExtension(pdfSample);
                bool savedA = await WaitForFileAsync(Path.Combine(batchGlobal.OutputFolder, savedName + ".pdf"), TimeSpan.FromSeconds(60));
                bool savedB = await WaitForFileAsync(Path.Combine(batchGlobal.OutputFolder, savedName + " (2).pdf"), TimeSpan.FromSeconds(60));
                Check(savedA && savedB, "Save Print Output saved both documents automatically, with no Save As dialog");
            }
            catch (Exception ex)
            {
                Check(false, "Batch engine test: " + ex.Message);
            }
        }

        office.CleanUp();
        return Finish();

        int Finish()
        {
            Line("");
            Line($"Checks: {checks}, failures: {failures}");
            File.WriteAllText(Path.Combine(output, "results.txt"), sb.ToString(), Encoding.UTF8);
            return failures == 0 ? 0 : 1;
        }
    }

    private static async Task<bool> WaitForFileAsync(string path, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            try
            {
                if (File.Exists(path) && new FileInfo(path).Length > 0)
                {
                    using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    return true;
                }
            }
            catch (IOException) { }
            await Task.Delay(300);
        }
        return false;
    }

    private static void SavePng(BitmapSource image, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }

    private sealed class NullSink : IPrintProgressSink
    {
        public void SetStatus(PrintStatus status, string? detail = null) { }
        public void PageSent() { }
    }
}
