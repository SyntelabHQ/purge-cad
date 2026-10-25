using System.Runtime.Versioning;
using Microsoft.Win32;

namespace PurgeCad.Engines.AutoCad;

public sealed record AutoCadInstall(string Release, string ProductName, string Folder)
{
    public string ConsolePath => Path.Combine(Folder, "accoreconsole.exe");
}

/// <summary>
/// Finds installed AutoCAD versions that have accoreconsole.exe (full AutoCAD and verticals; AutoCAD LT has none).
/// Registry: HKLM\SOFTWARE\Autodesk\AutoCAD\R&lt;nn&gt;\ACAD-&lt;id&gt;:&lt;lang&gt; → AcadLocation.
/// </summary>
public static class AutoCadLocator
{
    public static IReadOnlyList<AutoCadInstall> FindAll()
    {
        if (!OperatingSystem.IsWindows()) return [];
        return FindOnWindows();
    }

    /// <summary>Newest install, or the one at <paramref name="manualConsolePath"/> when the user chose it.</summary>
    public static AutoCadInstall? Best(string? manualConsolePath)
    {
        if (!string.IsNullOrWhiteSpace(manualConsolePath) && File.Exists(manualConsolePath))
            return new AutoCadInstall("manual", Path.GetFileName(Path.GetDirectoryName(manualConsolePath)!),
                Path.GetDirectoryName(manualConsolePath)!);
        return FindAll().FirstOrDefault();
    }

    [SupportedOSPlatform("windows")]
    static List<AutoCadInstall> FindOnWindows()
    {
        var found = new List<AutoCadInstall>();
        using var autocad = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Autodesk\AutoCAD");
        if (autocad is null) return found;

        foreach (var release in autocad.GetSubKeyNames().Where(n => n.StartsWith('R')))
        {
            using var releaseKey = autocad.OpenSubKey(release);
            if (releaseKey is null) continue;
            foreach (var product in releaseKey.GetSubKeyNames().Where(n => n.StartsWith("ACAD-", StringComparison.OrdinalIgnoreCase)))
            {
                using var productKey = releaseKey.OpenSubKey(product);
                var folder = productKey?.GetValue("AcadLocation") as string;
                var name = productKey?.GetValue("ProductName") as string ?? product;
                if (folder is null || !File.Exists(Path.Combine(folder, "accoreconsole.exe"))) continue;
                found.Add(new AutoCadInstall(release, name, folder));
            }
        }

        // R25.0 > R24.3 > … : newest first.
        return found
            .OrderByDescending(i => Version.TryParse(i.Release.TrimStart('R'), out var v) ? v : new Version(0, 0))
            .ToList();
    }
}
