using System.Globalization;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using PurgeCad.App.Services;
using PurgeCad.Core.Branding;
using PurgeCad.Core.News;
using PurgeCad.Core.Purging;
using PurgeCad.Core.Updates;

namespace PurgeCad.App.Views;

/// <summary>One row in the About links list.</summary>
public sealed class AboutLink(string title, string url, Geometry? icon, TopLevel owner)
{
    public string Title { get; } = title;
    public string Url { get; } = url;
    public Geometry? Icon { get; } = icon;
    public IAsyncRelayCommand OpenCommand { get; } = new AsyncRelayCommand(() => owner.Launcher.LaunchUriAsync(new Uri(url)));
}

/// <summary>A release in What's new.</summary>
public sealed class AboutRelease(ReleaseNote note, TopLevel owner)
{
    public string Tag { get; } = note.Tag;
    public string DateText { get; } = note.Date?.ToString("d", Loc.Instance.Culture) ?? "";
    public string Notes { get; } = note.Notes;
    public string Url { get; } = note.PageUrl;
    public bool IsCurrent { get; } = UpdateChecker.ParseVersion(note.Tag) == UpdateChecker.CurrentVersion;
    public IAsyncRelayCommand OpenCommand { get; } = new AsyncRelayCommand(() => owner.Launcher.LaunchUriAsync(new Uri(note.PageUrl)));
}

/// <summary>A message in Messages.</summary>
public sealed class AboutNews(NewsItem item, TopLevel owner)
{
    public string Title { get; } = item.Title;
    public string Text { get; } = item.Text;
    public string DateText { get; } = item.Date?.ToString("d", Loc.Instance.Culture) ?? "";
    public string? Link { get; } = item.Link;
    public bool HasLink => Link is not null;
    public bool IsWarning { get; } = item.IsWarning;
    public IAsyncRelayCommand OpenCommand { get; } = new AsyncRelayCommand(async () =>
    {
        if (item.Link is { } link) await owner.Launcher.LaunchUriAsync(new Uri(link));
    });
}

public partial class AboutWindow : Window
{
    public AboutWindow() : this(0) { }

    /// <summary>Opens on a tab: 0 About, 1 What's new, 2 Messages, 3 Help.</summary>
    public AboutWindow(int tab = 0)
    {
        AvaloniaXamlLoader.Load(this);
        var loc = Loc.Instance;
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.1.0";

        this.FindControl<TextBlock>("ByLine")!.Text = loc["about.by"];
        this.FindControl<TextBlock>("Version")!.Text = loc.Format("about.version", ("version", version));

        this.FindControl<ItemsControl>("Links")!.ItemsSource = new[]
        {
            new AboutLink(loc["about.appPage"], Brand.AppPageUrl(loc.Language.Code), FindIcon("IconGlobe"), this),
            new AboutLink(loc["about.partOf"], Brand.AppsUrl, FindIcon("IconGrid"), this),
            new AboutLink(loc["about.website"], Brand.WebsiteUrl, FindIcon("IconHome"), this),
            new AboutLink(loc["about.source"], Brand.SourceCodeUrl, FindIcon("IconCode"), this),
        };

        var support = this.FindControl<Button>("SupportLink")!;
        ToolTip.SetTip(support, Brand.SupportUrl(loc.Language.Code));
        support.Click += async (_, _) => await Launcher.LaunchUriAsync(new Uri(Brand.SupportUrl(loc.Language.Code)));

        SetUpUpdates(loc, version);
        SetUpHelp(loc);
        _ = LoadReleasesAsync(loc);
        _ = LoadNewsAsync(loc);

        var tabs = new[] { "TabAbout", "TabNew", "TabNews", "TabHelp" };
        this.FindControl<RadioButton>(tabs[Math.Clamp(tab, 0, tabs.Length - 1)])!.IsChecked = true;
        this.FindControl<Button>("CloseButton")!.Click += (_, _) => Close();
    }

