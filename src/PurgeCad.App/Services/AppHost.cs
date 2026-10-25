using System.Globalization;
using PurgeCad.Core.Purging;
using PurgeCad.Core.Settings;
using PurgeCad.Engines.ACadSharp;
using PurgeCad.Engines.AutoCad;
using PurgeCad.Localization;

namespace PurgeCad.App.Services;

/// <summary>App-wide services, created once at start-up. Add new engines here.</summary>
public sealed class AppHost
{
    public static AppHost Current { get; } = new();

    AppHost()
    {
        Settings = SettingsStore.Load();
        Engines = new EngineRegistry()
            .Add(new AutoCadEngine(() => Settings.AutoCadConsolePath))
            .Add(new ACadSharpEngine());
    }

    public AppSettings Settings { get; }

    public EngineRegistry Engines { get; }

    public void SaveSettings() => SettingsStore.Save(Settings);

    /// <summary>Language to show: the saved one, or the OS language when "System language" is chosen.</summary>
    public string EffectiveLanguageCode =>
        Settings.FollowSystemLanguage || Settings.LanguageCode is null
            ? Languages.FromCulture(CultureInfo.CurrentUICulture.Name).Code
            : Settings.LanguageCode;

    public void ApplyLanguage()
    {
        Loc.Instance.SetLanguage(EffectiveLanguageCode);
    }
}
