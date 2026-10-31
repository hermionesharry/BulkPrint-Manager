using System.Text;

namespace BulkPrint.Core.Services;

public interface IAppLogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? ex = null);
}

/// <summary>
/// Thread-safe log file in %LocalAppData%\BulkPrintManager\Logs with simple rotation
/// (bulkprint.log, bulkprint.1.log … bulkprint.4.log, 2 MB each).
/// </summary>
public sealed class FileLogger : IAppLogger
{
    private readonly string _dir;
    private readonly string _file;
    private readonly long _maxBytes;
    private readonly int _keep;
    private readonly object _lock = new();

    public FileLogger(string directory, long maxBytes = 2 * 1024 * 1024, int keepFiles = 5)
    {
        _dir = directory;
        _file = Path.Combine(directory, "bulkprint.log");
        _maxBytes = maxBytes;
        _keep = Math.Max(1, keepFiles);
        try { Directory.CreateDirectory(directory); } catch { }
    }

    public string LogDirectory => _dir;
    public string CurrentFile => _file;

    public void Info(string message) => Write("INFO ", message);
    public void Warn(string message) => Write("WARN ", message);
    public void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}{Environment.NewLine}{ex}");

    private void Write(string level, string message)
    {
        try
        {
            lock (_lock)
            {
                RotateIfNeeded();
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(_file, line, Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never break printing.
        }
    }

    private void RotateIfNeeded()
    {
        var info = new FileInfo(_file);
        if (!info.Exists || info.Length < _maxBytes) return;
        for (int i = _keep - 1; i >= 1; i--)
        {
            var src = i == 1 ? _file : Path.Combine(_dir, $"bulkprint.{i - 1}.log");
            var dst = Path.Combine(_dir, $"bulkprint.{i}.log");
            if (File.Exists(src)) File.Move(src, dst, overwrite: true);
        }
    }
}

public sealed class NullLogger : IAppLogger
{
    public static readonly NullLogger Instance = new();
    public void Info(string message) { }
    public void Warn(string message) { }
    public void Error(string message, Exception? ex = null) { }
}
