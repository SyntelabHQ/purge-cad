using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using PurgeCad.App.Services;
using PurgeCad.App.ViewModels;
using PurgeCad.Core.Cleanup;

namespace PurgeCad.App.Views;

/// <summary>Only view work here: pickers, drag and drop, dialogs. Logic lives in MainViewModel.</summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);

        this.FindControl<Button>("ChooseFolder")!.Click += async (_, _) =>
        {
            if (await PickFolder(Loc.Instance["source.choose"]) is { } path) Vm.OpenPaths([path]);
        };
        this.FindControl<Button>("ChooseOutput")!.Click += async (_, _) =>
        {
            if (await PickFolder(Loc.Instance["output.chooseFolder"]) is { } path) Vm.OutputFolder = path;
        };

        foreach (var name in new[] { "CleanLock", "CleanBak", "CleanSv", "CleanPermanent" })
            this.FindControl<Avalonia.Controls.Primitives.ToggleButton>(name)!.IsCheckedChanged += (_, _) => Vm.CleanupOptionsChanged();

        SetUpTitleBar();

        // picking a recent folder or a profile closes its flyout
        CloseFlyoutOnClick("RecentButton", "RecentList");
        CloseFlyoutOnClick("ProfilesButton", "ProfileList", onlyClass: "apply");

        AddHandler(DragDrop.DragOverEvent, (_, e) =>
            e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Link : DragDropEffects.None);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    MainViewModel Vm => (MainViewModel)DataContext!;

    void CloseFlyoutOnClick(string buttonName, string listName, string? onlyClass = null)
    {
        var button = this.FindControl<Button>(buttonName)!;
        if (button.Flyout is not Flyout { Content: Control list } || list.Name != listName) return;
        list.AddHandler(Button.ClickEvent, (_, e) =>
        {
            if (onlyClass is null || (e.Source as Button)?.Classes.Contains(onlyClass) == true) button.Flyout.Hide();
        });
    }

    // Room left free for the system window buttons when the window draws into the title bar.
    const double MacTrafficLightsWidth = 70;
    const double WindowsCaptionButtonsWidth = 138;

    /// <summary>
    /// The title bar is our own header. Leave room for the system buttons (right on Windows, left on macOS),
    /// and let the empty parts of the bar move the window. Linux keeps its own title bar.
    /// </summary>
    void SetUpTitleBar()
    {
        var bar = this.FindControl<Border>("TitleBar")!;
        var grid = this.FindControl<Grid>("TitleBarGrid")!;

        void UpdateInsets()
        {
            var extended = IsExtendedIntoWindowDecorations;
            grid.ColumnDefinitions[0].Width = new GridLength(extended && OperatingSystem.IsMacOS() ? MacTrafficLightsWidth : 0);
            grid.ColumnDefinitions[4].Width = new GridLength(extended && OperatingSystem.IsWindows() ? WindowsCaptionButtonsWidth : 0);
        }
        UpdateInsets();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsExtendedIntoWindowDecorationsProperty) UpdateInsets();
        };

        bar.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            if (e.ClickCount == 2)
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            else
                BeginMoveDrag(e);
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not MainViewModel vm) return;
        vm.AboutRequested += (_, _) => new AboutWindow().ShowDialog(this);
        vm.OpenLink = async url => await Launcher.LaunchUriAsync(new Uri(url));
        vm.BringToFront += (_, _) =>
        {
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
        };
        vm.UrlRequested += async (_, url) => await Launcher.LaunchUriAsync(new Uri(url));
        vm.ConfirmCleanup = ConfirmCleanup;
        vm.AskReportPath = AskReportPath;
        vm.ConfirmReplace = async count =>
        {
            var dialog = new ConfirmDialog(Loc.Instance["output.replace.confirm.title"],
                Loc.Instance.Format("output.replace.confirm.body", ("count", count)),
                [], Loc.Instance["output.replace.confirm.ok"], Loc.Instance["common.cancel"]);
            return await dialog.ShowDialog<bool>(this);
        };
    }

    /// <summary>A dropped folder opens it; dropped drawings are listed alone, like from the Explorer menu.</summary>
    void OnDrop(object? sender, DragEventArgs e)
    {
        var paths = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList() ?? [];
        if (paths.Count == 0) return;
        if (paths.Any(Directory.Exists) || paths.Any(p => PurgeCad.Core.Scanning.AuxiliaryRules.DrawingKindOf(p) is not null))
            Vm.OpenPaths(paths);
        else if (Path.GetDirectoryName(paths[0]) is { } folder)
            Vm.OpenPaths([folder]);
    }

    async Task<string?> PickFolder(string title)
    {
        var result = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return result.FirstOrDefault()?.TryGetLocalPath();
    }

    async Task<string?> AskReportPath()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Loc.Instance["summary.saveReport"],
            SuggestedFileName = $"purgecad-report-{DateTime.Now:yyyy-MM-dd_HHmm}.csv",
            DefaultExtension = "csv",
            FileTypeChoices = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }],
        });
        return file?.TryGetLocalPath();
    }

    async Task<bool> ConfirmCleanup(CleanupPlan plan)
    {
        var body = Loc.Instance.Format(Vm.Cleanup.Permanent ? "cleanup.confirm.bodyPermanent" : "cleanup.confirm.body",
            ("count", plan.Count), ("size", Loc.Instance.Size(plan.TotalBytes)));
        var dialog = new ConfirmDialog(Loc.Instance["cleanup.confirm.title"], body,
            plan.ToRemove.Select(i => i.File.RelativePath).ToList(), Loc.Instance["cleanup.run"], Loc.Instance["common.cancel"]);
        return await dialog.ShowDialog<bool>(this);
    }
}
