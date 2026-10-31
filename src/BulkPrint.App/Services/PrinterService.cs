using System.ComponentModel;
using System.Drawing.Printing;
using System.Runtime.InteropServices;
using BulkPrint.Core.Services;

namespace BulkPrint.App.Services;

/// <summary>Printers installed in Windows, their capabilities and live status.</summary>
internal sealed class PrinterService : IPrinterService
{
    public IReadOnlyList<string> GetPrinterNames()
    {
        var list = new List<string>();
        foreach (string name in PrinterSettings.InstalledPrinters) list.Add(name);
        list.Sort(StringComparer.CurrentCultureIgnoreCase);
        return list;
    }

    public string? GetDefaultPrinterName()
    {
        try
        {
            var ps = new PrinterSettings();
            return ps.IsValid && ps.IsDefaultPrinter ? ps.PrinterName : null;
        }
        catch
        {
            return null;
        }
    }

    public PrinterCapabilities GetCapabilities(string printerName)
    {
        var ps = new PrinterSettings { PrinterName = printerName };
        if (!ps.IsValid) return PrinterCapabilities.None;

        var papers = new List<string>();
        foreach (PaperSize p in ps.PaperSizes)
            if (!string.IsNullOrWhiteSpace(p.PaperName) && !papers.Contains(p.PaperName)) papers.Add(p.PaperName);

        var sources = new List<string>();
        foreach (PaperSource s in ps.PaperSources)
            if (!string.IsNullOrWhiteSpace(s.SourceName) && !sources.Contains(s.SourceName)) sources.Add(s.SourceName);

        string? defaultPaper = null, defaultSource = null;
        try
        {
            defaultPaper = ps.DefaultPageSettings.PaperSize?.PaperName;
            defaultSource = ps.DefaultPageSettings.PaperSource?.SourceName;
        }
        catch { }

        bool color;
        try { color = ps.SupportsColor; } catch { color = false; }
        bool duplex;
        try { duplex = ps.CanDuplex; } catch { duplex = false; }

        return new PrinterCapabilities(papers, sources, duplex, color, Math.Max(1, ps.MaximumCopies), true, defaultPaper, defaultSource);
    }

    public PrinterStatusInfo GetStatus(string printerName)
    {
        if (!OpenPrinter(printerName, out var handle, IntPtr.Zero))
            return new PrinterStatusInfo(false, true, "Printer not available (" + new Win32Exception(Marshal.GetLastWin32Error()).Message + ")", 0);
        try
        {
            GetPrinter(handle, 2, IntPtr.Zero, 0, out int needed);
            if (needed <= 0) return PrinterStatusInfo.Unknown();
            var buffer = Marshal.AllocHGlobal(needed);
            try
            {
                if (!GetPrinter(handle, 2, buffer, needed, out _)) return PrinterStatusInfo.Unknown();
                var info = Marshal.PtrToStructure<PRINTER_INFO_2>(buffer);
                return Describe(info.Status, info.Attributes, (int)info.cJobs);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            ClosePrinter(handle);
        }
    }

    private static PrinterStatusInfo Describe(uint status, uint attributes, int jobs)
    {
        if ((attributes & PRINTER_ATTRIBUTE_WORK_OFFLINE) != 0)
            return new PrinterStatusInfo(false, true, "Printer is set to \"Use printer offline\"", jobs);

        (uint flag, string text)[] problems =
        {
            (0x00000010, "Out of paper"),
            (0x00000008, "Paper jam"),
            (0x00000040, "Paper problem"),
            (0x00040000, "Out of toner or ink"),
            (0x00400000, "A printer door is open"),
            (0x00000080, "Printer is offline"),
            (0x00001000, "Printer is not available"),
            (0x00100000, "The printer needs attention"),
            (0x00000002, "Printer error"),
            (0x00000800, "Output bin is full"),
            (0x00000001, "Printer is paused in Windows"),
            (0x00800000, "Printer is out of memory"),
        };
        foreach (var (flag, text) in problems)
            if ((status & flag) != 0) return new PrinterStatusInfo(false, true, text, jobs);

        if ((status & 0x00020000) != 0) return new PrinterStatusInfo(true, false, "Low on toner or ink", jobs);
        if ((status & 0x00000400) != 0) return new PrinterStatusInfo(true, false, "Printing", jobs);
        if ((status & 0x00004000) != 0) return new PrinterStatusInfo(true, false, "Processing", jobs);
        if ((status & 0x00010000) != 0) return new PrinterStatusInfo(true, false, "Warming up", jobs);
        if ((status & 0x00000200) != 0) return new PrinterStatusInfo(true, false, "Busy", jobs);
        if ((status & 0x00008000) != 0) return new PrinterStatusInfo(true, false, "Initializing", jobs);
        return new PrinterStatusInfo(true, false, "Ready", jobs);
    }

    private const uint PRINTER_ATTRIBUTE_WORK_OFFLINE = 0x00000400;

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "GetPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetPrinter(IntPtr hPrinter, int level, IntPtr pPrinter, int cbBuf, out int pcbNeeded);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PRINTER_INFO_2
    {
        public string? pServerName;
        public string? pPrinterName;
        public string? pShareName;
        public string? pPortName;
        public string? pDriverName;
        public string? pComment;
        public string? pLocation;
        public IntPtr pDevMode;
        public string? pSepFile;
        public string? pPrintProcessor;
        public string? pDatatype;
        public string? pParameters;
        public IntPtr pSecurityDescriptor;
        public uint Attributes;
        public uint Priority;
        public uint DefaultPriority;
        public uint StartTime;
        public uint UntilTime;
        public uint Status;
        public uint cJobs;
        public uint AveragePPM;
    }
}
