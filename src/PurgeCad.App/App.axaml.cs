using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PurgeCad.App.Services;
using PurgeCad.App.ViewModels;
using PurgeCad.App.Views;

namespace PurgeCad.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var host = AppHost.Current;
        host.ApplyLanguage();
        ThemeService.Apply(host.Settings.Theme);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Every start: language, theme and the no-warranty notice (ticked each time), then the main window.
            desktop.MainWindow = new WelcomeWindow { DataContext = new WelcomeViewModel(OpenMain(desktop)) };
        }

        base.OnFrameworkInitializationCompleted();
    }

    static Action<AvailableUpdate?> OpenMain(IClassicDesktopStyleApplicationLifetime desktop) => update =>
    {
        // the Explorer entry, in the chosen language (only for the installed app, not a development build)
        if (OperatingSystem.IsWindows() && UpdateService.IsInstalled)
            ShellIntegration.Register(Loc.Instance["shell.folder"], Loc.Instance["shell.files"]);

        var previous = desktop.MainWindow;
        desktop.MainWindow = new MainWindow { DataContext = new MainViewModel(update) };
        desktop.MainWindow.Show();
        previous?.Close();
    };
}
