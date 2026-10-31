using System.Globalization;
using System.IO.Pipes;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using BulkPrint.Core.Services;

namespace BulkPrint.App.Services;

internal sealed class WpfDispatcher : IUiDispatcher
{
    private readonly Dispatcher _dispatcher;
    public WpfDispatcher(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public void Post(Action action) => _dispatcher.BeginInvoke(action);

    public Task InvokeAsync(Action action) =>
        _dispatcher.CheckAccess() ? RunInline(action) : _dispatcher.InvokeAsync(action).Task;

    private static Task RunInline(Action action)
    {
        action();
        return Task.CompletedTask;
    }
}

/// <summary>
/// Keeps one copy of BulkPrint Manager running per user. When Explorer's
/// "Add to BulkPrint Manager" starts a second copy, its file list is passed to the first one.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private readonly string _pipeName;
    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _cts = new();

    public SingleInstance()
    {
        var user = Environment.UserName.Replace('\\', '_');
        _pipeName = $"BulkPrintManager-{user}";
        _mutex = new Mutex(initiallyOwned: true, $@"Local\BulkPrintManager-{user}", out bool created);
        IsPrimary = created;
    }

    public bool IsPrimary { get; }

    /// <summary>Sends paths to the running copy. Retries for a few seconds while it starts up.</summary>
    public bool SendToPrimary(IReadOnlyList<string> args)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
                client.Connect(1000);
                using var writer = new StreamWriter(client, new UTF8Encoding(false));
                writer.WriteLine(args.Count == 0 ? "--activate" : string.Join("\n", args));
                writer.WriteLine("\u0004");
                writer.Flush();
                return true;
            }
            catch (TimeoutException) { }
            catch (IOException) { Thread.Sleep(200); }
        }
        return false;
    }

    /// <summary>Starts listening for other copies. The callback receives paths (empty = just bring to front).</summary>
    public void StartServer(Action<IReadOnlyList<string>> onArgs)
    {
        _ = Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(_pipeName, PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(_cts.Token);
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var paths = new List<string>();
                    string? line;
                    while ((line = await reader.ReadLineAsync(_cts.Token)) is not null && line != "\u0004")
                    {
                        if (line.Length > 0 && line != "--activate") paths.Add(line);
                    }
                    onArgs(paths);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception)
                {
                    await Task.Delay(250);
                }
            }
        });
    }

    public void Dispose()
    {
        _cts.Cancel();
        if (IsPrimary)
        {
            try { _mutex.ReleaseMutex(); } catch { }
        }
        _mutex.Dispose();
    }
}

public sealed class InverseBooleanConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is bool b ? !b : true;
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is bool b ? !b : false;
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool v = value is bool b && b;
        if (Invert) v = !v;
        return v ? Visibility.Visible : Visibility.Collapsed;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility vis && (vis == Visibility.Visible) != Invert;
}

/// <summary>Visible when the value is a non-empty string or a non-null object (Invert flips it).</summary>
public sealed class HasValueToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool has = value switch
        {
            null => false,
            string s => s.Length > 0,
            _ => true,
        };
        if (Invert) has = !has;
        return has ? Visibility.Visible : Visibility.Collapsed;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
