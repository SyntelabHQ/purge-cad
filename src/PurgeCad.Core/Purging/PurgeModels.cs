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
}

public sealed class PurgeOptions
{
    /// <summary>Categories off by default because they remove geometry, not just unused definitions.</summary>
    public static readonly PurgeCategory[] Dangerous = [PurgeCategory.ZeroLengthGeometry, PurgeCategory.EmptyTexts];

    public HashSet<PurgeCategory> Categories { get; set; } =
        Enum.GetValues<PurgeCategory>().Except(Dangerous).ToHashSet();

    /// <summary>Run AUDIT before purging (AutoCAD engine only).</summary>
    public bool AuditFirst { get; set; }

    /// <summary>ACadSharp: process files that contain objects the library does not understand (risk of data loss).</summary>
    public bool AllowUnknownObjects { get; set; }

    /// <summary>ACadSharp: DWG 2007 (AC1021) cannot be written; when true, save it as DWG 2010 (AC1024) instead of skipping.</summary>
    public bool Upgrade2007To2010 { get; set; }

    public bool Includes(PurgeCategory category) => Categories.Contains(category);
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

    public static FilePurgeResult Skip(string reasonKey, string? detail = null) =>
        new() { Status = FileStatus.Skipped, ReasonKey = reasonKey, Detail = detail };

    public static FilePurgeResult Fail(string reasonKey, string? detail = null) =>
        new() { Status = FileStatus.Failed, ReasonKey = reasonKey, Detail = detail };
}
