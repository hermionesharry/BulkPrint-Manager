using System.Text.Json;
using System.Text.Json.Serialization;
using BulkPrint.Core.Models;

namespace BulkPrint.Core.Services;

public sealed class AppSettings
{
    public GlobalPrintSettings Global { get; set; } = new();
    public string? LastFolder { get; set; }
    public bool IncludeSubfolders { get; set; } = true;
    public bool DarkTheme { get; set; }
    public int OfficeConversionTimeoutSeconds { get; set; } = 180;
    public bool OfficeNoticeShown { get; set; }
}

public sealed class PrintProfile
{
    public string Name { get; set; } = "";
    public GlobalPrintSettings Settings { get; set; } = new();
}

public sealed class QueueEntry
{
    public string Path { get; set; } = "";
    public DocumentPrintOptions? Overrides { get; set; }
    public PrintStatus Status { get; set; } = PrintStatus.Pending;
    public string? StatusDetail { get; set; }
    public int? PageCount { get; set; }
}

/// <summary>A saved print queue (.bpq file) or the automatically saved session.</summary>
public sealed class QueueProject
{
    public int FormatVersion { get; set; } = 1;
    public string App { get; set; } = AppInfo.Name;
    public DateTime SavedAt { get; set; } = DateTime.Now;
    public bool BatchInProgress { get; set; }
    public GlobalPrintSettings? Global { get; set; }
    public List<QueueEntry> Items { get; set; } = new();

    public const string FileExtension = ".bpq";
    public const string DialogFilter = "BulkPrint queue (*.bpq)|*.bpq";
}

/// <summary>Reads and writes JSON files atomically (write to temp file, then replace).</summary>
public static class JsonStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static T? Load<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch
        {
            return null;
        }
    }

    public static void Save<T>(string path, T value)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
        File.Move(temp, path, overwrite: true);
    }
}

/// <summary>Locations of the files BulkPrint Manager keeps for the current Windows user.</summary>
public sealed class StoragePaths
{
    public StoragePaths(string roamingDir, string localDir)
    {
        RoamingDir = roamingDir;
        LocalDir = localDir;
    }

    public static StoragePaths Default => new(AppInfo.RoamingDataDir, AppInfo.LocalDataDir);

    public string RoamingDir { get; }
    public string LocalDir { get; }
    public string SettingsFile => Path.Combine(RoamingDir, "settings.json");
    public string ProfilesFile => Path.Combine(RoamingDir, "profiles.json");
    public string SessionFile => Path.Combine(RoamingDir, "session.json");
    public string LogDir => Path.Combine(LocalDir, "Logs");
    public string TempDir => Path.Combine(LocalDir, "Temp");
    public string OfficeProfileDir => Path.Combine(LocalDir, "OfficeEngineProfile");
}

public sealed class SettingsStore
{
    private readonly StoragePaths _paths;
    public SettingsStore(StoragePaths paths) => _paths = paths;
    public StoragePaths Paths => _paths;

    public AppSettings LoadSettings() => JsonStore.Load<AppSettings>(_paths.SettingsFile) ?? new AppSettings();
    public void SaveSettings(AppSettings s) => JsonStore.Save(_paths.SettingsFile, s);

    public List<PrintProfile> LoadProfiles() => JsonStore.Load<List<PrintProfile>>(_paths.ProfilesFile) ?? new List<PrintProfile>();
    public void SaveProfiles(List<PrintProfile> profiles) => JsonStore.Save(_paths.ProfilesFile, profiles);

    public QueueProject? LoadSession() => JsonStore.Load<QueueProject>(_paths.SessionFile);
    public void SaveSession(QueueProject session) => JsonStore.Save(_paths.SessionFile, session);
    public void ClearSession()
    {
        try { if (File.Exists(_paths.SessionFile)) File.Delete(_paths.SessionFile); } catch { }
    }
}
