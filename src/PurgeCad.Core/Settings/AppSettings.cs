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

    /// <summary>Folders used lately, newest first (at most <see cref="MaxRecentFolders"/>).</summary>
    public List<string> RecentFolders { get; set; } = new();

    public const int MaxRecentFolders = 8;

    /// <summary>Puts a folder first in <see cref="RecentFolders"/>.</summary>
    public void RememberFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return;
        var path = Path.TrimEndingDirectorySeparator(folder);
        RecentFolders.RemoveAll(f => string.Equals(Path.TrimEndingDirectorySeparator(f), path,
            OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase));
        RecentFolders.Insert(0, path);
        if (RecentFolders.Count > MaxRecentFolders) RecentFolders.RemoveRange(MaxRecentFolders, RecentFolders.Count - MaxRecentFolders);
    }

    /// <summary>Named sets of options, applied with one click (and by name on the command line).</summary>
    public List<OptionProfile> Profiles { get; set; } = new();

    public OptionProfile? FindProfile(string name) =>
        Profiles.FirstOrDefault(p => string.Equals(p.Name, name.Trim(), StringComparison.CurrentCultureIgnoreCase));

    /// <summary>Saves the current options under a name, replacing a profile with the same name.</summary>
    public OptionProfile SaveProfile(string name)
    {
        var profile = OptionProfile.From(name.Trim(), this);
        var index = Profiles.FindIndex(p => string.Equals(p.Name, profile.Name, StringComparison.CurrentCultureIgnoreCase));
        if (index >= 0) Profiles[index] = profile; else Profiles.Add(profile);
        return profile;
    }

    public BatchSettings Batch { get; set; } = new();

    /// <summary>Messages (news.json) the user closed in the main window.</summary>
    public List<string> DismissedNews { get; set; } = new();

    /// <summary>The Convert tab's version (AC1027 = 2013 until the user picks one).</summary>
    public string ConvertVersion { get; set; } = "AC1027";
    public AuxiliaryCleanupOptions Cleanup { get; set; } = new();

    /// <summary>Look for a newer version on start-up (GitHub releases).</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>A version the user chose to skip; it is not offered again (a later one is).</summary>
    public string? SkippedVersion { get; set; }

    /// <summary>accoreconsole.exe chosen by hand; null = find automatically.</summary>
    public string? AutoCadConsolePath { get; set; }
}

/// <summary>A saved set of options: engine, where to save, what to purge, version, files at the same time.</summary>
public sealed class OptionProfile
{
    public string Name { get; set; } = "";
    public string EngineId { get; set; } = Purging.EngineRegistry.AutomaticId;
    public OutputMode Mode { get; set; } = OutputMode.CopyToFolder;
    public string? OutputFolder { get; set; }
    public int Parallelism { get; set; } = 2;
    public Purging.PurgeOptions Purge { get; set; } = new();
    public bool IncludeSubfolders { get; set; } = true;
    public bool IncludeDwg { get; set; } = true;
    public bool IncludeDxf { get; set; } = true;

    public static OptionProfile From(string name, AppSettings s) => new()
    {
        Name = name,
        EngineId = s.Batch.EngineId,
        Mode = s.Batch.Mode,
        OutputFolder = s.Batch.OutputFolder,
        Parallelism = s.Batch.Parallelism,
        Purge = s.Batch.Purge.Clone(),
        IncludeSubfolders = s.IncludeSubfolders,
        IncludeDwg = s.IncludeDwg,
        IncludeDxf = s.IncludeDxf,
    };

    /// <summary>Copies the profile into the settings (the options a run uses).</summary>
    public void ApplyTo(AppSettings s)
    {
        s.Batch.EngineId = EngineId;
        s.Batch.Mode = Mode;
        if (!string.IsNullOrWhiteSpace(OutputFolder)) s.Batch.OutputFolder = OutputFolder;
        s.Batch.Parallelism = Math.Clamp(Parallelism, 1, 4);
        s.Batch.Purge = Purge.Clone();
        s.IncludeSubfolders = IncludeSubfolders;
        s.IncludeDwg = IncludeDwg;
        s.IncludeDxf = IncludeDxf;
    }
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
