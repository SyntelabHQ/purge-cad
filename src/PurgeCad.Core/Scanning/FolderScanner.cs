using System.Diagnostics;

namespace PurgeCad.Core.Scanning;

/// <summary>Finds drawings and auxiliary files under a folder, reporting live counts.</summary>
public static class FolderScanner
{
    /// <summary>Folder names PurgeCAD creates itself; never scanned.</summary>
    public static readonly string[] OwnFolderNames = ["_purgecad-backup", "_purgecad-deleted"];

    public static Task<ScanResult> ScanAsync(ScanOptions options, IProgress<ScanCounts>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Scan(options, progress, cancellationToken), cancellationToken);

    public static ScanResult Scan(ScanOptions options, IProgress<ScanCounts>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(options.Root);
        var result = new ScanResult { Root = root };
        var excluded = (options.ExcludedFolders ?? [])
            .Select(f => Path.TrimEndingDirectorySeparator(Path.GetFullPath(f)))
            .ToList();

        var folders = new Stack<string>();
        folders.Push(root);
        int scanned = 0;
        var lastReport = Stopwatch.StartNew();

        // Hidden and system files are included on purpose: .dwl files are often hidden.
        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = true,
            AttributesToSkip = 0,
            MatchCasing = MatchCasing.CaseInsensitive,
        };

        while (folders.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var folder = folders.Pop();
            scanned++;

            try
            {
                foreach (var file in Directory.EnumerateFiles(folder, "*", enumeration))
                    Classify(file, root, options, result);

                if (options.IncludeSubfolders)
                    foreach (var sub in Directory.EnumerateDirectories(folder, "*", enumeration))
                    {
                        var name = Path.GetFileName(sub);
                        if (OwnFolderNames.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                        if (excluded.Any(e => PathEquals(e, sub))) continue;
                        if (IsLink(sub)) continue; // avoid loops through symlinks / junctions
                        folders.Push(sub);
                    }
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException)
            {
                result.InaccessibleFolders.Add(folder);
            }

            if (progress is not null && lastReport.ElapsedMilliseconds >= 100)
            {
                progress.Report(result.Counts(scanned));
                lastReport.Restart();
            }
        }

        result.Drawings.Sort((a, b) => string.Compare(a.RelativePath, b.RelativePath, StringComparison.CurrentCultureIgnoreCase));
        progress?.Report(result.Counts(scanned));
        return result;
    }

    static void Classify(string file, string root, ScanOptions options, ScanResult result)
    {
        var relative = Path.GetRelativePath(root, file);

        if (AuxiliaryRules.DrawingKindOf(file) is { } kind)
        {
            if ((kind == DrawingKind.Dwg && !options.IncludeDwg) || (kind == DrawingKind.Dxf && !options.IncludeDxf)) return;
            var info = new FileInfo(file);
            result.Drawings.Add(new FoundDrawing(file, relative, kind, info.Length, info.IsReadOnly));
        }
        else if (options.IncludeAuxiliary && AuxiliaryRules.KindOf(file) is { } aux)
        {
            result.Auxiliary.Add(new FoundAuxiliary(file, relative, aux, new FileInfo(file).Length));
        }
    }

    static bool IsLink(string folder)
    {
        try { return new DirectoryInfo(folder).LinkTarget is not null; }
        catch (IOException) { return true; }
    }

    static bool PathEquals(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
