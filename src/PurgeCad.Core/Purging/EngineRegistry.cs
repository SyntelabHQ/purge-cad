namespace PurgeCad.Core.Purging;

/// <summary>Known engines, in order of preference for the "Automatic" choice (safest first).</summary>
public sealed class EngineRegistry
{
    public const string AutomaticId = "auto";

    readonly List<IPurgeEngine> _engines = new();

    public IReadOnlyList<IPurgeEngine> Engines => _engines;

    public EngineRegistry Add(IPurgeEngine engine)
    {
        if (_engines.Any(e => e.Id == engine.Id))
            throw new InvalidOperationException($"Engine '{engine.Id}' is already registered.");
        _engines.Add(engine);
        return this;
    }

    public IPurgeEngine? Find(string id) => _engines.FirstOrDefault(e => e.Id == id);

    /// <summary>
    /// The engine to use for a choice. "auto" picks the lowest-risk engine available here.
    /// Returns null when the chosen engine is not available.
    /// </summary>
    public IPurgeEngine? Resolve(string choiceId)
    {
        if (choiceId == AutomaticId)
            return _engines.Where(e => e.CheckAvailability().IsAvailable).OrderBy(e => e.Risk).FirstOrDefault();

        var engine = Find(choiceId);
        return engine is not null && engine.CheckAvailability().IsAvailable ? engine : null;
    }

    /// <summary>
    /// The engine a run uses. Like <see cref="Resolve"/>, but "auto" with two engines available gets a fallback:
    /// the next engine tries the files the first one cannot do.
    /// </summary>
    public IPurgeEngine? ResolveForRun(string choiceId)
    {
        if (choiceId != AutomaticId) return Resolve(choiceId);
        var available = _engines.Where(e => e.CheckAvailability().IsAvailable).OrderBy(e => e.Risk).ToList();
        return available.Count switch
        {
            0 => null,
            1 => available[0],
            _ => new FallbackEngine(available[0], available[1]),
        };
    }
}
