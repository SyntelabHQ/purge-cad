using PurgeCad.Core.Purging;
using PurgeCad.Core.Scanning;

namespace PurgeCad.Core.Batch;

public enum OutputMode
{
    /// <summary>Write purged copies to another folder, keeping subfolders. Originals untouched. Default.</summary>
    CopyToFolder,
    /// <summary>Replace originals, after copying each one to _purgecad-backup/&lt;date&gt;/.</summary>
    InPlaceWithBackup,
    /// <summary>Dry-run: report what would be removed, write nothing.</summary>
    ReportOnly,
    /// <summary>Replace originals without a backup. The user confirms before the run.</summary>
    ReplaceOriginals,
}

public sealed class BatchSettings
{
    public OutputMode Mode { get; set; } = OutputMode.CopyToFolder;

    /// <summary>Destination for <see cref="OutputMode.CopyToFolder"/>.</summary>
    public string? OutputFolder { get; set; }

    /// <summary>Engine id or <see cref="EngineRegistry.AutomaticId"/>.</summary>
    public string EngineId { get; set; } = EngineRegistry.AutomaticId;

    /// <summary>Files processed at the same time (1–4); capped by the engine.</summary>
    public int Parallelism { get; set; } = 2;

    public PurgeOptions Purge { get; set; } = new();

    /// <summary>Per-file time limit.</summary>
    public TimeSpan FileTimeout { get; set; } = TimeSpan.FromMinutes(15);
}

/// <summary>One row of the results table and the report.</summary>
public sealed class FileReport
{
    public required FoundDrawing File { get; init; }
    public FileStatus Status { get; set; } = FileStatus.Pending;
    public string? ReasonKey { get; set; }
    public string? Detail { get; set; }
    public long SizeBefore { get; set; }
    public long? SizeAfter { get; set; }
    /// <summary>True in report-only mode: SizeAfter comes from a temporary copy that was deleted again.</summary>
    public bool SizeAfterIsEstimate { get; set; }
    public Dictionary<PurgeCategory, int> Removed { get; set; } = new();
    public string? EngineId { get; set; }
    /// <summary>The engine that could not do this file before <see cref="EngineId"/> took over (Automatic only).</summary>
    public string? FallbackFrom { get; set; }
    public string? Version { get; set; }
    /// <summary>Version code the file was saved in, when it was converted.</summary>
    public string? SavedAs { get; set; }
    public TimeSpan Duration { get; set; }
    /// <summary>The engine's log for this file from this run ("Show log"); null when the engine wrote none.</summary>
    public string? LogPath { get; set; }
    /// <summary>Where the result was written (output copy or the original in place); null for dry-run or skip.</summary>
    public string? OutputPath { get; set; }
}

/// <param name="Fraction">Overall progress 0..1, counting how far the running files are.</param>
public readonly record struct BatchProgress(int Done, int Total, string? CurrentFile, TimeSpan Elapsed, TimeSpan? Remaining,
    double Fraction = 0);

public sealed class BatchSummary
{
    public required IReadOnlyList<FileReport> Files { get; init; }
    public bool Cancelled { get; init; }
    public TimeSpan Elapsed { get; init; }
    public string? BackupFolder { get; init; }

    public int Count(FileStatus status) => Files.Count(f => f.Status == status);

    /// <summary>Bytes saved over all files that were written.</summary>
    public long BytesSaved => Files.Where(f => f.SizeAfter is not null).Sum(f => f.SizeBefore - f.SizeAfter!.Value);

    /// <summary>Total size of the files that have a size after (written or estimated).</summary>
    public long BytesBefore => Files.Where(f => f.SizeAfter is not null).Sum(f => f.SizeBefore);
    public long BytesAfter => Files.Where(f => f.SizeAfter is not null).Sum(f => f.SizeAfter!.Value);

    /// <summary>True when the sizes are estimates (report-only run).</summary>
    public bool IsEstimate => Files.Any(f => f.SizeAfterIsEstimate);
}
