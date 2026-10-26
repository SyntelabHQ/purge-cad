namespace PurgeCad.Core.Purging;

/// <summary>Where the engines write one log per drawing (the last run of each file), for checking what happened.</summary>
public static class EngineLogs
{
    public static string Folder => Path.Combine(Path.GetTempPath(), "PurgeCAD", "logs");

    /// <summary>Log file for a drawing: its file name (made safe) plus <paramref name="suffix"/>, e.g. "plan.dwg.log".</summary>
    public static string PathFor(string source, string suffix = ".log")
    {
        var name = string.Concat(Path.GetFileName(source).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(Folder, name + suffix);
    }

    /// <summary>The log the engine <paramref name="engineId"/> writes for <paramref name="source"/>.</summary>
    public static string ForEngine(string source, string? engineId) =>
        PathFor(source, engineId == "acadsharp" ? ".acadsharp.log" : ".log");

    /// <summary>That log, when it was written since <paramref name="sinceUtc"/> (an older one is from another run).</summary>
    public static string? Written(string source, string? engineId, DateTime sinceUtc)
    {
        var path = ForEngine(source, engineId);
        try { return File.Exists(path) && File.GetLastWriteTimeUtc(path) >= sinceUtc ? path : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }
}
