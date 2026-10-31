using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using BulkPrint.Core.Infrastructure;
using BulkPrint.Core.Models;
using BulkPrint.Core.Services;

namespace BulkPrint.Core.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly IUiDispatcher _ui;
    private readonly IPrinterService _printers;
    private readonly IDocumentInspector _inspector;
    private readonly IPreviewService _preview;
    private readonly PrintEngine _engine;
    private readonly SettingsStore _store;
    private readonly IAppLogger _log;
    private readonly AppSettings _settings;
    private readonly List<PrintProfile> _profiles;

    private CancellationTokenSource _inspectCts = new();
    private CancellationTokenSource? _batchCts;
    private CancellationTokenSource? _thumbCts;
    private CancellationTokenSource? _saveCts;
    private CancellationTokenSource? _capsCts;
    private IReadOnlyList<DocumentItem> _selectedItems = Array.Empty<DocumentItem>();
    private DocumentItem? _selectedDocument;
    private object? _thumbnail;
    private string _thumbnailMessage = "Select a document to see a preview.";
    private bool _isPrinting, _isPaused, _isImporting, _supportsDuplex, _supportsColor, _printerHasProblem;
    private double _progressPercent;
    private string _progressText = "Ready";
    private string _etaText = "";
    private string _statusMessage = "Ready";
    private string _printerStatusText = "";
    private string _queueSummary = "No documents";
    private string? _globalRangeError;
    private BatchReport? _lastReport;
    private bool _initialized;

    public MainViewModel(
        IDialogService dialogs,
        IUiDispatcher ui,
        IPrinterService printers,
        IDocumentInspector inspector,
        IPreviewService preview,
        PrintEngine engine,
        SettingsStore store,
        IAppLogger log)
    {
        _dialogs = dialogs;
        _ui = ui;
        _printers = printers;
        _inspector = inspector;
        _preview = preview;
        _engine = engine;
        _store = store;
        _log = log;

        _settings = store.LoadSettings();
        _profiles = store.LoadProfiles();
        Global = _settings.Global;
        Global.PropertyChanged += OnGlobalChanged;
        Documents.CollectionChanged += OnDocumentsChanged;

        _engine.Gate.Changed += paused => _ui.Post(() => IsPaused = paused);
        _engine.AutoPaused += message => _ui.Post(() =>
        {
            IsPaused = true;
            StatusMessage = message;
            if (!PrintWithoutPopups) _dialogs.Warning("Printing paused", message);
        });
        _engine.Waiting += message => _ui.Post(() => StatusMessage = message);

        void OnError(Exception ex)
        {
            _log.Error("Command failed.", ex);
            _dialogs.Error("Something went wrong", PrintEngine.FriendlyError(ex));
        }

        AddFilesCommand = new AsyncRelayCommand(AddFilesAsync, () => CanEditQueue, OnError);
        AddFolderCommand = new AsyncRelayCommand(AddFolderAsync, () => CanEditQueue, OnError);
        RemoveSelectedCommand = new RelayCommand(RemoveSelected, () => CanEditQueue && _selectedItems.Count > 0);
        RemoveDuplicatesCommand = new RelayCommand(RemoveDuplicates, () => CanEditQueue && Documents.Count > 1);
        ClearQueueCommand = new RelayCommand(ClearQueue, () => CanEditQueue && Documents.Count > 0);
        SortCommand = new RelayCommand(p => Sort(p), _ => CanEditQueue && Documents.Count > 1);
        MoveUpCommand = new RelayCommand(() => Shift(-1), () => CanEditQueue && _selectedItems.Count > 0);
        MoveDownCommand = new RelayCommand(() => Shift(1), () => CanEditQueue && _selectedItems.Count > 0);

        PrintAllCommand = new AsyncRelayCommand(() => PrintAllAsync(false), () => !IsPrinting && Documents.Count > 0, OnError);
        PauseCommand = new RelayCommand(() => _engine.Gate.Pause(), () => IsPrinting && !IsPaused);
        ResumeCommand = new RelayCommand(Resume, () => IsPrinting && IsPaused);
        CancelCommand = new RelayCommand(CancelBatch, () => IsPrinting);
        SkipSelectedCommand = new RelayCommand(SkipSelected, () => _selectedItems.Any(i => i.Status == PrintStatus.Pending || i.IsBusy));
        RetryFailedCommand = new RelayCommand(RetryFailed, () => !IsPrinting && Documents.Any(d => d.Status == PrintStatus.Failed));
        ResetStatusesCommand = new RelayCommand(ResetStatuses, () => !IsPrinting && Documents.Any(d => d.Status is PrintStatus.Printed or PrintStatus.Failed or PrintStatus.Skipped));
        UnlockCommand = new AsyncRelayCommand(() => UnlockAsync(Documents.Where(d => d.Status == PrintStatus.NeedsPassword).ToList()),
            () => !IsPrinting && Documents.Any(d => d.Status == PrintStatus.NeedsPassword), OnError);

        RefreshPrintersCommand = new AsyncRelayCommand(() => LoadPrintersAsync(keepSelection: true), () => !IsPrinting, OnError);
        UseDefaultPrinterCommand = new RelayCommand(UseDefaultPrinter, () => !IsPrinting);
        EditSelectedSettingsCommand = new RelayCommand(EditSelectedSettings, () => !IsPrinting && _selectedItems.Count > 0);
        ResetSelectedSettingsCommand = new RelayCommand(ResetSelectedSettings, () => !IsPrinting && _selectedItems.Any(i => i.HasCustomSettings));
        ApplyGlobalToSelectedCommand = new RelayCommand(ApplyGlobalToSelected, () => !IsPrinting && _selectedItems.Count > 0);
        PreviewCommand = new RelayCommand(PreviewSelected, () => _selectedDocument is not null);

        SaveProfileCommand = new RelayCommand(SaveProfile, () => !IsPrinting);
        LoadProfileCommand = new AsyncRelayCommand(LoadProfileAsync, () => !IsPrinting && _profiles.Count > 0, OnError);
        SaveQueueCommand = new RelayCommand(SaveQueue, () => Documents.Count > 0);
        OpenQueueCommand = new AsyncRelayCommand(OpenQueueAsync, () => CanEditQueue, OnError);
        ShowLastReportCommand = new RelayCommand(() => { if (_lastReport is not null) ShowReport(_lastReport); }, () => _lastReport is not null && !IsPrinting);
        BrowseOutputFolderCommand = new RelayCommand(BrowseOutputFolder, () => !IsPrinting);
        OpenOutputFolderCommand = new RelayCommand(() => _dialogs.OpenFolder(Global.EffectiveOutputFolder));
        AboutCommand = new RelayCommand(() => _dialogs.ShowAbout());
        OpenLogFolderCommand = new RelayCommand(() => _dialogs.OpenFolder(_store.Paths.LogDir));
    }

    // ----- Identity -----------------------------------------------------------------------
    public string AppName => AppInfo.Name;
    public string Tagline => AppInfo.Tagline;
    public string DeveloperCredit => AppInfo.DeveloperCredit;

    // ----- Collections and settings -------------------------------------------------------
    public BulkObservableCollection<DocumentItem> Documents { get; } = new();
    public GlobalPrintSettings Global { get; }
    public ObservableCollection<string> Printers { get; } = new();
    public ObservableCollection<string> PaperSizes { get; } = new();
    public ObservableCollection<string> PaperSources { get; } = new();

    public IReadOnlyList<Choice<PageSelectionMode>> PageSelectionChoices { get; } = Choices<PageSelectionMode>(EnumLabels.Of);
    public IReadOnlyList<Choice<OrientationMode>> OrientationChoices { get; } = Choices<OrientationMode>(EnumLabels.Of);
    public IReadOnlyList<Choice<DuplexMode>> DuplexChoices { get; } = Choices<DuplexMode>(EnumLabels.Of);
    public IReadOnlyList<Choice<ColorMode>> ColorChoices { get; } = Choices<ColorMode>(EnumLabels.Of);
    public IReadOnlyList<Choice<ScalingMode>> ScalingChoices { get; } = Choices<ScalingMode>(EnumLabels.Of);
    public IReadOnlyList<Choice<PrintQuality>> QualityChoices { get; } = Choices<PrintQuality>(EnumLabels.Of);
    public IReadOnlyList<Choice<SeparatorMode>> SeparatorChoices { get; } = Choices<SeparatorMode>(EnumLabels.Of);
    public IReadOnlyList<Choice<SortMode>> SortChoices { get; } = Choices<SortMode>(EnumLabels.Of);
    public IReadOnlyList<Choice<bool>> OnOffChoices { get; } = new[] { new Choice<bool>(true, "On"), new Choice<bool>(false, "Off") };

    public static IReadOnlyList<Choice<T>> Choices<T>(Func<T, string> label) where T : struct, Enum =>
        Enum.GetValues<T>().Select(v => new Choice<T>(v, label(v))).ToList();

    public string? SelectedPrinter
    {
        get => Global.PrinterName;
        set
        {
            if (value is null || value == Global.PrinterName) return;
            Global.PrinterName = value;
            OnPropertyChanged();
            _ = LoadCapabilitiesAsync(value);
        }
    }

    public bool SupportsDuplex
    {
        get => _supportsDuplex;
        private set
        {
            if (Set(ref _supportsDuplex, value)) OnPropertyChanged(nameof(DuplexTip));
        }
    }

    public bool SupportsColor
    {
        get => _supportsColor;
        private set
        {
            if (Set(ref _supportsColor, value)) OnPropertyChanged(nameof(ColorTip));
        }
    }

    public string? DuplexTip => SupportsDuplex ? null : "The selected printer does not support double-sided printing.";
    public string? ColorTip => SupportsColor ? null : "The selected printer prints in black and white only.";
    public string PrinterStatusText { get => _printerStatusText; private set => Set(ref _printerStatusText, value); }
    public bool PrinterHasProblem { get => _printerHasProblem; private set => Set(ref _printerHasProblem, value); }

    public bool IncludeSubfolders
    {
        get => _settings.IncludeSubfolders;
        set
        {
            if (_settings.IncludeSubfolders == value) return;
            _settings.IncludeSubfolders = value;
            OnPropertyChanged();
            SaveSettings();
        }
    }

    public bool DarkTheme
    {
        get => _settings.DarkTheme;
        set
        {
            if (_settings.DarkTheme == value) return;
            _settings.DarkTheme = value;
            OnPropertyChanged();
            ThemeChanged?.Invoke(value);
            SaveSettings();
        }
    }

    public event Action<bool>? ThemeChanged;

    /// <summary>Print without pop-ups: the whole batch runs in one go with no confirmation, questions or report window.</summary>
    public bool PrintWithoutPopups
    {
        get => _settings.PrintWithoutPopups;
        set
        {
            if (_settings.PrintWithoutPopups == value) return;
            _settings.PrintWithoutPopups = value;
            OnPropertyChanged();
            SaveSettings();
        }
    }

    private string? _fileOutputExtension;

    /// <summary>True when the selected printer writes files (for example Microsoft Print to PDF).</summary>
    public bool PrinterSavesToFile => _fileOutputExtension is not null;

    public string SaveOutputHint => PrinterSavesToFile
        ? Global.SaveOutput
            ? $"On: each document is saved automatically as its own {_fileOutputExtension!.TrimStart('.').ToUpperInvariant()} file. Windows will not ask for file names."
            : "Off: Windows will ask \"Save Print Output As\" for every document."
        : "Used with printers that save files, such as Microsoft Print to PDF. Paper printers are not affected.";

    private void BrowseOutputFolder()
    {
        var folder = _dialogs.PickFolder(Global.EffectiveOutputFolder, out _, false);
        if (folder is null) return;
        Global.OutputFolder = folder;
        StatusMessage = $"Print output will be saved in {folder}";
    }

    public string? GlobalRangeError { get => _globalRangeError; private set => Set(ref _globalRangeError, value); }

    // ----- Selection & preview ------------------------------------------------------------
    public IReadOnlyList<DocumentItem> SelectedItems => _selectedItems;

    public void SetSelection(IEnumerable<DocumentItem> items)
    {
        _selectedItems = items.ToList();
        OnPropertyChanged(nameof(SelectedItems));
        OnPropertyChanged(nameof(SelectionSummary));
        RefreshCommands();
    }

    public string SelectionSummary => _selectedItems.Count switch
    {
        0 => "",
        1 => _selectedItems[0].SettingsSummary,
        _ => $"{_selectedItems.Count} documents selected",
    };

    public DocumentItem? SelectedDocument
    {
        get => _selectedDocument;
        set
        {
            if (Set(ref _selectedDocument, value))
            {
                RefreshThumbnail();
                RefreshCommands();
            }
        }
    }

    public object? Thumbnail { get => _thumbnail; private set => Set(ref _thumbnail, value); }
    public string ThumbnailMessage { get => _thumbnailMessage; private set => Set(ref _thumbnailMessage, value); }

    // ----- Progress -----------------------------------------------------------------------
    public bool IsPrinting
    {
        get => _isPrinting;
        private set
        {
            if (Set(ref _isPrinting, value))
            {
                OnPropertyChanged(nameof(CanEditQueue));
                OnPropertyChanged(nameof(CanEditSettings));
                RefreshCommands();
            }
        }
    }

    public bool IsPaused
    {
        get => _isPaused;
        private set
        {
            if (Set(ref _isPaused, value)) RefreshCommands();
        }
    }

    public bool IsImporting
    {
        get => _isImporting;
        private set
        {
            if (Set(ref _isImporting, value))
            {
                OnPropertyChanged(nameof(CanEditQueue));
                RefreshCommands();
            }
        }
    }

    public bool CanEditQueue => !IsPrinting && !IsImporting;
    public bool CanEditSettings => !IsPrinting;
    public bool IsQueueEmpty => Documents.Count == 0;
    public double ProgressPercent { get => _progressPercent; private set => Set(ref _progressPercent, value); }
    public string ProgressText { get => _progressText; private set => Set(ref _progressText, value); }
    public string EtaText { get => _etaText; private set => Set(ref _etaText, value); }
    public string StatusMessage { get => _statusMessage; set => Set(ref _statusMessage, value); }
    public string QueueSummary { get => _queueSummary; private set => Set(ref _queueSummary, value); }

    // ----- Commands -----------------------------------------------------------------------
    public AsyncRelayCommand AddFilesCommand { get; }
    public AsyncRelayCommand AddFolderCommand { get; }
    public RelayCommand RemoveSelectedCommand { get; }
    public RelayCommand RemoveDuplicatesCommand { get; }
    public RelayCommand ClearQueueCommand { get; }
    public RelayCommand SortCommand { get; }
    public RelayCommand MoveUpCommand { get; }
    public RelayCommand MoveDownCommand { get; }
    public AsyncRelayCommand PrintAllCommand { get; }
    public RelayCommand PauseCommand { get; }
    public RelayCommand ResumeCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand SkipSelectedCommand { get; }
    public RelayCommand RetryFailedCommand { get; }
    public RelayCommand ResetStatusesCommand { get; }
    public AsyncRelayCommand UnlockCommand { get; }
    public AsyncRelayCommand RefreshPrintersCommand { get; }
    public RelayCommand UseDefaultPrinterCommand { get; }
    public RelayCommand EditSelectedSettingsCommand { get; }
    public RelayCommand ResetSelectedSettingsCommand { get; }
    public RelayCommand ApplyGlobalToSelectedCommand { get; }
    public RelayCommand PreviewCommand { get; }
    public RelayCommand SaveProfileCommand { get; }
    public AsyncRelayCommand LoadProfileCommand { get; }
    public RelayCommand SaveQueueCommand { get; }
    public AsyncRelayCommand OpenQueueCommand { get; }
    public RelayCommand ShowLastReportCommand { get; }
    public RelayCommand BrowseOutputFolderCommand { get; }
    public RelayCommand OpenOutputFolderCommand { get; }
    public RelayCommand AboutCommand { get; }
    public RelayCommand OpenLogFolderCommand { get; }

    public void RefreshCommands()
    {
        foreach (var c in new object[]
        {
            AddFilesCommand, AddFolderCommand, RemoveSelectedCommand, RemoveDuplicatesCommand, ClearQueueCommand,
            SortCommand, MoveUpCommand, MoveDownCommand, PrintAllCommand, PauseCommand, ResumeCommand, CancelCommand,
            SkipSelectedCommand, RetryFailedCommand, ResetStatusesCommand, UnlockCommand, RefreshPrintersCommand,
            UseDefaultPrinterCommand, EditSelectedSettingsCommand, ResetSelectedSettingsCommand, ApplyGlobalToSelectedCommand,
            PreviewCommand, SaveProfileCommand, LoadProfileCommand, SaveQueueCommand, OpenQueueCommand, ShowLastReportCommand,
        })
        {
            switch (c)
            {
                case RelayCommand r: r.RaiseCanExecuteChanged(); break;
                case AsyncRelayCommand a: a.RaiseCanExecuteChanged(); break;
            }
        }
    }

    // ----- Start-up -----------------------------------------------------------------------
    public async Task InitializeAsync(IReadOnlyList<string> startupPaths)
    {
        await LoadPrintersAsync(keepSelection: true);
        _initialized = true;
        ValidateGlobal();

        var session = _store.LoadSession();
        bool resume = false;
        if (session is { Items.Count: > 0 })
        {
            var items = QueueOperations.FromProject(session, keepStatuses: true);
            int pending = items.Count(i => i.Status == PrintStatus.Pending);
            if (session.BatchInProgress && pending > 0)
            {
                resume = _dialogs.Confirm("Resume unfinished batch?",
                    $"BulkPrint Manager was closed before the last batch finished.\n\n" +
                    $"{pending} of {items.Count} documents have not been printed yet.\n\n" +
                    "Resume printing them now?\n(Choose No to keep the queue and print later.)");
            }
            AddItems(items);
            StatusMessage = $"Restored your queue of {items.Count} documents from the last session.";
            await InspectAsync(items.Where(i => i.Status == PrintStatus.Pending).ToList(), promptForPasswords: false);
        }

        if (startupPaths.Count > 0) await AddPathsAsync(startupPaths);

        if (resume) await PrintAllAsync(skipConfirmation: true);
    }

    // ----- Printers -----------------------------------------------------------------------
    private async Task LoadPrintersAsync(bool keepSelection)
    {
        StatusMessage = "Looking for printers…";
        var (names, defaultName) = await Task.Run(() =>
        {
            try { return (_printers.GetPrinterNames(), _printers.GetDefaultPrinterName()); }
            catch (Exception ex)
            {
                _log.Error("Could not list printers.", ex);
                return ((IReadOnlyList<string>)Array.Empty<string>(), (string?)null);
            }
        });

        var previous = Global.PrinterName;
        Printers.Clear();
        foreach (var n in names) Printers.Add(n);

        string? choose = keepSelection && previous is not null && names.Contains(previous) ? previous
            : defaultName is not null && names.Contains(defaultName) ? defaultName
            : names.FirstOrDefault();

        if (choose is null)
        {
            Global.PrinterName = null;
            OnPropertyChanged(nameof(SelectedPrinter));
            PrinterStatusText = "No printers are installed. Add a printer in Windows Settings > Bluetooth & devices > Printers & scanners.";
            PrinterHasProblem = true;
            StatusMessage = "No printers found";
            return;
        }

        Global.PrinterName = choose;
        OnPropertyChanged(nameof(SelectedPrinter));
        await LoadCapabilitiesAsync(choose);
        StatusMessage = names.Count == 1 ? "1 printer found" : $"{names.Count} printers found";
    }

    private void UseDefaultPrinter()
    {
        var name = _printers.GetDefaultPrinterName();
        if (name is null)
        {
            _dialogs.Warning("No default printer", "Windows does not have a default printer set.");
            return;
        }
        if (!Printers.Contains(name)) Printers.Add(name);
        SelectedPrinter = name;
    }

    private async Task LoadCapabilitiesAsync(string printerName)
    {
        _capsCts?.Cancel();
        var cts = _capsCts = new CancellationTokenSource();
        PrinterStatusText = "Reading printer settings…";
        var (caps, status, extension) = await Task.Run(() =>
        {
            try { return (_printers.GetCapabilities(printerName), _printers.GetStatus(printerName), _printers.GetFileOutputExtension(printerName)); }
            catch (Exception ex)
            {
                _log.Error($"Could not read printer '{printerName}'.", ex);
                return (PrinterCapabilities.None, new PrinterStatusInfo(false, true, "This printer could not be read", 0), (string?)null);
            }
        });
        if (cts.IsCancellationRequested) return;
        _fileOutputExtension = extension;
        OnPropertyChanged(nameof(PrinterSavesToFile));
        OnPropertyChanged(nameof(SaveOutputHint));

        var paper = Global.PaperSizeName;
        var source = Global.PaperSourceName;
        PaperSizes.Clear();
        foreach (var p in caps.PaperSizes) PaperSizes.Add(p);
        PaperSources.Clear();
        foreach (var s in caps.PaperSources) PaperSources.Add(s);

        Global.PaperSizeName = PickExisting(caps.PaperSizes, paper, caps.DefaultPaperSize, "A4", "Letter");
        Global.PaperSourceName = PickExisting(caps.PaperSources, source, caps.DefaultPaperSource);
        SupportsDuplex = caps.SupportsDuplex;
        SupportsColor = caps.SupportsColor;
        if (!caps.SupportsDuplex) Global.Duplex = DuplexMode.Simplex;
        if (!caps.SupportsColor && caps.IsValid) Global.Color = ColorMode.BlackAndWhite;
        ApplyStatus(status);
        RefreshThumbnail();
    }

    private static string? PickExisting(IReadOnlyList<string> available, params string?[] preferred)
    {
        foreach (var p in preferred)
        {
            if (p is null) continue;
            var match = available.FirstOrDefault(a => string.Equals(a, p, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }
        return available.FirstOrDefault();
    }

    private void ApplyStatus(PrinterStatusInfo status)
    {
        PrinterHasProblem = status.HasProblem;
        PrinterStatusText = status.JobsInQueue > 0 ? $"{status.Text} · {status.JobsInQueue} job(s) waiting" : status.Text;
    }

    /// <summary>Called by the window every few seconds to refresh the printer status line.</summary>
    public async Task RefreshPrinterStatusAsync()
    {
        var name = Global.PrinterName;
        if (name is null || !_initialized) return;
        var status = await Task.Run(() =>
        {
            try { return _printers.GetStatus(name); }
            catch { return PrinterStatusInfo.Unknown(); }
        });
        if (name == Global.PrinterName) ApplyStatus(status);
    }

    // ----- Adding documents ---------------------------------------------------------------
    private async Task AddFilesAsync()
    {
        var files = _dialogs.PickFiles(FileTypes.OpenDialogFilter, _settings.LastFolder);
        if (files is null || files.Count == 0) return;
        _settings.LastFolder = Path.GetDirectoryName(files[0]);
        SaveSettings();
        await AddPathsAsync(files);
    }

    private async Task AddFolderAsync()
    {
        var folder = _dialogs.PickFolder(_settings.LastFolder, out bool subfolders, IncludeSubfolders);
        if (folder is null) return;
        IncludeSubfolders = subfolders;
        _settings.LastFolder = folder;
        SaveSettings();
        await AddPathsAsync(new[] { folder });
    }

    /// <summary>Adds files and folders (from dialogs, drag and drop, the command line or Explorer's menu).</summary>
    public async Task AddPathsAsync(IEnumerable<string> paths)
    {
        var list = paths.ToList();
        if (list.Count == 0) return;
        if (IsPrinting)
        {
            _dialogs.Info("Printing in progress", "Documents can be added after the current batch finishes.");
            return;
        }

        IsImporting = true;
        StatusMessage = "Adding documents…";
        try
        {
            bool subfolders = IncludeSubfolders;
            var result = await Task.Run(() => QueueOperations.Expand(list, subfolders));
            AddItems(result.Items);

            var msg = result.Items.Count == 1 ? "Added 1 document." : $"Added {result.Items.Count} documents.";
            if (result.UnsupportedCount > 0)
                msg += $" Skipped {result.UnsupportedCount} unsupported file{(result.UnsupportedCount == 1 ? "" : "s")}" +
                       (result.UnsupportedExamples.Count > 0 ? $" (for example {string.Join(", ", result.UnsupportedExamples.Take(3))})." : ".");
            if (result.UnreadableCount > 0)
                msg += $" {result.UnreadableCount} item{(result.UnreadableCount == 1 ? "" : "s")} could not be read.";
            StatusMessage = msg;
            _log.Info(msg);

            if (result.Items.Count == 0 && result.UnsupportedCount > 0)
                _dialogs.Info("No supported documents",
                    "None of the selected files can be printed by BulkPrint Manager.\n\n" +
                    "Supported formats: PDF, DOC, DOCX, RTF, XLS, XLSX, CSV, PPT, PPTX, JPG, JPEG, PNG, BMP, TIFF and TXT.");

            ShowOfficeNoticeIfNeeded(result.Items);
            IsImporting = false;
            await InspectAsync(result.Items, promptForPasswords: true);
        }
        finally
        {
            IsImporting = false;
        }
    }

    private void AddItems(IEnumerable<DocumentItem> items)
    {
        var list = items.ToList();
        foreach (var i in list) i.AttachGlobal(Global);
        Documents.AddRange(list);
    }

    private void ShowOfficeNoticeIfNeeded(IReadOnlyList<DocumentItem> items)
    {
        int office = items.Count(i => FileTypes.NeedsOfficeEngine(i.Kind));
        if (office == 0) return;
        if (!_inspector.OfficeEngineAvailable && PrintWithoutPopups)
        {
            StatusMessage = $"{office} Word, Excel or PowerPoint file(s) cannot be printed because the Office engine is not installed.";
            return;
        }
        if (!_inspector.OfficeEngineAvailable)
        {
            _dialogs.Warning("Office documents cannot be printed",
                $"{office} Word, Excel or PowerPoint file{(office == 1 ? " was" : "s were")} added, but the built-in Office " +
                "conversion engine is not installed with this copy of BulkPrint Manager.\n\n" +
                "These files are marked \"Cannot print\" so that nothing is printed incorrectly. " +
                "Reinstall BulkPrint Manager using the full installer to print Office documents.");
            return;
        }
        if (_settings.OfficeNoticeShown || PrintWithoutPopups) return;
        _settings.OfficeNoticeShown = true;
        SaveSettings();
        _dialogs.Info("About Office documents",
            "Word, Excel and PowerPoint files are converted to PDF by BulkPrint Manager's built-in Office engine " +
            "(based on LibreOffice) just before they are printed. No copy of Microsoft Office is needed.\n\n" +
            "Most documents print as expected. Complex layouts, uncommon fonts, macros or embedded objects can look " +
            "slightly different from Microsoft Office. Use Preview to check important documents before printing.");
    }

    private async Task InspectAsync(IReadOnlyList<DocumentItem> items, bool promptForPasswords)
    {
        if (items.Count == 0)
        {
            UpdateSummary();
            return;
        }
        var ct = _inspectCts.Token;
        var global = Global;
        int done = 0;
        try
        {
            await Parallel.ForEachAsync(items, new ParallelOptions { MaxDegreeOfParallelism = 2, CancellationToken = ct }, async (item, token) =>
            {
                try
                {
                    if (item.Status == PrintStatus.Pending)
                        await _inspector.InspectAsync(item, global, token);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _log.Warn($"Could not inspect '{item.Path}': {ex.Message}");
                }
                int n = Interlocked.Increment(ref done);
                if (n % 25 == 0 || n == items.Count)
                    _ui.Post(() =>
                    {
                        if (!IsPrinting) StatusMessage = $"Reading documents… {n} of {items.Count}";
                    });
            });
        }
        catch (OperationCanceledException)
        {
            return;
        }

        UpdateSummary();
        if (!IsPrinting) StatusMessage = "Ready";
        ScheduleSave();
        RefreshCommands();

        var locked = items.Where(i => i.Status == PrintStatus.NeedsPassword).ToList();
        if (promptForPasswords && locked.Count > 0 && !IsPrinting && PrintWithoutPopups)
        {
            StatusMessage = $"{locked.Count} password-protected PDF{(locked.Count == 1 ? "" : "s")} will be skipped. " +
                            "To print them, use Settings > Unlock password-protected files.";
        }
        else if (promptForPasswords && locked.Count > 0 && !IsPrinting)
        {
            if (_dialogs.Confirm("Password-protected documents",
                    $"{locked.Count} PDF document{(locked.Count == 1 ? " is" : "s are")} password-protected.\n\nEnter the password now?\n" +
                    "(You can also do this later from Settings > Unlock password-protected files.)"))
                await UnlockAsync(locked);
        }
        RefreshThumbnail();
    }

    private async Task UnlockAsync(IReadOnlyList<DocumentItem> items)
    {
        var remaining = items.Where(i => i.Status == PrintStatus.NeedsPassword).ToList();
        string? shared = null;
        int unlocked = 0;
        for (int idx = 0; idx < remaining.Count; idx++)
        {
            var item = remaining[idx];
            if (item.Status != PrintStatus.NeedsPassword) continue;
            if (shared is not null && await _inspector.TryUnlockAsync(item, shared, CancellationToken.None))
            {
                unlocked++;
                continue;
            }

            bool failed = false;
            while (true)
            {
                var answer = _dialogs.AskPassword(item.FileName, remaining.Count - idx - 1, failed);
                if (answer.SkipAll)
                {
                    FinishUnlock(unlocked);
                    return;
                }
                if (string.IsNullOrEmpty(answer.Password)) break;
                if (await _inspector.TryUnlockAsync(item, answer.Password, CancellationToken.None))
                {
                    unlocked++;
                    if (answer.ApplyToAll) shared = answer.Password;
                    break;
                }
                failed = true;
            }
        }
        FinishUnlock(unlocked);
    }

    private void FinishUnlock(int unlocked)
    {
        int stillLocked = Documents.Count(d => d.Status == PrintStatus.NeedsPassword);
        StatusMessage = stillLocked == 0
            ? $"Unlocked {unlocked} document{(unlocked == 1 ? "" : "s")}."
            : $"Unlocked {unlocked}. {stillLocked} document{(stillLocked == 1 ? " still needs" : "s still need")} a password and will be skipped.";
        UpdateSummary();
        RefreshCommands();
        RefreshThumbnail();
    }

    // ----- Queue editing ------------------------------------------------------------------
    private void RemoveSelected()
    {
        var sel = _selectedItems.ToList();
        Documents.RemoveRange(sel);
        SetSelection(Array.Empty<DocumentItem>());
        StatusMessage = $"Removed {sel.Count} document{(sel.Count == 1 ? "" : "s")}.";
    }

    private void RemoveDuplicates()
    {
        var dups = QueueOperations.FindDuplicates(Documents);
        if (dups.Count == 0)
        {
            StatusMessage = "No duplicate documents found.";
            return;
        }
        Documents.RemoveRange(dups);
        StatusMessage = $"Removed {dups.Count} duplicate entr{(dups.Count == 1 ? "y" : "ies")}.";
    }

    private void ClearQueue()
    {
        if (!_dialogs.Confirm("Clear the print queue", $"Remove all {Documents.Count} documents from the queue?\n\nYour files are not deleted.")) return;
        _inspectCts.Cancel();
        _inspectCts = new CancellationTokenSource();
        Documents.ReplaceAll(Array.Empty<DocumentItem>());
        SetSelection(Array.Empty<DocumentItem>());
        _inspector.CleanUp();
        StatusMessage = "Queue cleared.";
    }

    private void Sort(object? parameter)
    {
        SortMode mode = parameter switch
        {
            SortMode m => m,
            Choice<SortMode> c => c.Value,
            string s when Enum.TryParse<SortMode>(s, out var parsed) => parsed,
            _ => SortMode.NameAscending,
        };
        Documents.ReplaceAll(QueueOperations.Sort(Documents.ToList(), mode));
        StatusMessage = $"Sorted by {EnumLabels.Of(mode).ToLowerInvariant()}.";
    }

    private void Shift(int direction)
    {
        var selected = _selectedItems.ToList();
        Documents.ReplaceAll(QueueOperations.Shift(Documents.ToList(), selected, direction));
        SelectionRestoreRequested?.Invoke(selected);
    }

    /// <summary>Drag-and-drop reordering from the queue list.</summary>
    public void MoveSelectedTo(IReadOnlyCollection<DocumentItem> moving, int targetIndex)
    {
        if (!CanEditQueue || moving.Count == 0) return;
        Documents.ReplaceAll(QueueOperations.MoveTo(Documents.ToList(), moving, targetIndex));
        SelectionRestoreRequested?.Invoke(moving.ToList());
    }

    /// <summary>Asks the view to reselect these items after the list was rebuilt.</summary>
    public event Action<IReadOnlyList<DocumentItem>>? SelectionRestoreRequested;

    private void OnDocumentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsQueueEmpty));
        UpdateSummary();
        RefreshCommands();
        ScheduleSave();
    }

    private void UpdateSummary()
    {
        int count = Documents.Count;
        if (count == 0)
        {
            QueueSummary = "No documents";
            return;
        }
        int known = 0, pages = 0;
        foreach (var d in Documents)
        {
            if (d.PageCount is int p) { known++; pages += p; }
        }
        string pageText = known == count ? $"{pages:N0} pages" : known == 0 ? "pages counted when printed" : $"{pages:N0}+ pages";
        int printed = Documents.Count(d => d.Status == PrintStatus.Printed);
        int failed = Documents.Count(d => d.Status == PrintStatus.Failed);
        var parts = new List<string> { $"{count:N0} document{(count == 1 ? "" : "s")}", pageText };
        if (printed > 0) parts.Add($"{printed:N0} printed");
        if (failed > 0) parts.Add($"{failed:N0} failed");
        QueueSummary = string.Join(" · ", parts);
    }

    // ----- Per-document settings ----------------------------------------------------------
    private void EditSelectedSettings()
    {
        var sel = _selectedItems.ToList();
        if (sel.Count == 0) return;
        var initial = sel[0].EffectiveOptions.CloneOptions();
        var result = _dialogs.EditDocumentSettings(sel.Count, initial, SupportsDuplex, SupportsColor);
        if (result is null) return;
        foreach (var item in sel)
        {
            if (result.ResetToGlobal || result.Options is null || result.Options.SameOptionsAs(Global))
                item.Overrides = null;
            else
                item.Overrides = result.Options.CloneOptions();
        }
        AfterOverridesChanged(sel.Count);
    }

    private void ResetSelectedSettings()
    {
        var sel = _selectedItems.ToList();
        foreach (var item in sel) item.Overrides = null;
        AfterOverridesChanged(sel.Count);
    }

    private void ApplyGlobalToSelected() => ResetSelectedSettings();

    private void AfterOverridesChanged(int count)
    {
        StatusMessage = $"Updated settings for {count} document{(count == 1 ? "" : "s")}.";
        OnPropertyChanged(nameof(SelectionSummary));
        RefreshThumbnail();
        RefreshCommands();
        ScheduleSave();
    }

    /// <summary>Called by the view after a copies value is edited directly in the list.</summary>
    public void OnItemEdited(DocumentItem item)
    {
        if (item.Overrides is not null && item.Overrides.SameOptionsAs(Global)) item.Overrides = null;
        OnPropertyChanged(nameof(SelectionSummary));
        RefreshCommands();
        ScheduleSave();
    }

    private void PreviewSelected()
    {
        var item = _selectedDocument;
        if (item is null) return;
        _dialogs.ShowPreview(item, item.EffectiveOptions.CloneOptions(), Global.CloneGlobal());
        RefreshThumbnail();
    }

    private void RefreshThumbnail()
    {
        _thumbCts?.Cancel();
        var item = _selectedDocument;
        if (item is null || !_initialized)
        {
            Thumbnail = null;
            ThumbnailMessage = "Select a document to see a preview.";
            return;
        }
        var cts = _thumbCts = new CancellationTokenSource();
        var options = item.EffectiveOptions.CloneOptions();
        var global = Global.CloneGlobal();
        ThumbnailMessage = "Loading preview…";
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(200, cts.Token);
                var image = await _preview.RenderAsync(item, options, global, 1, 36, allowConversion: false, cts.Token);
                _ui.Post(() =>
                {
                    if (cts.IsCancellationRequested) return;
                    Thumbnail = image.Image;
                    ThumbnailMessage = image.Error ?? image.Description;
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _ui.Post(() =>
                {
                    if (cts.IsCancellationRequested) return;
                    Thumbnail = null;
                    ThumbnailMessage = "Preview not available: " + PrintEngine.FriendlyError(ex);
                });
            }
        });
    }

    // ----- Global settings ----------------------------------------------------------------
    private void OnGlobalChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GlobalPrintSettings.PrinterName)) OnPropertyChanged(nameof(SelectedPrinter));
        if (e.PropertyName == nameof(GlobalPrintSettings.SaveOutput)) OnPropertyChanged(nameof(SaveOutputHint));
        foreach (var d in Documents)
            if (!d.HasCustomSettings) d.NotifyEffectiveChanged();
        ValidateGlobal();
        if (e.PropertyName is not nameof(GlobalPrintSettings.DelayBetweenDocumentsSeconds)
            and not nameof(GlobalPrintSettings.MaxJobsInPrinterQueue)
            and not nameof(GlobalPrintSettings.Separator)
            and not nameof(GlobalPrintSettings.UsePrinterCopies)
            and not nameof(GlobalPrintSettings.SaveOutput)
            and not nameof(GlobalPrintSettings.OutputFolder))
            RefreshThumbnail();
        SaveSettings();
    }

    private void ValidateGlobal() => GlobalRangeError = Global.Validate();

    // ----- Profiles -----------------------------------------------------------------------
    private void SaveProfile()
    {
        var name = _dialogs.AskText("Save print profile",
            "Name for this profile (for example \"A4 Duplex B&W\"):",
            SuggestProfileName());
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        var existing = _profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (!_dialogs.Confirm("Replace profile", $"A profile called \"{existing.Name}\" already exists. Replace it?")) return;
            _profiles.Remove(existing);
        }
        _profiles.Add(new PrintProfile { Name = name, Settings = Global.CloneGlobal() });
        _profiles.Sort((a, b) => NaturalStringComparer.Instance.Compare(a.Name, b.Name));
        _store.SaveProfiles(_profiles);
        StatusMessage = $"Saved profile \"{name}\".";
        RefreshCommands();
    }

    private string SuggestProfileName()
    {
        var paper = Global.PaperSizeName ?? "Paper";
        if (paper.Length > 20) paper = paper[..20].Trim();
        var sides = Global.Duplex == DuplexMode.Simplex ? "Single-Sided" : "Duplex";
        var colour = Global.Color == ColorMode.Color ? "Colour" : "B&W";
        return $"{paper} {sides} {colour}";
    }

    private async Task LoadProfileAsync()
    {
        var choice = _dialogs.ChooseProfile(_profiles.Select(p => p.Name).ToList());
        if (choice is null) return;
        var profile = _profiles.FirstOrDefault(p => p.Name == choice.Name);
        if (profile is null) return;

        if (choice.Delete)
        {
            _profiles.Remove(profile);
            _store.SaveProfiles(_profiles);
            StatusMessage = $"Deleted profile \"{profile.Name}\".";
            RefreshCommands();
            return;
        }

        var currentPrinter = Global.PrinterName;
        Global.CopyGlobalFrom(profile.Settings);
        if (Global.PrinterName is null || !Printers.Contains(Global.PrinterName))
        {
            if (profile.Settings.PrinterName is not null)
                _dialogs.Warning("Printer not found",
                    $"The printer \"{profile.Settings.PrinterName}\" saved in this profile is not installed. The current printer is kept.");
            Global.PrinterName = currentPrinter;
        }
        OnPropertyChanged(nameof(SelectedPrinter));
        if (Global.PrinterName is not null) await LoadCapabilitiesAsync(Global.PrinterName);
        StatusMessage = $"Loaded profile \"{profile.Name}\".";
    }

    // ----- Saved queues -------------------------------------------------------------------
    private void SaveQueue()
    {
        var path = _dialogs.PickSaveFile(QueueProject.DialogFilter, $"Print queue {DateTime.Now:yyyy-MM-dd}{QueueProject.FileExtension}");
        if (path is null) return;
        JsonStore.Save(path, QueueOperations.ToProject(Documents, Global, batchInProgress: false));
        StatusMessage = $"Queue saved to {Path.GetFileName(path)}.";
    }

    private async Task OpenQueueAsync()
    {
        var path = _dialogs.PickOpenFile(QueueProject.DialogFilter);
        if (path is null) return;
        var project = JsonStore.Load<QueueProject>(path);
        if (project is null)
        {
            _dialogs.Error("Could not open queue", "This file is not a valid BulkPrint queue file.");
            return;
        }

        if (Documents.Count > 0)
        {
            var replace = _dialogs.AskYesNoCancel("Open queue",
                $"Replace the current queue with the {project.Items.Count} documents in this file?\n\n" +
                "Yes = replace the current queue\nNo = add them to the end of the current queue");
            if (replace is null) return;
            if (replace == true) Documents.ReplaceAll(Array.Empty<DocumentItem>());
        }

        var items = QueueOperations.FromProject(project, keepStatuses: false);
        if (project.Global is not null)
        {
            var printer = Global.PrinterName;
            Global.CopyGlobalFrom(project.Global);
            if (Global.PrinterName is null || !Printers.Contains(Global.PrinterName)) Global.PrinterName = printer;
            OnPropertyChanged(nameof(SelectedPrinter));
            if (Global.PrinterName is not null) await LoadCapabilitiesAsync(Global.PrinterName);
        }
        AddItems(items);
        int missing = items.Count(i => i.Status == PrintStatus.Failed);
        StatusMessage = $"Opened {items.Count} documents from {Path.GetFileName(path)}." +
                        (missing > 0 ? $" {missing} file{(missing == 1 ? " is" : "s are")} missing." : "");
        await InspectAsync(items.Where(i => i.Status == PrintStatus.Pending).ToList(), promptForPasswords: true);
    }

    // ----- Printing -----------------------------------------------------------------------
    private async Task PrintAllAsync(bool skipConfirmation)
    {
        if (string.IsNullOrEmpty(Global.PrinterName))
        {
            _dialogs.Warning("No printer selected", "Choose a printer in the Printer panel before printing.");
            return;
        }
        var globalError = Global.Validate();
        if (globalError is not null)
        {
            _dialogs.Warning("Check the page range", globalError);
            return;
        }
        var invalid = Documents.Where(d => d.Overrides?.Validate() is not null).ToList();
        if (invalid.Count > 0)
        {
            _dialogs.Warning("Check the page ranges",
                $"{invalid.Count} document{(invalid.Count == 1 ? " has" : "s have")} an invalid page range in custom settings, for example \"{invalid[0].FileName}\": " +
                invalid[0].Overrides!.Validate());
            return;
        }

        bool quiet = PrintWithoutPopups;
        var locked = Documents.Where(d => d.Status == PrintStatus.NeedsPassword).ToList();
        if (locked.Count > 0 && !skipConfirmation && !quiet)
        {
            var answer = _dialogs.AskYesNoCancel("Password-protected documents",
                $"{locked.Count} document{(locked.Count == 1 ? " needs" : "s need")} a password.\n\n" +
                "Yes = enter passwords now\nNo = skip these documents and print the rest");
            if (answer is null) return;
            if (answer == true) await UnlockAsync(locked);
        }

        var printable = Documents.Where(PrintEngine.IsPrintable).ToList();
        if (printable.Count == 0 && quiet)
        {
            StatusMessage = "Nothing to print: every document has already been printed, skipped or failed. Use Print > Reset statuses to print again.";
            return;
        }
        if (printable.Count == 0)
        {
            bool anyDone = Documents.Any(d => d.Status is PrintStatus.Printed or PrintStatus.Skipped or PrintStatus.Failed);
            _dialogs.Info("Nothing to print", anyDone
                ? "Every document in the queue has already been printed, skipped or failed.\n\nUse Print > Reset statuses to print them again, or Print > Retry failed documents."
                : "There are no documents that can be printed.");
            return;
        }

        var status = await Task.Run(() =>
        {
            try { return _printers.GetStatus(Global.PrinterName!); }
            catch { return PrinterStatusInfo.Unknown(); }
        });
        ApplyStatus(status);

        if (!skipConfirmation && !quiet)
        {
            int copies = printable.Sum(p => p.EffectiveOptions.Copies);
            var message = $"Print {printable.Count:N0} document{(printable.Count == 1 ? "" : "s")} ({copies:N0} cop{(copies == 1 ? "y" : "ies")} in total) to:\n\n{Global.PrinterName}";
            if (status.HasProblem) message += $"\n\nWarning: the printer reports \"{status.Text}\".";
            if (!_dialogs.Confirm("Start printing", message)) return;
        }

        _batchCts = new CancellationTokenSource();
        _engine.Gate.Resume();
        IsPaused = false;
        IsPrinting = true;
        ProgressPercent = 0;
        EtaText = "";
        StatusMessage = $"Printing to {Global.PrinterName}…";
        SaveSessionNow(batchInProgress: true);

        var progress = new Progress<BatchProgress>(UpdateProgress);
        BatchReport? report = null;
        try
        {
            report = await _engine.RunAsync(Documents.ToList(), Global, () => _ui.InvokeAsync(() => SaveSessionNow(batchInProgress: true)), progress, _batchCts.Token);
        }
        finally
        {
            IsPrinting = false;
            IsPaused = false;
            _batchCts?.Dispose();
            _batchCts = null;
            SaveSessionNow(batchInProgress: false);
            UpdateSummary();
            RefreshCommands();
        }

        _lastReport = report;
        ProgressText = report.Headline;
        EtaText = "";
        StatusMessage = report.Headline;
        if (!report.WasCancelled) ProgressPercent = 100;

        if (report.OutputFolder is not null)
        {
            // Keep a copy of the report next to the saved files.
            try
            {
                var csv = Path.Combine(report.OutputFolder, $"BulkPrint report {report.StartTime:yyyy-MM-dd HHmmss}.csv");
                report.SaveCsv(csv);
                StatusMessage = $"{report.Headline}. Files saved in {report.OutputFolder}";
            }
            catch (Exception ex)
            {
                _log.Warn("Could not save the report next to the output: " + ex.Message);
            }
        }

        if (quiet)
        {
            if (report.Failed > 0) StatusMessage += " · See Print > Show last report for the reasons.";
            RefreshCommands();
            return;
        }
        ShowReport(report);
    }

    private void ShowReport(BatchReport report)
    {
        if (_dialogs.ShowReport(report))
        {
            RetryFailed();
            _ = PrintAllAsync(skipConfirmation: false);
        }
    }

    private void UpdateProgress(BatchProgress p)
    {
        ProgressPercent = p.Total == 0 ? 0 : 100.0 * p.Completed / p.Total;
        var text = $"{p.Completed:N0} of {p.Total:N0} documents · {p.PagesSent:N0} pages sent";
        if (p.EstimatedTotalPages is int est && est > 0) text += $" of about {est:N0}";
        ProgressText = text;
        if (p.CurrentFile is not null && !IsPaused) StatusMessage = $"Printing {p.CurrentFile}";
        EtaText = p.Remaining is TimeSpan r
            ? r.TotalMinutes >= 60 ? $"About {(int)r.TotalHours} h {r.Minutes} min left"
            : r.TotalMinutes >= 1 ? $"About {Math.Ceiling(r.TotalMinutes):0} min left"
            : "Less than a minute left"
            : "";
    }

    private void Resume()
    {
        StatusMessage = "Resuming…";
        _engine.Gate.Resume();
    }

    private void CancelBatch()
    {
        if (!_dialogs.Confirm("Cancel printing", "Stop printing now?\n\nDocuments that have not been printed stay in the queue, so you can print them later.")) return;
        _batchCts?.Cancel();
        _engine.Gate.Resume();
        StatusMessage = "Cancelling…";
    }

    private void SkipSelected()
    {
        int n = 0;
        foreach (var item in _selectedItems)
        {
            if (item.IsBusy)
            {
                _engine.SkipCurrent();
                n++;
            }
            else if (item.Status == PrintStatus.Pending)
            {
                item.Status = PrintStatus.Skipped;
                item.StatusDetail = "Skipped by user";
                n++;
            }
        }
        StatusMessage = $"Skipped {n} document{(n == 1 ? "" : "s")}.";
        RefreshCommands();
        ScheduleSave();
    }

    private void RetryFailed()
    {
        int n = 0;
        foreach (var d in Documents.Where(d => d.Status == PrintStatus.Failed))
        {
            if (!File.Exists(d.Path)) continue;
            d.ResetForReprint();
            n++;
        }
        StatusMessage = $"{n} failed document{(n == 1 ? " is" : "s are")} ready to print again.";
        UpdateSummary();
        RefreshCommands();
        ScheduleSave();
    }

    private void ResetStatuses()
    {
        if (!_dialogs.Confirm("Reset statuses", "Mark every printed, skipped and failed document as Pending so the whole queue can be printed again?")) return;
        foreach (var d in Documents) d.ResetForReprint();
        ProgressPercent = 0;
        ProgressText = "Ready";
        StatusMessage = "All documents are ready to print again.";
        UpdateSummary();
        RefreshCommands();
        ScheduleSave();
    }

    // ----- Saving -------------------------------------------------------------------------
    private void SaveSettings()
    {
        try { _store.SaveSettings(_settings); }
        catch (Exception ex) { _log.Warn("Could not save settings: " + ex.Message); }
    }

    private void ScheduleSave()
    {
        if (!_initialized) return;
        _saveCts?.Cancel();
        var cts = _saveCts = new CancellationTokenSource();
        _ = Task.Delay(800, cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            _ui.Post(() =>
            {
                if (!cts.IsCancellationRequested) SaveSessionNow(IsPrinting);
            });
        }, TaskScheduler.Default);
    }

    /// <summary>Saves the queue so it survives a crash or restart. Must run on the UI thread.</summary>
    public void SaveSessionNow(bool batchInProgress)
    {
        try
        {
            if (Documents.Count == 0) _store.ClearSession();
            else _store.SaveSession(QueueOperations.ToProject(Documents, Global, batchInProgress));
        }
        catch (Exception ex)
        {
            _log.Warn("Could not save session: " + ex.Message);
        }
    }

    /// <summary>Returns false if the user does not want to close.</summary>
    public bool ConfirmClose()
    {
        if (!IsPrinting) return true;
        if (!_dialogs.Confirm("Printing in progress",
                "A batch is still printing.\n\nStop printing and close BulkPrint Manager?\n" +
                "The documents that are left will be offered for printing the next time you open the program.")) return false;
        _batchCts?.Cancel();
        _engine.Gate.Resume();
        return true;
    }

    public void Shutdown()
    {
        _inspectCts.Cancel();
        _thumbCts?.Cancel();
        _saveCts?.Cancel();
        SaveSettings();
        SaveSessionNow(batchInProgress: IsPrinting || Documents.Any(d => d.Status == PrintStatus.Pending) && _batchCts is not null);
        _inspector.CleanUp();
        _log.Info("BulkPrint Manager closed.");
    }
}
