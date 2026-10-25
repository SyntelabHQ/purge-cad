namespace PurgeCad.Core.Scanning;

public enum DrawingKind { Dwg, Dxf }

/// <summary>Files CAD programs leave next to drawings.</summary>
public enum AuxiliaryKind
{
    /// <summary>.dwl / .dwl2 lock files (exist while the drawing is open).</summary>
    Lock,
    /// <summary>.bak backup of the previous save.</summary>
    Backup,
    /// <summary>.sv$ autosave.</summary>
    Autosave,
}

public sealed record FoundDrawing(string Path, string RelativePath, DrawingKind Kind, long Size, bool IsReadOnly);

public sealed record FoundAuxiliary(string Path, string RelativePath, AuxiliaryKind Kind, long Size)
{
    /// <summary>The drawing this file belongs to (same name, .dwg extension).</summary>
    public string DrawingPath => AuxiliaryRules.DrawingFor(Path);
}

public sealed record ScanOptions(
    string Root,
    bool IncludeSubfolders = true,
    bool IncludeDwg = true,
    bool IncludeDxf = true,
    bool IncludeAuxiliary = true,
    // Folders not to scan, e.g. the output folder when it is inside the root.
    IReadOnlyList<string>? ExcludedFolders = null);

/// <summary>Live counters while scanning.</summary>
public readonly record struct ScanCounts(int Dwg, int Dxf, int Auxiliary, int FoldersScanned);

public sealed class ScanResult
{
    public required string Root { get; init; }
    public List<FoundDrawing> Drawings { get; } = new();
    public List<FoundAuxiliary> Auxiliary { get; } = new();
    /// <summary>Folders that could not be read (no permission, removed during the scan).</summary>
    public List<string> InaccessibleFolders { get; } = new();

    public ScanCounts Counts(int folders) => new(
        Drawings.Count(d => d.Kind == DrawingKind.Dwg),
        Drawings.Count(d => d.Kind == DrawingKind.Dxf),
        Auxiliary.Count,
        folders);
}

public static class AuxiliaryRules
{
    /// <summary>Kind of an auxiliary file by extension, or null if it is not one.</summary>
    public static AuxiliaryKind? KindOf(string path) => System.IO.Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".dwl" or ".dwl2" => AuxiliaryKind.Lock,
        ".bak" => AuxiliaryKind.Backup,
        ".sv$" => AuxiliaryKind.Autosave,
        _ => null,
    };

    public static string DrawingFor(string auxiliaryPath) => System.IO.Path.ChangeExtension(auxiliaryPath, ".dwg");

    public static DrawingKind? DrawingKindOf(string path) => System.IO.Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".dwg" => DrawingKind.Dwg,
        ".dxf" => DrawingKind.Dxf,
        _ => null,
    };
}
