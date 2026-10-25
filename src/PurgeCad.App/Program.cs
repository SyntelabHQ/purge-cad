using Avalonia;
using Avalonia.Media;
using Velopack;

namespace PurgeCad.App;

static class Program
{
    const string Fonts = "avares://PurgeCAD/Assets/Fonts#";

    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack's install / update / uninstall hooks run here and exit; first thing in Main, before any UI.
        VelopackApp.Build().Run();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
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
