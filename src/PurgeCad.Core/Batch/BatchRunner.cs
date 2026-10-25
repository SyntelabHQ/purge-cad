using System.Diagnostics;
using PurgeCad.Core.Scanning;

namespace PurgeCad.Core.Batch;

/// <summary>
/// Runs an operation on every drawing of a scan: parallel up to the limit, with progress, stop, and a report row per file.
/// Stop finishes the files already started, then returns.
/// </summary>
public static class BatchRunner
{
    public static async Task<BatchSummary> RunAsync(
        ScanResult scan,
        IBatchOperation operation,
        BatchSettings settings,
        IProgress<BatchProgress>? progress = null,
        Action<FileReport>? fileFinished = null,
        CancellationToken cancellationToken = default)
    {
        if (settings.Mode == OutputMode.CopyToFolder && string.IsNullOrWhiteSpace(settings.OutputFolder))
            throw new ArgumentException("An output folder is required in copy mode.", nameof(settings));

        var watch = Stopwatch.StartNew();
        int done = 0;
        // How far each running file is (0..1), so the bar moves while a long file is purged.
        var partial = new System.Collections.Concurrent.ConcurrentDictionary<string, double>();
        long lastReport = 0;
        var total = scan.Drawings.Count;

        var context = new BatchContext
        {
            FileProgress = (drawing, fraction) =>
            {
                partial[drawing.Path] = Math.Clamp(fraction, 0, 1);
                var now = watch.ElapsedMilliseconds;
                if (now - Interlocked.Read(ref lastReport) < 250) return;
                Interlocked.Exchange(ref lastReport, now);
                progress?.Report(Snapshot(Volatile.Read(ref done), total, null, watch.Elapsed, partial.Values.Sum()));
            },
            Root = scan.Root,
            Settings = settings,
            BackupFolder = Path.Combine(scan.Root, "_purgecad-backup", DateTime.Now.ToString("yyyy-MM-dd_HHmm")),
        };

        var reports = scan.Drawings.Select(d => new FileReport { File = d, SizeBefore = d.Size }).ToList();
        var parallelism = Math.Clamp(Math.Min(settings.Parallelism, operation.MaxParallelism), 1, 4);
        bool cancelled = false;

        // Cancelling stops new files from starting; files already running are finished so none is left half-done.
        var queue = new Queue<FileReport>(reports);
        var gate = new object();

        async Task Worker()
        {
            while (true)
            {
                FileReport? next;
                lock (gate)
                {
                    if (cancellationToken.IsCancellationRequested) { cancelled = true; return; }
                    if (!queue.TryDequeue(out next)) return;
                }

                partial[next.File.Path] = 0;
                progress?.Report(Snapshot(Volatile.Read(ref done), reports.Count, next.File.RelativePath, watch.Elapsed, partial.Values.Sum()));
                await operation.ProcessAsync(next.File, next, context, CancellationToken.None).ConfigureAwait(false);
                fileFinished?.Invoke(next);

                partial.TryRemove(next.File.Path, out _);
                var finished = Interlocked.Increment(ref done);
                progress?.Report(Snapshot(finished, reports.Count, null, watch.Elapsed, partial.Values.Sum()));
            }
        }

        await Task.WhenAll(Enumerable.Range(0, parallelism).Select(_ => Task.Run(Worker))).ConfigureAwait(false);

        return new BatchSummary
        {
            Files = reports,
            Cancelled = cancelled,
            Elapsed = watch.Elapsed,
            BackupFolder = Directory.Exists(context.BackupFolder) ? context.BackupFolder : null,
        };
    }

    static BatchProgress Snapshot(int done, int total, string? current, TimeSpan elapsed, double running)
    {
        var fraction = total == 0 ? 1 : Math.Clamp((done + running) / total, 0, 1);
        // No estimate until there is something to go on: files skipped in a blink would promise "0 s left".
        TimeSpan? remaining = fraction > 0.01 && fraction < 1 && elapsed >= TimeSpan.FromSeconds(5)
            ? TimeSpan.FromTicks((long)(elapsed.Ticks * (1 - fraction) / fraction))
            : null;
        if (remaining < TimeSpan.FromSeconds(1)) remaining = null;
        return new BatchProgress(done, total, current, elapsed, remaining, fraction);
    }
}
