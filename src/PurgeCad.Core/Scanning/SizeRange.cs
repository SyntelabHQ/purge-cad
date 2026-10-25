using System.Globalization;

namespace PurgeCad.Core.Scanning;

/// <summary>Drawing size limits in MB (1 MB = 1024 × 1024 bytes); either end may be open.</summary>
public sealed record SizeRange(double? MinMb, double? MaxMb)
{
    public static readonly SizeRange Any = new(null, null);

    public bool IsEmpty => MinMb is null && MaxMb is null;

    public bool Matches(long bytes)
    {
        var mb = bytes / (1024.0 * 1024.0);
        return (MinMb is not { } min || mb >= min) && (MaxMb is not { } max || mb <= max);
    }

    /// <summary>"12", "2.5", "2,5" → MB; empty or invalid → no limit.</summary>
    public static double? ParseMb(string? text)
    {
        var t = (text ?? "").Trim().Replace(',', '.');
        return t.Length > 0 && double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v >= 0 ? v : null;
    }
}

/// <summary>Name and size filters applied to a scan.</summary>
public static class ScanFilter
{
    /// <summary>
    /// The drawings that pass both filters, with their auxiliary files (.bak, .dwl, .sv$ go with the drawing of the
    /// same name). An auxiliary file whose drawing is not in the folder follows the name filter only.
    /// </summary>
    public static ScanResult Apply(ScanResult scan, NameFilter name, SizeRange size)
    {
        if (name.IsEmpty && size.IsEmpty) return scan;
        var result = new ScanResult { Root = scan.Root };
        result.Drawings.AddRange(scan.Drawings.Where(d => name.Matches(d.Path) && size.Matches(d.Size)));
        var comparer = OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        var all = scan.Drawings.Select(d => d.Path).ToHashSet(comparer);
        var kept = result.Drawings.Select(d => d.Path).ToHashSet(comparer);
        result.Auxiliary.AddRange(scan.Auxiliary.Where(a =>
            kept.Contains(a.DrawingPath) || (!all.Contains(a.DrawingPath) && name.Matches(a.DrawingPath))));
        result.InaccessibleFolders.AddRange(scan.InaccessibleFolders);
        return result;
    }
}
