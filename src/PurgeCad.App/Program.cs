using Avalonia;
using Avalonia.Media;
using PurgeCad.App.Services;
using Velopack;

namespace PurgeCad.App;

static class Program
{
    const string Fonts = "avares://PurgeCAD/Assets/Fonts#";

    [STAThread]
    public static int Main(string[] args)
    {
        // Velopack's install / update / uninstall hooks run here and exit; first thing in Main, before any UI.
        // The hooks add / remove the Explorer right-click entry.
        var velopack = VelopackApp.Build();
        if (OperatingSystem.IsWindows())
            velopack = velopack
                .OnAfterInstallFastCallback(_ => ShellIntegration.Register())
                .OnAfterUpdateFastCallback(_ => ShellIntegration.Register())
                .OnBeforeUninstallFastCallback(_ => ShellIntegration.Unregister());
        velopack.Run();

        // PurgeCAD --run <folder> ...: no window, see CommandLine.
        if (CommandLine.IsCommand(args)) return CommandLine.Run(args);

        // A folder or drawings from Explorer: one window; a later start hands its paths to the first one.
        var paths = OpenRequests.PathsIn(args);
        if (!SingleInstance.Claim() && SingleInstance.Send(paths)) return 0;
        SingleInstance.Listen(OpenRequests.Add);
        if (paths.Count > 0) OpenRequests.Add(paths);

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new FontManagerOptions
            {
                DefaultFamilyName = Fonts + "Noto Sans",
                // Scripts not covered by Noto Sans (Arabic, Hebrew, Indic, Thai, CJK, ...) fall back
                // to the operating system fonts. Extra Noto script fonts can be added to Assets/Fonts later.
            })
            .LogToTrace();
}
