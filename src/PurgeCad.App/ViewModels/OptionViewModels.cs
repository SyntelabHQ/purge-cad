using CommunityToolkit.Mvvm.ComponentModel;
using PurgeCad.App.Services;
using PurgeCad.Core.Batch;
using PurgeCad.Core.Purging;

namespace PurgeCad.App.ViewModels;

/// <summary>One engine choice with its risk badge and the reasons it is unavailable here, if any.</summary>
public sealed partial class EngineOptionViewModel(string id, IPurgeEngine? engine) : ObservableObject
{
    [ObservableProperty] private bool _isSelected;

    public string Id { get; } = id;
    public IPurgeEngine? Engine { get; } = engine;

    public string Name => Loc.Instance[Engine?.NameKey ?? "engine.auto.name"];

    public EngineAvailability Availability => Engine?.CheckAvailability() ?? EngineAvailability.Available;
    public bool IsAvailable => Availability.IsAvailable;
    public string? UnavailableReason => Availability.ReasonKey is { } key ? Loc.Instance[key] : null;

    public string RiskText => Engine is null ? "" : Loc.Instance["risk." + Engine.Risk];
    public bool HasRisk => Engine is not null;
    public bool IsVeryLow => Engine?.Risk is RiskLevel.VeryLow or RiskLevel.Low;
    public bool IsMedium => Engine?.Risk is RiskLevel.Medium or RiskLevel.High;

    /// <summary>Refreshes texts after a language change.</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);

    public IReadOnlyList<string> Risks => Engine is null
        ? [Loc.Instance["engine.auto.description"], Loc.Instance["engine.auto.fallback"]]
        : Engine.RiskKeys.Select(k => Loc.Instance[k]).ToList();
}

/// <summary>A version to save in; null code = each drawing keeps its own.</summary>
public sealed partial class SaveVersionChoice(string? code, Func<string?>? selected = null, Action<SaveVersionChoice>? select = null)
    : ObservableObject
{
    public string? Code { get; } = code;
    /// <summary>Convert tab: the chosen version among the buttons.</summary>
    public bool IsSelected
    {
        get => selected?.Invoke() == Code;
        set { if (value) select?.Invoke(this); }
    }
    public string Name => Code is null ? Loc.Instance["saveAs.original"] : "DWG / DXF " + SaveVersions.NameOf(Code);
    /// <summary>Short text on the button.</summary>
    public string Short => Code is null ? Loc.Instance["saveAs.original"] : SaveVersions.NameOf(Code);
    public void Refresh() => OnPropertyChanged(string.Empty);
    public override string ToString() => Name;
}

/// <summary>A folder in the recent list: its name, and the full path under it.</summary>
public sealed record RecentFolderItem(string Path)
{
    public string Name => System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(Path)) is { Length: > 0 } n ? n : Path;
}

/// <summary>One option of a small segmented choice (for example files at the same time).</summary>
public sealed partial class ChoiceViewModel(int value, Func<int> get, Action<int> set) : ObservableObject
{
    public int Value { get; } = value;
    public string Label => Value.ToString(Loc.Instance.Culture);

    public bool IsSelected
    {
        get => get() == Value;
        set { if (value) set(Value); }
    }

    public void Refresh() => OnPropertyChanged(nameof(IsSelected));
}

/// <summary>A purge category checkbox.</summary>
public sealed partial class CategoryOptionViewModel(PurgeCategory category, PurgeOptions options) : ObservableObject
{
    PurgeOptions options = options;

    public PurgeCategory Category { get; } = category;
    public string Name => Loc.Instance["category." + Category];
    public bool IsDangerous => PurgeOptions.Dangerous.Contains(Category);
    /// <summary>Off until chosen: not ticked by "All".</summary>
    public bool IsOptIn => PurgeOptions.OptIn.Contains(Category);
    public string? DangerHint => IsDangerous ? Loc.Instance["options.dangerous"]
        : Category == PurgeCategory.MissingReferences ? Loc.Instance["category.MissingReferences.hint"] : null;

    /// <summary>After a profile replaced the options object.</summary>
    public void Rebind(PurgeOptions newOptions)
    {
        options = newOptions;
        OnPropertyChanged(nameof(IsChecked));
    }

    public bool IsChecked
    {
        get => options.Includes(Category);
        set
        {
            if (value) options.Categories.Add(Category); else options.Categories.Remove(Category);
            OnPropertyChanged();
        }
    }

