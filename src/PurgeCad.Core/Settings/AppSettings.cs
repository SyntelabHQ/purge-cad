using System.Text.Json;
using System.Text.Json.Serialization;
using PurgeCad.Core.Batch;
using PurgeCad.Core.Cleanup;

namespace PurgeCad.Core.Settings;

public enum ThemeChoice { Dark, Light }

/// <summary>Everything remembered between runs. Saved as JSON in the user's config folder.</summary>
public sealed class AppSettings
{
    /// <summary>Null until the welcome screen has been completed.</summary>
    public string? LanguageCode { get; set; }

    /// <summary>True when the user picked "System language" (follow the OS).</summary>
    public bool FollowSystemLanguage { get; set; } = true;

    public ThemeChoice Theme { get; set; } = ThemeChoice.Dark;

    public bool FirstRunCompleted { get; set; }

    /// <summary>
    /// Version of the start-up notice (no warranty, use at your own risk) the user accepted; 0 = never.
    /// Raise <see cref="CurrentNoticeVersion"/> when the notice changes, and everyone is asked again.
    /// </summary>
    public int AcceptedNoticeVersion { get; set; }

    public const int CurrentNoticeVersion = 1;

    [JsonIgnore]
    public bool NoticeAccepted => AcceptedNoticeVersion >= CurrentNoticeVersion;

    public string? LastFolder { get; set; }
    public bool IncludeSubfolders { get; set; } = true;
    public bool IncludeDwg { get; set; } = true;
    public bool IncludeDxf { get; set; } = true;

    public BatchSettings Batch { get; set; } = new();
    public AuxiliaryCleanupOptions Cleanup { get; set; } = new();

    /// <summary>Look for a newer version on start-up (GitHub releases).</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>A version the user chose to skip; it is not offered again (a later one is).</summary>
    public string? SkippedVersion { get; set; }

    /// <summary>accoreconsole.exe chosen by hand; null = find automatically.</summary>
    public string? AutoCadConsolePath { get; set; }
}

public static class SettingsStore
{
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>%APPDATA%\Syntelab\PurgeCAD on Windows, ~/.config/Syntelab/PurgeCAD on Linux and macOS.</summary>
    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Syntelab", "PurgeCAD");

    public static string FilePath => Path.Combine(Folder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new AppSettings();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // Unreadable settings must never stop the app; start with defaults.
        }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Folder);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Json));
        File.Move(temp, FilePath, overwrite: true);
    }
}
