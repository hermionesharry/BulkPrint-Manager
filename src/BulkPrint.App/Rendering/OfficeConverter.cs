using System.Diagnostics;
using BulkPrint.Core.Services;

namespace BulkPrint.App.Rendering;

/// <summary>
/// Converts Word, Excel and PowerPoint files to PDF with the LibreOffice engine bundled in the
/// "engine" folder next to BulkPrintManager.exe. Runs fully offline, one conversion at a time.
/// </summary>
internal sealed class OfficeConverter
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly string _tempRoot;
    private readonly string _profileDir;
    private readonly IAppLogger _log;
    private readonly Func<int> _timeoutSeconds;

    public OfficeConverter(StoragePaths paths, IAppLogger log, Func<int> timeoutSeconds)
    {
        _tempRoot = Path.Combine(paths.TempDir, "Converted");
        _profileDir = paths.OfficeProfileDir;
        _log = log;
        _timeoutSeconds = timeoutSeconds;
        SofficePath = FindEngine();
        if (SofficePath is null) _log.Warn("Office conversion engine not found. Word, Excel and PowerPoint files cannot be printed.");
        else _log.Info("Office conversion engine: " + SofficePath);
    }

    public string? SofficePath { get; }
    public bool IsAvailable => SofficePath is not null;

    private static string? FindEngine()
    {
        var baseDir = AppContext.BaseDirectory;
        foreach (var candidate in new[]
                 {
                     Path.Combine(baseDir, "engine", "program", "soffice.exe"),
                     Path.Combine(baseDir, "engine", "LibreOffice", "program", "soffice.exe"),
                 })
        {
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>Returns the path of a PDF made from the document. Throws with a clear message on failure.</summary>
    public string ConvertToPdf(string inputPath, CancellationToken ct)
    {
        if (SofficePath is null)
            throw new InvalidOperationException("The Office conversion engine is not installed, so this document cannot be printed.");

        Gate.Wait(ct);
        try
        {
            var outDir = Path.Combine(_tempRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outDir);
            Directory.CreateDirectory(_profileDir);

            var psi = new ProcessStartInfo(SofficePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = outDir,
            };
            // A private engine profile keeps this separate from any LibreOffice the user has installed.
            psi.ArgumentList.Add("-env:UserInstallation=" + new Uri(_profileDir + Path.DirectorySeparatorChar).AbsoluteUri);
            psi.ArgumentList.Add("--headless");
            psi.ArgumentList.Add("--invisible");
            psi.ArgumentList.Add("--norestore");
            psi.ArgumentList.Add("--nolockcheck");
            psi.ArgumentList.Add("--nodefault");
            psi.ArgumentList.Add("--nologo");
            psi.ArgumentList.Add("--convert-to");
            psi.ArgumentList.Add("pdf");
            psi.ArgumentList.Add("--outdir");
            psi.ArgumentList.Add(outDir);
            psi.ArgumentList.Add(inputPath);

            var timer = Stopwatch.StartNew();
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("The Office conversion engine could not be started.");
            var timeout = TimeSpan.FromSeconds(Math.Max(30, _timeoutSeconds()));
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);
            try
            {
                process.WaitForExitAsync(timeoutCts.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                ct.ThrowIfCancellationRequested();
                throw new TimeoutException($"Converting this document took longer than {timeout.TotalSeconds:0} seconds and was stopped. " +
                                           "It may be very large, password-protected or damaged.");
            }

            // The launcher can exit before the engine (soffice.bin) has finished writing the PDF,
            // so keep looking while the engine is still running, up to the timeout.
            string? pdf = null;
            int graceChecks = 0;
            while (pdf is null)
            {
                pdf = FindOutput(outDir);
                if (pdf is not null) break;
                ct.ThrowIfCancellationRequested();
                if (timer.Elapsed > timeout)
                    throw new TimeoutException($"Converting this document took longer than {timeout.TotalSeconds:0} seconds and was stopped.");
                bool engineRunning = Process.GetProcessesByName("soffice.bin").Length > 0;
                if (!engineRunning && ++graceChecks > 15) break; // about 3 seconds after the engine stopped
                Thread.Sleep(200);
            }
            if (pdf is null)
                throw new InvalidDataException("This document could not be converted for printing. It may be password-protected, damaged, or use features the built-in Office engine does not support.");

            _log.Info($"Converted '{inputPath}' in {timer.Elapsed.TotalSeconds:0.0}s.");
            return pdf;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static string? FindOutput(string outDir)
    {
        foreach (var f in Directory.EnumerateFiles(outDir, "*.pdf"))
        {
            try
            {
                // The file must exist, have content and not be locked by the engine any more.
                using var s = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (s.Length > 0) return f;
            }
            catch (IOException) { }
        }
        return null;
    }

    private void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            _log.Warn("Could not stop the conversion engine: " + ex.Message);
        }
    }

    public void CleanUp()
    {
        try
        {
            if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            // Files may still be in use; they will be removed next time.
        }
    }
}
