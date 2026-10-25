using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PurgeCad.App.Services;
using PurgeCad.Core.Batch;
using PurgeCad.Core.Branding;
using PurgeCad.Core.Cleanup;
using PurgeCad.Core.Purging;
using PurgeCad.Core.Reporting;
using PurgeCad.Core.Scanning;
using PurgeCad.Core.Settings;

namespace PurgeCad.App.ViewModels;

/// <summary>
/// Main window: folder → live scan → options → run with progress → results and report.
/// Dialogs (folder pickers, confirmations) are raised as events and handled by the view.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    readonly AppHost _host = AppHost.Current;
    AppSettings Settings => _host.Settings;

    CancellationTokenSource? _scanCts;
    CancellationTokenSource? _runCts;
    /// <summary>Everything the scan found; <see cref="_scan"/> is what passes the name filter.</summary>
    ScanResult? _allScan;
    ScanResult? _scan;
    BatchSummary? _lastSummary;

    /// <param name="update">The newer version found by the start screen, if any (checked once per start).</param>
    public MainViewModel(AvailableUpdate? update = null)
    {
        Engines = [new EngineOptionViewModel(EngineRegistry.AutomaticId, null),
                   .. _host.Engines.Engines.Select(e => new EngineOptionViewModel(e.Id, e))];
        _selectedEngine = Engines.FirstOrDefault(e => e.Id == Settings.Batch.EngineId && e.IsAvailable) ?? Engines[0];

        _selectedEngine.IsSelected = true;
        Categories = new(Enum.GetValues<PurgeCategory>().Select(c => new CategoryOptionViewModel(c, Settings.Batch.Purge)));
        foreach (var c in Categories)
            c.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(CategoryOptionViewModel.IsChecked)) OnPropertyChanged(nameof(CategorySummary)); };

        _folder = Settings.LastFolder ?? "";
        _includeSubfolders = Settings.IncludeSubfolders;
        _includeDwg = Settings.IncludeDwg;
        _includeDxf = Settings.IncludeDxf;
        _outputFolder = Settings.Batch.OutputFolder ?? "";
        _parallelism = Settings.Batch.Parallelism;
        ParallelChoices = Enumerable.Range(1, 4).Select(n => new ChoiceViewModel(n, () => Parallelism, v => Parallelism = v)).ToList();

        Loc.Instance.LanguageChanged += (_, _) => RefreshTexts();
        if (Directory.Exists(Folder)) _ = ScanAsync();
        if (update is not null && update.Version.ToString() != Settings.SkippedVersion) _update = update;
    }

    // ---------- Updates ----------

    /// <summary>A newer release, when there is one the user has not skipped.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate), nameof(UpdateText), nameof(CanInstallUpdate))]
    private AvailableUpdate? _update;

    public bool HasUpdate => Update is not null;
    public bool CanInstallUpdate => Update?.CanInstall == true;
    public string UpdateText => Update is null ? "" : Loc.Instance.Format("update.available", ("version", Update.Version.ToString()));

    /// <summary>Download progress text while the update installs; empty otherwise.</summary>
    [ObservableProperty] private string _updateProgress = "";
    [ObservableProperty] private bool _isUpdating;

    /// <summary>Opens a web page (the window does it: it owns the launcher).</summary>
    public event EventHandler<string>? UrlRequested;

    /// <summary>One click: download, close, install, restart. Not while files are being processed.</summary>
    [RelayCommand(CanExecute = nameof(CanUpdateNow))]
    async Task InstallUpdate()
    {
        if (Update is null) return;
        if (!Update.CanInstall)
        {
            UrlRequested?.Invoke(this, Update.PageUrl);
            return;
        }
        IsUpdating = true;
        InstallUpdateCommand.NotifyCanExecuteChanged();
        if (!await UpdateService.TryInstallAndRestartAsync(Update, text => UpdateProgress = text))
        {
            IsUpdating = false;
            InstallUpdateCommand.NotifyCanExecuteChanged();
        }
    }

    bool CanUpdateNow() => !IsRunning && !IsUpdating;

    [RelayCommand]
    void OpenUpdatePage()
    {
        if (Update is not null) UrlRequested?.Invoke(this, Update.PageUrl);
    }

    [RelayCommand]
    void SkipUpdate()
    {
        if (Update is null) return;
        Settings.SkippedVersion = Update.Version.ToString();
        _host.SaveSettings();
        Update = null;
    }

    public LanguagePickerViewModel Languages { get; } = new();

    // ---------- Header ----------

    public string AppName => Brand.AppName;

    /// <summary>Two-way for the Light / Dark pill.</summary>
    public bool IsDark
    {
        get => Settings.Theme == ThemeChoice.Dark;
        set { if (value) SetTheme(ThemeChoice.Dark); }
    }

    public bool IsLight
    {
        get => Settings.Theme == ThemeChoice.Light;
        set { if (value) SetTheme(ThemeChoice.Light); }
    }

    void SetTheme(ThemeChoice theme)
    {
        if (!ThemeService.Choose(theme)) return;
        _host.SaveSettings();
        OnPropertyChanged(nameof(IsDark));
        OnPropertyChanged(nameof(IsLight));
    }

    // ---------- Mode: purge or auxiliary cleanup ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCleanMode))]
    private bool _isPurgeMode = true;

    public bool IsCleanMode
    {
        get => !IsPurgeMode;
        set => IsPurgeMode = !value;
    }

    public event EventHandler? AboutRequested;

    [RelayCommand]
    void ShowAbout() => AboutRequested?.Invoke(this, EventArgs.Empty);

    // ---------- Source and scan ----------

    [ObservableProperty] private string _folder;
    [ObservableProperty] private bool _includeSubfolders;
    [ObservableProperty] private bool _includeDwg;
    [ObservableProperty] private bool _includeDxf;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _scanText = "";
    [ObservableProperty] private int _countDwg;
    [ObservableProperty] private int _countDxf;
    [ObservableProperty] private int _countAux;

    public bool HasFolder => !string.IsNullOrWhiteSpace(Folder);
    public string FolderName => HasFolder ? Path.GetFileName(Folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) is { Length: > 0 } n ? n : Folder : "";

    void ShowCounts(ScanCounts c)
    {
        CountDwg = c.Dwg;
        CountDxf = c.Dxf;
        CountAux = c.Auxiliary;
    }

    partial void OnFolderChanged(string value)
    {
        Settings.LastFolder = value;
        OnPropertyChanged(nameof(HasFolder));
        OnPropertyChanged(nameof(FolderName));
        _ = ScanAsync();
    }
    partial void OnIncludeSubfoldersChanged(bool value) { Settings.IncludeSubfolders = value; _ = ScanAsync(); }
    partial void OnIncludeDwgChanged(bool value) { Settings.IncludeDwg = value; _ = ScanAsync(); }
    partial void OnIncludeDxfChanged(bool value) { Settings.IncludeDxf = value; _ = ScanAsync(); }

    public bool HasDrawings => _scan?.Drawings.Count > 0;

    async Task ScanAsync()
    {
        _scanCts?.Cancel();
        if (!Directory.Exists(Folder))
        {
            _allScan = _scan = null; ScanText = ""; ShowCounts(new ScanCounts(0, 0, 0, 0));
            OnPropertyChanged(nameof(HasDrawings)); return;
        }

        var cts = _scanCts = new CancellationTokenSource();
        IsScanning = true;
        ScanText = Loc.Instance["scan.scanning"];
        var progress = new Progress<ScanCounts>(c => { ScanText = FoundText(c); ShowCounts(c); });
        try
        {
            var excluded = Mode == OutputMode.CopyToFolder && Directory.Exists(OutputFolder) ? new[] { OutputFolder } : null;
            _allScan = await FolderScanner.ScanAsync(
                new ScanOptions(Folder, IncludeSubfolders, IncludeDwg, IncludeDxf, IncludeAuxiliary: true, excluded),
                progress, cts.Token);
            ApplyNameFilter();
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (cts == _scanCts) IsScanning = false;
            OnPropertyChanged(nameof(HasDrawings));
            StartCommand.NotifyCanExecuteChanged();
            CleanCommand.NotifyCanExecuteChanged();
        }
    }

    // ---------- Name filter ----------

    /// <summary>Only drawings whose name contains one of these terms (see <see cref="NameFilter"/>). Not saved.</summary>
    [ObservableProperty] private string _nameFilterText = "";
    CancellationTokenSource? _filterDelay;

    public bool HasNameFilter => !string.IsNullOrWhiteSpace(NameFilterText);

    /// <summary>Size limits in MB, as typed ("2,5" or "2.5"); empty = no limit. Not saved.</summary>
    [ObservableProperty] private string _minSizeText = "";
    [ObservableProperty] private string _maxSizeText = "";

    partial void OnMinSizeTextChanged(string value) => FilterChanged();
    partial void OnMaxSizeTextChanged(string value) => FilterChanged();

    partial void OnNameFilterTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasNameFilter));
        FilterChanged();
    }

    void FilterChanged()
    {
        // wait for a short pause in typing: re-listing thousands of rows on every key is slow
        _filterDelay?.Cancel();
        var delay = _filterDelay = new CancellationTokenSource();
        _ = Task.Delay(TimeSpan.FromMilliseconds(250), delay.Token).ContinueWith(t =>
        {
            if (!t.IsCanceled) ApplyNameFilter();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    [RelayCommand]
    void ClearNameFilter() => NameFilterText = "";

    /// <summary>Shows what passes the name filter: counts, the file list, and what Start and Clean will use.</summary>
    void ApplyNameFilter()
    {
        if (_allScan is null) return;
        _scan = ScanFilter.Apply(_allScan, new NameFilter(NameFilterText),
            new SizeRange(SizeRange.ParseMb(MinSizeText), SizeRange.ParseMb(MaxSizeText)));
        ShowCounts(_scan.Counts(0));
        ShowScannedFiles();
        ScanText = ScanSummary();
        OnPropertyChanged(nameof(HasDrawings));
        StartCommand.NotifyCanExecuteChanged();
        CleanCommand.NotifyCanExecuteChanged();
    }

    string ScanSummary()
    {
        if (_scan is null || _allScan is null) return "";
        var text = _allScan.Drawings.Count == 0 ? Loc.Instance["scan.none"]
            : _scan.Drawings.Count == 0 ? Loc.Instance["scan.filter.none"]
            : FoundText(_scan.Counts(0));
        if (_scan != _allScan && _allScan.Drawings.Count > 0)
            text += "  " + Loc.Instance.Format("scan.filter.shown", ("shown", _scan.Drawings.Count), ("total", _allScan.Drawings.Count));
        if (_allScan.InaccessibleFolders.Count > 0)
            text += "  " + Loc.Instance.Plural("scan.inaccessible", _allScan.InaccessibleFolders.Count);
        return text;
    }

    /// <summary>Lists the drawings found, all selected, so the user can untick the ones to leave out.</summary>
    void ShowScannedFiles()
    {
        if (IsRunning || _scan is null) return;
        HasSummary = false;
        // one new list instead of thousands of single adds (each add makes the list relayout)
        ReplaceRows(_scan.Drawings.Select(d => NewRow(new FileReport { File = d, SizeBefore = d.Size }, isChecked: true)));
        SelectionChanged();
    }

    FileRowViewModel NewRow(FileReport report, bool isChecked)
    {
        var row = new FileRowViewModel(report) { IsChecked = isChecked };
        row.CheckedChanged = SelectionChanged;
        return row;
    }

    // ---------- File selection ----------

    public int SelectedCount => Rows.Count(r => r.IsChecked);

    /// <summary>Header checkbox: true = all, false = none, null = some.</summary>
    public bool? AllSelected
    {
        get => Rows.Count == 0 ? false : Rows.All(r => r.IsChecked) ? true : Rows.Any(r => r.IsChecked) ? null : false;
        set
        {
            // Like a mail inbox: when everything is ticked a click clears all, otherwise it ticks all.
            var select = !Rows.All(r => r.IsChecked);
            _bulkSelecting = true;
            foreach (var r in Rows) r.IsChecked = select;
            _bulkSelecting = false;
            SelectionChanged();
        }
    }
    bool _bulkSelecting;

    /// <summary>"9 files" when all are selected, "3 of 9 selected" otherwise.</summary>
    public string SelectionText => SelectedCount == Rows.Count
        ? Loc.Instance.Plural("results.files", Rows.Count)
        : Loc.Instance.Format("results.selected", ("selected", SelectedCount), ("total", Rows.Count));

    void SelectionChanged()
    {
        if (_bulkSelecting) return;
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(AllSelected));
        OnPropertyChanged(nameof(SelectionText));
        StartCommand.NotifyCanExecuteChanged();
    }

    static string FoundText(ScanCounts c) =>
        Loc.Instance.Format("scan.found", ("dwg", c.Dwg), ("dxf", c.Dxf), ("aux", c.Auxiliary));

    // ---------- Options ----------

    public IReadOnlyList<EngineOptionViewModel> Engines { get; }
    [ObservableProperty] private EngineOptionViewModel _selectedEngine;
    partial void OnSelectedEngineChanged(EngineOptionViewModel value)
    {
        Settings.Batch.EngineId = value.Id;
        OnPropertyChanged(nameof(ShowAllowUnknown));
        foreach (var e in Engines) e.IsSelected = e == value;
    }

    [RelayCommand]
    void SelectEngine(EngineOptionViewModel engine)
    {
        if (engine.IsAvailable) SelectedEngine = engine;
    }

    public ObservableCollection<CategoryOptionViewModel> Categories { get; }

    /// <summary>"14 / 16" — shown on the compact "What to purge" button.</summary>
    public string CategorySummary => Loc.Instance.Format("options.what.summary",
        ("selected", Categories.Count(c => c.IsChecked)), ("total", Categories.Count));

    [RelayCommand]
    void SelectCategories(string which)
    {
        foreach (var c in Categories)
            c.IsChecked = which == "all" ? !c.IsDangerous : which == "none" ? false : c.IsChecked;
    }

    /// <summary>The "allow unknown objects" switch matters only when ACadSharp may be used.</summary>
    public bool ShowAllowUnknown => SelectedEngine.Id != "autocad";

    public OutputMode Mode
    {
        get => Settings.Batch.Mode;
        set
        {
            Settings.Batch.Mode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCopyMode));
            OnPropertyChanged(nameof(IsInPlaceMode));
            OnPropertyChanged(nameof(IsReportOnlyMode));
            OnPropertyChanged(nameof(IsReplaceMode));
            OnPropertyChanged(nameof(OutputHint));
            StartCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsCopyMode { get => Mode == OutputMode.CopyToFolder; set { if (value) Mode = OutputMode.CopyToFolder; } }
    public bool IsInPlaceMode { get => Mode == OutputMode.InPlaceWithBackup; set { if (value) Mode = OutputMode.InPlaceWithBackup; } }
    /// <summary>One line under the "Where to save" choice explaining the selected mode.</summary>
    public string OutputHint => Loc.Instance[Mode switch
    {
        OutputMode.CopyToFolder => "output.copy.hint",
        OutputMode.InPlaceWithBackup => "output.inPlace.hint",
        OutputMode.ReplaceOriginals => "output.replace.hint",
        _ => "output.reportOnly.hint",
    }];

    public bool IsReplaceMode { get => Mode == OutputMode.ReplaceOriginals; set { if (value) Mode = OutputMode.ReplaceOriginals; } }
    public bool IsReportOnlyMode { get => Mode == OutputMode.ReportOnly; set { if (value) Mode = OutputMode.ReportOnly; } }

    [ObservableProperty] private string _outputFolder;
    partial void OnOutputFolderChanged(string value)
    {
        Settings.Batch.OutputFolder = value;
        StartCommand.NotifyCanExecuteChanged();
    }

    [ObservableProperty] private int _parallelism;
    partial void OnParallelismChanged(int value)
    {
        Settings.Batch.Parallelism = Math.Clamp(value, 1, 4);
        foreach (var c in ParallelChoices) c.Refresh();
    }

    /// <summary>1–4 files at the same time, shown as a small segmented control.</summary>
    public IReadOnlyList<ChoiceViewModel> ParallelChoices { get; }

    public AuxiliaryCleanupOptions Cleanup => Settings.Cleanup;

    [ObservableProperty] private bool _cleanDuringPurge;

    /// <summary>Lets ACadSharp write files even when it must drop objects it cannot save. Off by default.</summary>
    public bool AllowUnknownObjects
    {
        get => Settings.Batch.Purge.AllowUnknownObjects;
        set
        {
            if (Settings.Batch.Purge.AllowUnknownObjects == value) return;
            Settings.Batch.Purge.AllowUnknownObjects = value;
            OnPropertyChanged();
        }
    }

    // ---------- Run ----------

    /// <summary>The file list. Replaced as a whole when it is rebuilt (scan, filter, start): far faster than
    /// clearing and adding thousands of rows one by one.</summary>
    public ObservableCollection<FileRowViewModel> Rows { get; private set; } = new();
    public bool HasRows => Rows.Count > 0;

    void ReplaceRows(IEnumerable<FileRowViewModel> rows)
    {
        Rows.CollectionChanged -= RowsChanged;
        Rows = new ObservableCollection<FileRowViewModel>(rows);
        Rows.CollectionChanged += RowsChanged;
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(HasRows));
    }

    void RowsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
        OnPropertyChanged(nameof(HasRows));

    [ObservableProperty] private bool _isRunning;
    partial void OnIsRunningChanged(bool value) => InstallUpdateCommand.NotifyCanExecuteChanged();
    [ObservableProperty] private bool _isStopping;
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private double _progressMaximum = 1;
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private bool _hasSummary;

    /// <summary>Asks the view to confirm an auxiliary cleanup (plan shown to the user). Returns true to go on.</summary>
    public Func<CleanupPlan, Task<bool>>? ConfirmCleanup { get; set; }

    /// <summary>Asks the view for a path to save the report; null = cancelled.</summary>
    public Func<Task<string?>>? AskReportPath { get; set; }

    /// <summary>Asks before overwriting originals without a backup (count of drawings). Set by the view.</summary>
    public Func<int, Task<bool>>? ConfirmReplace { get; set; }

    bool CanStart() => !IsRunning && HasDrawings && SelectedCount > 0
        && (Mode != OutputMode.CopyToFolder || !string.IsNullOrWhiteSpace(OutputFolder));

    [RelayCommand(CanExecute = nameof(CanStart))]
    async Task StartAsync()
    {
        var engine = _host.Engines.Resolve(SelectedEngine.Id);
        if (engine is null || _scan is null) return;
        // Only the ticked files run; the others stay listed, dimmed, with their last result.
        var selected = Rows.Where(r => r.IsChecked).Select(r => r.Report.File.Path).ToHashSet();
        var run = new ScanResult { Root = _scan.Root };
        run.Drawings.AddRange(_scan.Drawings.Where(d => selected.Contains(d.Path)));
        run.Auxiliary.AddRange(_scan.Auxiliary);
        if (run.Drawings.Count == 0) return;
        if (Mode == OutputMode.ReplaceOriginals && ConfirmReplace is not null && !await ConfirmReplace(run.Drawings.Count))
            return;

        _host.SaveSettings();
        var rowByFile = new Dictionary<string, FileRowViewModel>();
        var rowByRelative = new Dictionary<string, FileRowViewModel>();
        var fresh = new List<FileRowViewModel>(Rows.Count);
        foreach (var old in Rows)
        {
            if (!old.IsChecked) { fresh.Add(old); continue; }
            var d = old.Report.File;
            var row = NewRow(new FileReport { File = d, SizeBefore = d.Size }, isChecked: true);
            rowByFile[d.Path] = row;
            rowByRelative[d.RelativePath] = row;
            fresh.Add(row);
        }
        ReplaceRows(fresh);
        // finished files are found by position, not searched for in a list of thousands
        var indexOf = new Dictionary<FileRowViewModel, int>();
        for (var i = 0; i < Rows.Count; i++) indexOf[Rows[i]] = i;

        IsRunning = true;
        IsStopping = false;
        HasSummary = false;
        ProgressMaximum = Math.Max(1, run.Drawings.Count);
        ProgressValue = 0;
        _runCts = new CancellationTokenSource();
        StartCommand.NotifyCanExecuteChanged();

        var progress = new Progress<BatchProgress>(p =>
        {
            if (p.CurrentFile is { } current && rowByRelative.TryGetValue(current, out var running)
                && running.Report.Status == FileStatus.Pending)
                running.IsRunning = true;
            // Parallel workers can report out of order; never move the bar backwards.
            var value = p.Fraction * p.Total;
            if (value < ProgressValue) return;
            ProgressValue = value;
            ProgressText = Loc.Instance.Format("run.progress", ("done", p.Done), ("total", p.Total))
                + " · " + ((int)(p.Fraction * 100)).ToString(Loc.Instance.Culture) + "%"
                + (p.Remaining is { } r ? " · " + Loc.Instance.Format("run.remaining", ("time", FormatTime(r))) : "");
        });

        try
        {
            _lastSummary = await BatchRunner.RunAsync(run, new PurgeOperation(engine), Settings.Batch, progress,
                report => Dispatcher.UIThread.Post(() =>
                {
                    if (!rowByFile.TryGetValue(report.File.Path, out var row)) return;
                    if (!indexOf.Remove(row, out var index) || index >= Rows.Count || Rows[index] != row) index = Rows.IndexOf(row);
                    var updated = NewRow(report, isChecked: true);
                    rowByFile[report.File.Path] = updated;
                    if (index < 0) return;
                    Rows[index] = updated;
                    indexOf[updated] = index;
                }),
                _runCts.Token);

            if (CleanDuringPurge && Cleanup.Any && !_runCts.IsCancellationRequested)
                await CleanAsync();

            ProgressValue = _lastSummary.Files.Count(f => f.Status != FileStatus.Pending);
            ProgressText = Loc.Instance.Format("run.progress", ("done", ProgressValue), ("total", run.Drawings.Count));
            ShowSummary(_lastSummary);
        }
        finally
        {
            IsRunning = false;
            IsStopping = false;
            StartCommand.NotifyCanExecuteChanged();
            SaveReportCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    void Stop()
    {
        IsStopping = true;
        _runCts?.Cancel();
    }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(AnyPurged))] private int _summaryPurged;
    public bool AnyPurged => SummaryPurged > 0;
    [ObservableProperty] private int _summaryUnchanged;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(AnySkipped))] private int _summarySkipped;
    public bool AnySkipped => SummarySkipped > 0;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(AnyFailed))] private int _summaryFailed;
    public bool AnyFailed => SummaryFailed > 0;
    [ObservableProperty] private string _summarySaved = "";
    [ObservableProperty] private string _summaryBefore = "";
    [ObservableProperty] private string _summaryAfter = "";
    [ObservableProperty] private bool _summaryIsEstimate;

    void ShowSummary(BatchSummary s)
    {
        SummaryPurged = s.Count(FileStatus.Purged);
        SummaryUnchanged = s.Count(FileStatus.NothingToPurge);
        SummarySkipped = s.Count(FileStatus.Skipped);
        SummaryFailed = s.Count(FileStatus.Failed);
        SummarySaved = Loc.Instance.Size(Math.Max(0, s.BytesSaved));
        SummaryBefore = Loc.Instance.Size(s.BytesBefore);
        SummaryAfter = Loc.Instance.Size(s.BytesAfter);
        SummaryIsEstimate = s.IsEstimate;
        HasSummary = true;
    }

    bool CanSaveReport() => _lastSummary is not null && !IsRunning;

    [RelayCommand(CanExecute = nameof(CanSaveReport))]
    async Task SaveReportAsync()
    {
        if (_lastSummary is null || AskReportPath is null) return;
        var path = await AskReportPath();
        if (path is null) return;
        CsvReport.Write(path, _lastSummary, key => Loc.Instance[key]);
    }

    // ---------- Auxiliary cleanup (separate tool) ----------

    [ObservableProperty] private string _cleanupText = "";

    bool CanClean() => !IsRunning && _scan is not null && Cleanup.Any;

    [RelayCommand(CanExecute = nameof(CanClean))]
    async Task CleanAsync()
    {
        if (_scan is null) return;
        var plan = AuxiliaryCleaner.Plan(_scan, Cleanup);
        if (plan.Count == 0) { CleanupText = Loc.Instance.Format("cleanup.done", ("count", 0), ("size", Loc.Instance.Size(0))); return; }
        if (ConfirmCleanup is not null && !await ConfirmCleanup(plan)) return;

        var outcomes = await AuxiliaryCleaner.ExecuteAsync(plan, _scan.Root, Cleanup);
        var removed = outcomes.Where(o => o.Result.Kind is RemovalKind.RecycleBin or RemovalKind.MovedToFolder or RemovalKind.Deleted).ToList();
        CleanupText = Loc.Instance.Format("cleanup.done", ("count", removed.Count), ("size", Loc.Instance.Size(removed.Sum(o => o.Item.File.Size))));
        if (removed.Any(o => o.Result.Kind == RemovalKind.MovedToFolder))
            CleanupText += "\n" + Loc.Instance["cleanup.networkNote"];
        _ = ScanAsync();
    }

    /// <summary>Re-checks whether the Clean button can run (after a cleanup checkbox changes).</summary>
    public void CleanupOptionsChanged()
    {
        _host.SaveSettings();
        CleanCommand.NotifyCanExecuteChanged();
    }

    // ---------- Language ----------

    void RefreshTexts()
    {
        OnPropertyChanged(nameof(SelectionText));
        foreach (var c in Categories) c.Refresh();
        foreach (var r in Rows) r.Refresh();
        OnPropertyChanged(nameof(UpdateText));
        if (_scan is not null && !IsScanning) ScanText = ScanSummary();
        if (_lastSummary is not null && HasSummary) ShowSummary(_lastSummary);
        foreach (var e in Engines) e.Refresh();
        OnPropertyChanged(nameof(OutputHint));
        OnPropertyChanged(nameof(CategorySummary));
    }

    static string FormatTime(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes:00}m"
        : t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes}m {t.Seconds:00}s" : $"{t.Seconds}s";
}
