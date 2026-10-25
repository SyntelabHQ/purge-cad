using PurgeCad.Core.Batch;
using PurgeCad.Core.Scanning;

namespace PurgeCad.Core.Cleanup;

public sealed class AuxiliaryCleanupOptions
{
    public bool LockFiles { get; set; }   // .dwl, .dwl2
    public bool Backups { get; set; }     // .bak
    public bool Autosaves { get; set; }   // .sv$

    /// <summary>Delete for good instead of using the Recycle Bin / Trash. Off by default.</summary>
    public bool Permanent { get; set; }

    public bool Any => LockFiles || Backups || Autosaves;

    public bool Includes(AuxiliaryKind kind) => kind switch
    {
        AuxiliaryKind.Lock => LockFiles,
        AuxiliaryKind.Backup => Backups,
        AuxiliaryKind.Autosave => Autosaves,
        _ => false,
    };
}

public sealed record CleanupItem(FoundAuxiliary File, bool WillRemove, string? SkipReasonKey, string? Detail = null);

/// <summary>What would be removed; shown to the user for confirmation before anything is deleted.</summary>
public sealed class CleanupPlan
{
    public required IReadOnlyList<CleanupItem> Items { get; init; }
    public IEnumerable<CleanupItem> ToRemove => Items.Where(i => i.WillRemove);
    public int Count => ToRemove.Count();
    public long TotalBytes => ToRemove.Sum(i => i.File.Size);
}

public sealed record CleanupOutcome(CleanupItem Item, RemovalResult Result);

/// <summary>Cleans .dwl / .dwl2 / .bak / .sv$ files. Works as part of a purge run or on its own.</summary>
public static class AuxiliaryCleaner
{
    public static CleanupPlan Plan(ScanResult scan, AuxiliaryCleanupOptions options)
    {
        var items = scan.Auxiliary
            .Where(a => options.Includes(a.Kind))
            .Select(a => a.Kind == AuxiliaryKind.Lock ? CheckLock(a) : new CleanupItem(a, true, null))
            .ToList();
        return new CleanupPlan { Items = items };
    }

    public static async Task<IReadOnlyList<CleanupOutcome>> ExecuteAsync(
        CleanupPlan plan, string root, AuxiliaryCleanupOptions options,
        IProgress<(int Done, int Total)>? progress = null, CancellationToken cancellationToken = default)
    {
        var remover = FileRemover.Create(root, options.Permanent);
        var outcomes = new List<CleanupOutcome>();
        var toRemove = plan.ToRemove.ToList();

        await Task.Run(() =>
        {
            foreach (var item in toRemove)
            {
                if (cancellationToken.IsCancellationRequested) break;

                // Check the lock again: the drawing may have been opened since the plan was made.
                var current = item.File.Kind == AuxiliaryKind.Lock ? CheckLock(item.File) : item;
                var result = current.WillRemove
                    ? remover.Remove(item.File.Path, item.File.RelativePath)
                    : RemovalResult.Skipped(current.SkipReasonKey!);
                outcomes.Add(new CleanupOutcome(current, result));
                progress?.Report((outcomes.Count, toRemove.Count));
            }
        }, cancellationToken);

        return outcomes;
    }

    /// <summary>A lock file may go only if its drawing is not open anywhere.</summary>
    static CleanupItem CheckLock(FoundAuxiliary lockFile)
    {
        var drawing = lockFile.DrawingPath;
        if (!File.Exists(drawing))
            return new CleanupItem(lockFile, true, null); // orphan lock file

        if (SafeFile.CanOpenExclusively(drawing))
            return new CleanupItem(lockFile, true, null);

        return new CleanupItem(lockFile, false, "cleanup.skip.drawingOpen", DwlOwner.Read(lockFile.Path));
    }
}

/// <summary>Reads who holds a drawing open from its .dwl file (user name, computer).</summary>
public static class DwlOwner
{
    public static string? Read(string dwlPath)
    {
        try
        {
            // .dwl is a short text file: user, computer, date — one per line. Encoding varies; Latin-1 never throws.
            using var stream = new FileStream(dwlPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, System.Text.Encoding.Latin1);
            var lines = new List<string>();
            for (var i = 0; i < 2 && reader.ReadLine() is { } line; i++)
                if (line.Trim().Length > 0) lines.Add(line.Trim());
            return lines.Count > 0 ? string.Join(" @ ", lines) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