    public void Refresh() => OnPropertyChanged(string.Empty);
}

/// <summary>One row of the results table.</summary>
public sealed partial class FileRowViewModel(FileReport report) : ObservableObject
{
    public FileReport Report { get; } = report;

    public string File => Report.File.RelativePath;
    /// <summary>Included in the next run. Called back so the window can update its selection count.</summary>
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (!SetProperty(ref _isChecked, value)) return;
            OnPropertyChanged(nameof(ShowStatus));
            CheckedChanged?.Invoke();
        }
    }
    bool _isChecked = true;
    public Action? CheckedChanged { get; set; }

    /// <summary>"Waiting" means nothing on a file that is not selected, so it is hidden there.</summary>
    public bool ShowStatus => IsChecked || Report.Status != FileStatus.Pending;

    /// <summary>True while the engine works on this file.</summary>
    public bool IsRunning
    {
        get => _isRunning;
        set { if (SetProperty(ref _isRunning, value)) OnPropertyChanged(nameof(Status)); }
    }
    bool _isRunning;

    /// <summary>Convert tab: the statuses say converted / already in that version instead of purged.</summary>
    public bool IsConvert
    {
        get => _isConvert;
        set { if (SetProperty(ref _isConvert, value)) OnPropertyChanged(nameof(Status)); }
    }
    bool _isConvert;

    public string Status => IsRunning && Report.Status == FileStatus.Pending
        ? Loc.Instance[IsConvert ? "status.convert.Running" : "status.Running"]
        : IsConvert && Report.Status is FileStatus.Purged or FileStatus.NothingToPurge
            ? Loc.Instance["status.convert." + Report.Status]
            : Loc.Instance["status." + Report.Status];
    public bool IsPurged => Report.Status == FileStatus.Purged;
    public bool IsSkipped => Report.Status == FileStatus.Skipped;
    public bool IsFailed => Report.Status == FileStatus.Failed;
    public bool HasReason => Reason is not null;
    public string? Reason => Report.ReasonKey is { } key
        ? Loc.Instance.Format(key, ("owner", Report.Detail ?? "?"))
        : null;
    /// <summary>Reason plus the technical detail (for example which objects could not be written).</summary>
    public string? ReasonDetail => Reason is null ? null
        : string.IsNullOrEmpty(Report.Detail) ? Reason : Reason + "\n\n" + Report.Detail;
    public string Size => (Report.SizeAfter is { } after
        ? $"{Loc.Instance.Size(Report.SizeBefore)} → {(Report.SizeAfterIsEstimate ? "~" : "")}{Loc.Instance.Size(after)}"
        : Loc.Instance.Size(Report.SizeBefore))
        + (Report.SavedAs is { } v ? " · " + SaveVersions.NameOf(v) : "");
    public string Removed => Report.Removed.Values.Sum() is var n && n > 0 ? n.ToString("N0", Loc.Instance.Culture) : "–";
    public string RemovedDetail => string.Join("\n", Report.Removed.Where(r => r.Value > 0)
        .Select(r => $"{Loc.Instance["category." + r.Key]}: {r.Value}"));
    public string Engine => Report.EngineId is not { } id ? ""
        : Report.FallbackFrom is null ? Loc.Instance[$"engine.{id}.name"]
        : Loc.Instance.Format("results.engine.fallback", ("engine", Loc.Instance[$"engine.{id}.name"]));
    /// <summary>Why another engine did this file (Automatic): the first one could not.</summary>
    public string? EngineTip => Report.FallbackFrom is { } from
        ? Loc.Instance.Format("results.engine.fallback.tip", ("first", Loc.Instance[$"engine.{from}.name"]), ("engine", Loc.Instance[$"engine.{Report.EngineId}.name"]))
        : null;
    /// <summary>The engine's log for this file from the last run, shown in Explorer ("Show log").</summary>
    public bool HasLog => Report.LogPath is not null && Report.Status != FileStatus.Pending;
    public string? LogPath => Report.LogPath;

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    void ShowLog()
    {
        if (Report.LogPath is { } path) Services.Reveal.File(path);
    }

    public string Time => Report.Status == FileStatus.Pending ? "" : Report.Duration.TotalSeconds.ToString("0.0", Loc.Instance.Culture) + " s";

    /// <summary>Refreshes every displayed column (after the file finishes or the language changes).</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);
}
