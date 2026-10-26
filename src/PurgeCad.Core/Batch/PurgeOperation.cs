using System.Diagnostics;
using PurgeCad.Core.Purging;
using PurgeCad.Core.Scanning;

namespace PurgeCad.Core.Batch;

/// <summary>Purges one drawing with the chosen engine, honouring the output mode and the safety rules in docs/SPEC.md §5.</summary>
public sealed class PurgeOperation(IPurgeEngine engine) : IBatchOperation
{
    public string Id => "purge";

    public int MaxParallelism => engine.MaxParallelism;

    public async Task ProcessAsync(FoundDrawing drawing, FileReport report, BatchContext context, CancellationToken cancellationToken)
    {
        var settings = context.Settings;
        var watch = Stopwatch.StartNew();
        report.EngineId = engine.Id;
        report.SizeBefore = drawing.Size;

        if (PreCheck(drawing, settings.Mode) is { } skip)
        {
            Apply(report, FilePurgeResult.Skip(skip));
            return;
        }

        var dryRun = settings.Mode == OutputMode.ReportOnly;
        var target = settings.Mode == OutputMode.CopyToFolder
            ? Path.Combine(settings.OutputFolder!, drawing.RelativePath)
            : drawing.Path;
        // Report only: the engine still writes, but to a private temp file that is measured and deleted,
        // so the run can show the size each drawing would have with the chosen engine.
        var temp = dryRun
            ? Path.Combine(Path.GetTempPath(), "PurgeCAD", Guid.NewGuid().ToString("N") + Path.GetExtension(drawing.Path))
            : SafeFile.TempPathFor(target);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(temp)!);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(settings.FileTimeout);

            // logs written from now on belong to this run (file times can be a little coarse)
            var started = DateTime.UtcNow.AddSeconds(-2);
            FilePurgeResult result;
            try
            {
                result = await engine.PurgeAsync(new FilePurgeRequest(drawing.Path, temp, DryRun: false, settings.Purge,
                    context.FileProgress is { } fileProgress ? new CallbackProgress<double>(f => fileProgress(drawing, f)) : null), timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                result = FilePurgeResult.Fail("fail.timeout");
            }

            Apply(report, result);
            report.LogPath = EngineLogs.Written(drawing.Path, report.EngineId, started);

            if (result.Status is FileStatus.Skipped or FileStatus.Failed)
                return;

            if (dryRun)
            {
                report.SizeAfter = result.Status == FileStatus.Purged && File.Exists(temp) ? new FileInfo(temp).Length : drawing.Size;
                report.SizeAfterIsEstimate = true;
                return;
            }

            if (result.Status == FileStatus.NothingToPurge || !File.Exists(temp))
            {
                // Nothing changed: in copy mode the output folder still gets the file, unchanged.
                SafeFile.TryDelete(temp);
                if (settings.Mode == OutputMode.CopyToFolder)
                    report.OutputPath = SafeFile.CopyKeepingStructure(drawing.Path, drawing.RelativePath, settings.OutputFolder!);
                report.SizeAfter = drawing.Size;
                return;
            }

            if (settings.Mode == OutputMode.InPlaceWithBackup)
                SafeFile.CopyKeepingStructure(drawing.Path, drawing.RelativePath, context.BackupFolder);

            SafeFile.Commit(temp, target);
            report.OutputPath = target;
            report.SizeAfter = new FileInfo(target).Length;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Apply(report, FilePurgeResult.Fail("fail.unexpected", e.Message));
        }
        finally
        {
            SafeFile.TryDelete(temp);
            report.Duration = watch.Elapsed;
        }
    }

    static string? PreCheck(FoundDrawing drawing, OutputMode mode)
    {
        if (!File.Exists(drawing.Path)) return "skip.missing";
        if (drawing.Size == 0) return "skip.empty";
        if (mode is OutputMode.InPlaceWithBackup or OutputMode.ReplaceOriginals && drawing.IsReadOnly) return "skip.readOnly";
        if (!SafeFile.CanOpenExclusively(drawing.Path))
            return File.Exists(Path.ChangeExtension(drawing.Path, ".dwl")) ? "skip.openElsewhere" : "skip.locked";
        return null;
    }

    void Apply(FileReport report, FilePurgeResult result)
    {
        report.EngineId = result.EngineId ?? engine.Id;
        report.FallbackFrom = result.FallbackFrom;
        report.SavedAs = result.SavedAs;
        report.Status = result.Status;
        report.ReasonKey = result.ReasonKey;
        report.Detail = result.Detail;
        report.Removed = result.Removed;
        report.Version = result.Version;
    }
}
