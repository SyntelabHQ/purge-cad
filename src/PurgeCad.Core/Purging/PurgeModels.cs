namespace PurgeCad.Core.Purging;

/// <summary>What a purge can remove. Same list for every engine; an engine declares which ones it supports.</summary>
public enum PurgeCategory
{
    Blocks,
    Layers,
    Linetypes,
    TextStyles,
    DimensionStyles,
    TableStyles,
    MultileaderStyles,
    MultilineStyles,
    Materials,
    ViewStyles,
    RegisteredApps,
    LayerFilters,
    DgnLinetypeComponents,
    AnnotationScales,
    /// <summary>Lines / polylines of zero length. Off by default.</summary>
    ZeroLengthGeometry,
    /// <summary>Empty texts and MTexts (not attributes). Off by default.</summary>
    EmptyTexts,
    /// <summary>Xrefs, images and PDF / DWF / DGN underlays whose file no longer exists: detached. Off by default.</summary>
    MissingReferences,
}

public sealed class PurgeOptions
{
    /// <summary>Categories off by default because they remove geometry, not just unused definitions.</summary>
    public static readonly PurgeCategory[] Dangerous = [PurgeCategory.ZeroLengthGeometry, PurgeCategory.EmptyTexts];

    /// <summary>Off until the user turns them on: the dangerous ones, and detaching missing references.</summary>
    public static readonly PurgeCategory[] OptIn = [.. Dangerous, PurgeCategory.MissingReferences];

    public HashSet<PurgeCategory> Categories { get; set; } =
        Enum.GetValues<PurgeCategory>().Except(OptIn).ToHashSet();

    /// <summary>Format to save in (version code such as "AC1027" = 2013); null keeps each drawing's own version.</summary>
    public string? SaveVersion { get; set; }

    /// <summary>Run AUDIT before purging (AutoCAD engine only).</summary>
    public bool AuditFirst { get; set; }

    /// <summary>ACadSharp: process files that contain objects the library does not understand (risk of data loss).</summary>
    public bool AllowUnknownObjects { get; set; }

    /// <summary>ACadSharp: DWG 2007 (AC1021) cannot be written; when true, save it as DWG 2010 (AC1024) instead of skipping.</summary>
    public bool Upgrade2007To2010 { get; set; }

    public bool Includes(PurgeCategory category) => Categories.Contains(category);

    /// <summary>The version to write for a drawing read as <paramref name="original"/>.</summary>
    public string? TargetVersion(string? original) => SaveVersion ?? original;

    /// <summary>True when the drawing must be written in another version than it has.</summary>
    public bool Converts(string? original) => SaveVersion is not null && SaveVersion != original;

    /// <summary>Nothing purged: the drawings are only saved in <paramref name="version"/> (the Convert tab).</summary>
    public static PurgeOptions ConvertOnly(string version, PurgeOptions? like = null) => new()
    {
        Categories = [],
        SaveVersion = version,
        AllowUnknownObjects = like?.AllowUnknownObjects ?? false,
        Upgrade2007To2010 = like?.Upgrade2007To2010 ?? false,
    };

    /// <summary>True when nothing is to be purged and the drawing is already in the version asked for.</summary>
    public bool NothingToDo(string? original) => Categories.Count == 0 && !Converts(original);

    public PurgeOptions Clone() => new()
    {
        Categories = [.. Categories],
        AuditFirst = AuditFirst,
        AllowUnknownObjects = AllowUnknownObjects,
        Upgrade2007To2010 = Upgrade2007To2010,
        SaveVersion = SaveVersion,
    };
}

/// <summary>The DWG / DXF versions a drawing can be saved as, newest first.</summary>
public static class SaveVersions
{
    public static readonly IReadOnlyList<(string Code, string Name)> All =
    [
        ("AC1032", "2018"),
        ("AC1027", "2013"),
        ("AC1024", "2010"),
        ("AC1021", "2007"),
        ("AC1018", "2004"),
        ("AC1015", "2000"),
    ];

    /// <summary>"2013" → "AC1027", "AC1027" → "AC1027"; null when unknown.</summary>
    public static string? Parse(string text)
    {
        var t = text.Trim();
        foreach (var (code, name) in All)
            if (string.Equals(t, code, StringComparison.OrdinalIgnoreCase) || t == name) return code;
        return null;
    }

    /// <summary>"AC1027" → "2013"; the code itself when unknown.</summary>
    public static string NameOf(string code) => All.FirstOrDefault(v => v.Code == code).Name ?? code;
}

public enum RiskLevel { VeryLow, Low, Medium, High }

/// <summary>Whether an engine can run here; when not, a localization key with the reason.</summary>
public sealed record EngineAvailability(bool IsAvailable, string? ReasonKey = null)
{
    public static readonly EngineAvailability Available = new(true);
    public static EngineAvailability Unavailable(string reasonKey) => new(false, reasonKey);
}

public sealed record FilePurgeRequest(
    string SourcePath,
    // Where to write the purged file (a temp path chosen by the batch runner). Ignored when DryRun.
    string OutputPath,
    bool DryRun,
    PurgeOptions Options,
    // How far the engine is with this file (0..1), for engines that can tell. Called from any thread.
    IProgress<double>? Progress = null);

/// <summary>IProgress that calls back on the reporting thread (Progress&lt;T&gt; would post to the UI thread).</summary>
public sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
{
    public void Report(T value) => callback(value);
}

public enum FileStatus
{
    Pending,
    Purged,
    NothingToPurge,
    Skipped,
    Failed,
}

public sealed class FilePurgeResult
{
    public FileStatus Status { get; init; }

    /// <summary>Localization key explaining a skip or failure (e.g. "skip.versionTooNew").</summary>
    public string? ReasonKey { get; init; }

    /// <summary>Technical detail (exception message), shown in the report only.</summary>
    public string? Detail { get; init; }

    public Dictionary<PurgeCategory, int> Removed { get; init; } = new();

    /// <summary>File format version as read, e.g. "AC1027".</summary>
    public string? Version { get; init; }

    public int TotalRemoved => Removed.Values.Sum();

    /// <summary>Engine that produced this result, when it is not the one the run was started with (fallback).</summary>
    public string? EngineId { get; set; }

    /// <summary>The engine that could not do the file before <see cref="EngineId"/> took over; null without a fallback.</summary>
    public string? FallbackFrom { get; set; }

    /// <summary>Version code the drawing was written in, when it differs from <see cref="Version"/>.</summary>
    public string? SavedAs { get; init; }

    public static FilePurgeResult Skip(string reasonKey, string? detail = null) =>
        new() { Status = FileStatus.Skipped, ReasonKey = reasonKey, Detail = detail };

    public static FilePurgeResult Fail(string reasonKey, string? detail = null) =>
        new() { Status = FileStatus.Failed, ReasonKey = reasonKey, Detail = detail };
}
