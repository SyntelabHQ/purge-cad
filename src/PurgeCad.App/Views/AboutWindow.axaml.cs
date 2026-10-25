using System.Reflection;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using PurgeCad.App.Services;
using PurgeCad.Core.Branding;
using PurgeCad.Engines.AutoCad;

namespace PurgeCad.App.Views;

/// <summary>One row in the About links list.</summary>
public sealed class AboutLink(string title, string url, Geometry? icon, TopLevel owner)
{
    public string Title { get; } = title;
    public string Url { get; } = url;
    public Geometry? Icon { get; } = icon;
    public IAsyncRelayCommand OpenCommand { get; } = new AsyncRelayCommand(() => owner.Launcher.LaunchUriAsync(new Uri(url)));
}

public partial class AboutWindow : Window
{
    public AboutWindow()
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

        var logs = AutoCadEngine.LogFolder;
        this.FindControl<TextBlock>("LogsPath")!.Text = logs;
        var logsButton = this.FindControl<Button>("LogsButton")!;
        ToolTip.SetTip(logsButton, logs);
        logsButton.Click += async (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(logs); // opens even before the first AutoCAD run
                await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(logs));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        };

        SetUpUpdates(loc, version);

        this.FindControl<Button>("CloseButton")!.Click += (_, _) => Close();
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