    async Task LoadReleasesAsync(Loc loc)
    {
        var state = this.FindControl<TextBlock>("ReleasesState")!;
        state.Text = loc["news.loading"];
        this.FindControl<Button>("AllReleases")!.Click += async (_, _) =>
            await Launcher.LaunchUriAsync(new Uri($"https://github.com/{Brand.UpdateRepository}/releases"));
        var releases = await NewsFeed.ReleasesAsync();
        this.FindControl<ItemsControl>("Releases")!.ItemsSource = releases.Select(r => new AboutRelease(r, this)).ToList();
        state.Text = releases.Count == 0 ? loc["about.whatsNew.empty"] : "";
        state.IsVisible = releases.Count == 0;
    }

    async Task LoadNewsAsync(Loc loc)
    {
        var state = this.FindControl<TextBlock>("NewsState")!;
        state.Text = loc["news.loading"];
        // the main window already loaded them; ask again only when it has none
        var news = Owner?.DataContext is ViewModels.MainViewModel { News.Count: > 0 } vm
            ? vm.News
            : await NewsFeed.LoadAsync(loc.Language.Code);
        this.FindControl<ItemsControl>("NewsList")!.ItemsSource = news.Select(n => new AboutNews(n, this)).ToList();
        state.Text = news.Count == 0 ? loc["news.empty"] : "";
        state.IsVisible = news.Count == 0;
        this.FindControl<TextBlock>("NewsCount")!.Text = news.Count.ToString(CultureInfo.CurrentCulture);
        this.FindControl<Border>("NewsCountBadge")!.IsVisible = news.Count > 0;
    }

    void SetUpHelp(Loc loc)
    {
        this.FindControl<Button>("ReportButton")!.Click += async (_, _) => await Launcher.LaunchUriAsync(new Uri(Reveal.NewIssueUrl()));

        var logs = EngineLogs.Folder;
        this.FindControl<TextBlock>("LogsPath")!.Text = logs;
        var logsButton = this.FindControl<Button>("LogsButton")!;
        ToolTip.SetTip(logsButton, logs);
        logsButton.Click += async (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(logs); // opens even before the first run
                await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(logs));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        };

        var info = Reveal.SystemInfo();
        this.FindControl<SelectableTextBlock>("SystemInfo")!.Text = info;
        var copyText = this.FindControl<TextBlock>("CopyInfoText")!;
        this.FindControl<Button>("CopyInfoButton")!.Click += async (_, _) =>
        {
            if (Clipboard is null) return;
            await Clipboard.SetTextAsync(info);
            copyText.Text = loc["help.copied"];
        };
    }

    void SetUpUpdates(Loc loc, string version)
    {
        var settings = AppHost.Current.Settings;
        var status = this.FindControl<TextBlock>("UpdateStatus")!;
        var check = this.FindControl<Button>("CheckUpdatesButton")!;
        var download = this.FindControl<Button>("DownloadButton")!;
        var auto = this.FindControl<CheckBox>("AutoCheck")!;

        status.Text = loc.Format("update.current", ("version", version));
        auto.IsChecked = settings.CheckForUpdates;
        auto.IsCheckedChanged += (_, _) =>
        {
            settings.CheckForUpdates = auto.IsChecked == true;
            AppHost.Current.SaveSettings();
        };

        // Installing happens from the title bar (it knows when files are being processed); here it is the page link.
        string? page = null;
        download.Click += async (_, _) => { if (page is not null) await Launcher.LaunchUriAsync(new Uri(page)); };
        check.Click += async (_, _) =>
        {
            check.IsEnabled = false;
            status.Text = loc["update.checking"];
            var update = await UpdateService.CheckAsync();
            check.IsEnabled = true;
            page = update?.PageUrl;
            download.IsVisible = update is not null;
            if (update is not null && Owner?.DataContext is ViewModels.MainViewModel vm) vm.Update = update;
            status.Text = update is null
                ? loc.Format("update.upToDate", ("version", version))
                : loc.Format("update.available", ("version", update.Version.ToString()));
        };
    }

    Geometry? FindIcon(string key) => this.TryFindResource(key, out var value) ? value as Geometry : null;
}
