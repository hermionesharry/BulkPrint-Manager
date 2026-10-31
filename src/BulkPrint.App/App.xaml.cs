using System.Windows;
using System.Windows.Threading;
using BulkPrint.App.Rendering;
using BulkPrint.App.Services;
using BulkPrint.App.Views;
using BulkPrint.Core;
using BulkPrint.Core.Services;
using BulkPrint.Core.ViewModels;

namespace BulkPrint.App;

public partial class App : Application
{
    private SingleInstance? _singleInstance;
    private FileLogger? _log;
    private MainViewModel? _viewModel;
    private readonly List<string> _pendingPaths = new();
    private bool _ready;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args ?? Array.Empty<string>();

        var paths = StoragePaths.Default;
        _log = new FileLogger(paths.LogDir);
        _log.Info($"{AppInfo.Name} {AppInfo.Version} starting. {AppInfo.DeveloperCredit}.");
        HookErrorHandlers();

        // Automated check used by the build pipeline: BulkPrintManager.exe --selftest <samples> <output>
        if (args.Length > 0 && args[0].Equals("--selftest", StringComparison.OrdinalIgnoreCase))
        {
            int code = await SelfTestRunner.RunAsync(args.Skip(1).ToArray(), _log);
            Shutdown(code);
            return;
        }

        var filePaths = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();

        _singleInstance = new SingleInstance();
        if (!_singleInstance.IsPrimary)
        {
            _singleInstance.SendToPrimary(filePaths);
            Shutdown(0);
            return;
        }
        _singleInstance.StartServer(OnPathsFromOtherInstance);

        var store = new SettingsStore(paths);
        var settings = store.LoadSettings();
        ApplyTheme(settings.DarkTheme);

        var splash = new SplashWindow();
        splash.Show();
        var splashShown = DateTime.UtcNow;

        try
        {
            var office = new OfficeConverter(paths, _log, () => settings.OfficeConversionTimeoutSeconds);
            office.CleanUp(); // remove leftovers from a previous run
            var sources = new PageSourceFactory(office);
            var printerService = new PrinterService();
            var documentPrinter = new DocumentPrinter(sources, _log);
            var engine = new PrintEngine(documentPrinter, printerService, _log);
            var inspector = new DocumentInspector(sources, _log);
            var preview = new PreviewService(sources);
            var dialogs = new WpfDialogService(preview);

            _viewModel = new MainViewModel(dialogs, new WpfDispatcher(Dispatcher), printerService, inspector, preview, engine, store, _log);
            _viewModel.ThemeChanged += ApplyTheme;

            var window = new MainWindow(_viewModel);
            MainWindow = window;

            // Keep the splash screen up briefly so the credit can be read.
            var elapsed = DateTime.UtcNow - splashShown;
            if (elapsed < TimeSpan.FromMilliseconds(1400)) await Task.Delay(TimeSpan.FromMilliseconds(1400) - elapsed);

            window.Show();
            splash.Close();
            ShutdownMode = ShutdownMode.OnMainWindowClose;

            lock (_pendingPaths)
            {
                filePaths.AddRange(_pendingPaths);
                _pendingPaths.Clear();
                _ready = true;
            }
            await _viewModel.InitializeAsync(filePaths);
        }
        catch (Exception ex)
        {
            _log.Error("Start-up failed.", ex);
            splash.Close();
            MessageBox.Show("BulkPrint Manager could not start.\n\n" + ex.Message + "\n\nDetails were written to the log file in:\n" + paths.LogDir,
                AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void OnPathsFromOtherInstance(IReadOnlyList<string> paths)
    {
        lock (_pendingPaths)
        {
            if (!_ready)
            {
                _pendingPaths.AddRange(paths);
                return;
            }
        }
        Dispatcher.BeginInvoke(async () =>
        {
            if (MainWindow is MainWindow w) w.BringToFront();
            if (_viewModel is not null && paths.Count > 0) await _viewModel.AddPathsAsync(paths);
        });
    }

    internal static void ApplyTheme(bool dark)
    {
#pragma warning disable WPF0001
        Current.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001
        var merged = Current.Resources.MergedDictionaries;
        var palette = merged.FirstOrDefault(d => d.Source is not null &&
            (d.Source.OriginalString.EndsWith("Colors.Light.xaml", StringComparison.OrdinalIgnoreCase) ||
             d.Source.OriginalString.EndsWith("Colors.Dark.xaml", StringComparison.OrdinalIgnoreCase)));
        var replacement = new ResourceDictionary
        {
            Source = new Uri(dark
                ? "pack://application:,,,/BulkPrintManager;component/Themes/Colors.Dark.xaml"
                : "pack://application:,,,/BulkPrintManager;component/Themes/Colors.Light.xaml", UriKind.Absolute),
        };
        if (palette is null) merged.Add(replacement);
        else merged[merged.IndexOf(palette)] = replacement;
    }

    private void HookErrorHandlers()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            _log?.Error("Unhandled UI error.", e.Exception);
            MessageBox.Show("Something unexpected went wrong:\n\n" + e.Exception.Message +
                            "\n\nYour queue has been kept. Details were written to the log file (Help > Open log folder).",
                AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            _log?.Error("Unhandled error.", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            _log?.Error("Unobserved task error.", e.Exception);
            e.SetObserved();
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
