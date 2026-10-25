using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PurgeCad.App.Services;
using PurgeCad.Core.Settings;

namespace PurgeCad.App.ViewModels;

/// <summary>Every start: language + theme (both applied live) and the no-warranty notice, then the main window.</summary>
public sealed partial class WelcomeViewModel : ObservableObject
{
    readonly Action<AvailableUpdate?> _done;

    public WelcomeViewModel(Action<AvailableUpdate?> done)
    {
        _done = done;
        // The status line is composed in code, so it follows a language change made on this screen.
        Loc.Instance.LanguageChanged += (_, _) => { if (!IsUpdating) ShowUpdateStatus(); };
    }

    public LanguagePickerViewModel Languages { get; } = new();

    // ---------- Version check (every start) ----------

    /// <summary>"Checking…", "You have the latest version (1.0.0)", or "Version 1.1.0 available".</summary>
    [ObservableProperty] private string _updateStatus = "";
    [ObservableProperty] private bool _isUpdating;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate), nameof(CanInstallUpdate))]
    private AvailableUpdate? _update;

    public bool HasUpdate => Update is not null;
    public bool CanInstallUpdate => Update?.CanInstall == true;
    public bool IsUpToDate => _checked && Update is null;
    bool _checked;

    public static string CurrentVersion => PurgeCad.Core.Updates.UpdateChecker.CurrentVersion.ToString();

    /// <summary>Opens a web page (the window owns the launcher).</summary>
    public event EventHandler<string>? UrlRequested;

    bool _checking;

    public async Task CheckForUpdatesAsync()
    {
        if (AppHost.Current.Settings.CheckForUpdates)
        {
            _checking = true;
            ShowUpdateStatus();
            var (ok, update) = await UpdateService.TryCheckAsync();
            _checking = false;
            _checked = ok;
            Update = update;
            OnPropertyChanged(nameof(IsUpToDate));
        }
        ShowUpdateStatus();
    }

    void ShowUpdateStatus() => UpdateStatus =
        !AppHost.Current.Settings.CheckForUpdates ? Loc.Instance.Format("update.current", ("version", CurrentVersion))
        : _checking ? Loc.Instance["update.checking"]
        : Update is not null ? Loc.Instance.Format("update.available", ("version", Update.Version.ToString()))
        : _checked ? Loc.Instance.Format("update.upToDate", ("version", CurrentVersion))
        : Loc.Instance.Format("update.unknown", ("version", CurrentVersion));

    [RelayCommand]
    async Task InstallUpdate()
    {
        if (Update is null) return;
        if (!Update.CanInstall)
        {
            UrlRequested?.Invoke(this, Update.PageUrl);
            return;
        }
        IsUpdating = true;
        if (!await UpdateService.TryInstallAndRestartAsync(Update, text => UpdateStatus = text)) IsUpdating = false;
    }

    public bool IsDark
    {
        get => AppHost.Current.Settings.Theme == ThemeChoice.Dark;
        set => SetTheme(value ? ThemeChoice.Dark : ThemeChoice.Light);
    }

    public bool IsLight
    {
        get => !IsDark;
        set => SetTheme(value ? ThemeChoice.Light : ThemeChoice.Dark);
    }

    void SetTheme(ThemeChoice theme)
    {
        ThemeService.Choose(theme);
        OnPropertyChanged(nameof(IsDark));
        OnPropertyChanged(nameof(IsLight));
    }

    /// <summary>The no-warranty notice, ticked by the user at every start; Continue waits for it.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private bool _noticeAccepted;

    [RelayCommand(CanExecute = nameof(NoticeAccepted))]
    void Continue()
    {
        AppHost.Current.Settings.AcceptedNoticeVersion = AppSettings.CurrentNoticeVersion;
        AppHost.Current.Settings.FirstRunCompleted = true;
        AppHost.Current.SaveSettings();
        _done(Update);
    }
}
