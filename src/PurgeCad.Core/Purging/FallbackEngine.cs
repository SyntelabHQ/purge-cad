namespace PurgeCad.Core.Purging;

/// <summary>
/// "Automatic": the first engine does each file; when it cannot (AutoCAD cannot open the file, gets stuck, or the
/// file is newer than the installed AutoCAD), the second engine tries the same file. The second engine keeps its own
/// safety checks (ACadSharp skips files it cannot keep whole), so a fallback never takes more risk than choosing it.
/// </summary>
public sealed class FallbackEngine(IPurgeEngine primary, IPurgeEngine backup) : IPurgeEngine
{
    public IPurgeEngine Primary => primary;
    public IPurgeEngine Backup => backup;

    public string Id => primary.Id;
    public string NameKey => primary.NameKey;
    public RiskLevel Risk => primary.Risk;
    public IReadOnlyList<string> RiskKeys => primary.RiskKeys;
    public IReadOnlySet<PurgeCategory> SupportedCategories => primary.SupportedCategories;
    public int MaxParallelism => primary.MaxParallelism;
    public EngineAvailability CheckAvailability() => primary.CheckAvailability();

    /// <summary>Results after which the second engine tries: the first one could not handle this file.</summary>
    public static bool ShouldFallBack(FilePurgeResult result) =>
        result.Status is FileStatus.Failed or FileStatus.Skipped
        && result.ReasonKey is { } key
        && (key.StartsWith("engine.", StringComparison.Ordinal) || key == "skip.versionTooNew");

    public async Task<FilePurgeResult> PurgeAsync(FilePurgeRequest request, CancellationToken cancellationToken)
    {
        var first = await primary.PurgeAsync(request, cancellationToken);
        if (!ShouldFallBack(first)) return first;

        cancellationToken.ThrowIfCancellationRequested();
        var second = await backup.PurgeAsync(request, cancellationToken);
        if (second.Status is FileStatus.Purged or FileStatus.NothingToPurge)
        {
            second.EngineId = backup.Id;
            second.FallbackFrom = primary.Id;
            return second;
        }
        // Neither could: report the first engine's reason, with what the second one said.
        return new FilePurgeResult
        {
            Status = first.Status,
            ReasonKey = first.ReasonKey,
            Detail = (first.Detail is { Length: > 0 } d ? d + "\n\n" : "")
                     + $"{backup.Id}: {second.ReasonKey}{(second.Detail is { Length: > 0 } sd ? " – " + sd : "")}",
            Version = first.Version ?? second.Version,
        };
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
