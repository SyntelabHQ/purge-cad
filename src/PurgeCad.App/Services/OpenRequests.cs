using Avalonia.Threading;

namespace PurgeCad.App.Services;

/// <summary>
/// Folders and drawings to open: from the command line at start, or handed over by a later PurgeCAD (Explorer menu).
/// Kept until the main window takes them; after that each arrival is passed on at once, on the UI thread.
/// </summary>
public static class OpenRequests
{
    static readonly object Gate = new();
    static readonly List<string> Pending = new();
    static Action<IReadOnlyList<string>>? _handler;

    public static void Add(IReadOnlyList<string> paths)
    {
        Action<IReadOnlyList<string>>? handler;
        lock (Gate)
        {
            handler = _handler;
            if (handler is null) { Pending.AddRange(paths); return; }
        }
        Dispatcher.UIThread.Post(() => handler(paths));
    }

    /// <summary>The main window's handler; returns what arrived before it was there.</summary>
    public static IReadOnlyList<string> Attach(Action<IReadOnlyList<string>> handler)
    {
        lock (Gate)
        {
            _handler = handler;
            var waiting = Pending.ToList();
            Pending.Clear();
            return waiting;
        }
    }

    /// <summary>The paths among the start arguments (existing folders and files; options are left out).</summary>
    public static List<string> PathsIn(IEnumerable<string> args) =>
        args.Where(a => !a.StartsWith("--", StringComparison.Ordinal))
            .Select(a => a.Trim().Trim('"'))
            .Where(a => a.Length > 0 && (File.Exists(a) || Directory.Exists(a)))
            .ToList();
}
