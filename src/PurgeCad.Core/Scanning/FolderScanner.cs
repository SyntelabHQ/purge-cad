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

    /// <summary>
    /// A "scan" of chosen files only (from the Explorer menu or the command line): the drawings among them, with
    /// paths relative to the folder they share, and the lock / backup / autosave files that belong to them.
    /// </summary>
    public static ScanResult FromFiles(IEnumerable<string> files)
    {
        var drawings = files.Select(Path.GetFullPath)
            .Where(f => AuxiliaryRules.DrawingKindOf(f) is not null && File.Exists(f))
            .Distinct(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .ToList();
        var root = CommonFolder(drawings);
        var result = new ScanResult { Root = root };
        var options = new ScanOptions(root);
        foreach (var file in drawings)
        {
            Classify(file, root, options, result);
            var folder = Path.GetDirectoryName(file)!;
            var name = Path.GetFileNameWithoutExtension(file);
            foreach (var ext in new[] { ".dwl", ".dwl2", ".bak", ".sv$" })
            {
                var aux = Path.Combine(folder, name + ext);
                if (File.Exists(aux)) Classify(aux, root, options, result);
            }
        }
        result.Drawings.Sort((a, b) => string.Compare(a.RelativePath, b.RelativePath, StringComparison.CurrentCultureIgnoreCase));
        return result;
    }

    /// <summary>The deepest folder that holds all the files ("" for none).</summary>
    public static string CommonFolder(IReadOnlyList<string> files)
    {
        if (files.Count == 0) return "";
        var common = Path.GetDirectoryName(files[0])!;
        foreach (var file in files.Skip(1))
            while (!IsUnder(file, common) && Path.GetDirectoryName(common) is { } parent)
                common = parent;
        return common;
    }

    static bool IsUnder(string file, string folder)
    {
        var relative = Path.GetRelativePath(folder, file);
        return !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative);
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
