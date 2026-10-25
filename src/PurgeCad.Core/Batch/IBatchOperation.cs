using PurgeCad.Core.Scanning;

namespace PurgeCad.Core.Batch;

/// <summary>
/// Something done to every drawing in a batch. The runner handles order, parallelism, progress, cancel and the report;
/// an operation only processes one file. Purge is the first one; convert / audit can be added the same way.
/// </summary>
public interface IBatchOperation
{
    string Id { get; }

    int MaxParallelism { get; }

    Task ProcessAsync(FoundDrawing drawing, FileReport report, BatchContext context, CancellationToken cancellationToken);
}

/// <summary>Shared state of one run, given to the operation for each file.</summary>
public sealed class BatchContext
{
    public required string Root { get; init; }
    public required BatchSettings Settings { get; init; }

    /// <summary>Backup folder for in-place mode, created on first use.</summary>
    public required string BackupFolder { get; init; }

    /// <summary>How far the operation is with one file (0..1); set by the batch runner.</summary>
    public Action<FoundDrawing, double>? FileProgress { get; init; }
}
