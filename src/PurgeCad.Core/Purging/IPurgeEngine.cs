namespace PurgeCad.Core.Purging;

/// <summary>
/// A purge engine. To add one: implement this interface in its own project and register it in <see cref="EngineRegistry"/>.
/// Engines must never write to <see cref="FilePurgeRequest.SourcePath"/>; they write only to <see cref="FilePurgeRequest.OutputPath"/>.
/// </summary>
public interface IPurgeEngine : IAsyncDisposable
{
    /// <summary>Stable id, stored in settings and reports ("autocad", "acadsharp").</summary>
    string Id { get; }

    /// <summary>Localization key of the display name.</summary>
    string NameKey { get; }

    RiskLevel Risk { get; }

    /// <summary>Localization keys of the risks / limits shown next to the engine.</summary>
    IReadOnlyList<string> RiskKeys { get; }

    IReadOnlySet<PurgeCategory> SupportedCategories { get; }

    /// <summary>Maximum files processed at the same time by this engine.</summary>
    int MaxParallelism { get; }

    EngineAvailability CheckAvailability();

    Task<FilePurgeResult> PurgeAsync(FilePurgeRequest request, CancellationToken cancellationToken);
}
