using System.Reflection;

namespace BulkPrint.Core;

/// <summary>
/// Fixed product identity. The developer credit is intentionally a constant:
/// it is not stored in settings and cannot be changed or removed by the user.
/// </summary>
public static class AppInfo
{
    public const string Name = "BulkPrint Manager";
    public const string Tagline = "Print Hundreds of Documents. One Simple Workflow.";
    public const string DeveloperCredit = "Designed and Developed by Harish Vaishnav";
    public const string Developer = "Harish Vaishnav";
    public const string Copyright = "Copyright © 2026 Harish Vaishnav. All rights reserved.";
    public const string DataFolderName = "BulkPrintManager";

    public static string Version
    {
        get
        {
            var v = typeof(AppInfo).Assembly.GetName().Version;
            return v is null ? "1.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    /// <summary>Build date taken from the core assembly's informational metadata, or the file time.</summary>
    public static string BuildDate
    {
        get
        {
            try
            {
                var attr = typeof(AppInfo).Assembly
                    .GetCustomAttributes<AssemblyMetadataAttribute>()
                    .FirstOrDefault(a => a.Key == "BuildDate");
                if (attr?.Value is { Length: > 0 } value) return value;
                var path = AppContext.BaseDirectory;
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                    return File.GetLastWriteTime(exe).ToString("dd MMM yyyy");
                return Directory.GetLastWriteTime(path).ToString("dd MMM yyyy");
            }
            catch
            {
                return "";
            }
        }
    }

    /// <summary>%AppData%\BulkPrintManager — settings, profiles, saved session.</summary>
    public static string RoamingDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), DataFolderName);

    /// <summary>%LocalAppData%\BulkPrintManager — logs, conversion engine profile, temporary files.</summary>
    public static string LocalDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DataFolderName);

    public static string LogDir => Path.Combine(LocalDataDir, "Logs");
}
