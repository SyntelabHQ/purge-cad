namespace PurgeCad.Engines.AutoCad;

/// <summary>
/// Where the PurgeCAD plugin for accoreconsole lies: next to PurgeCAD.exe, in acad-plugin\net462 (AutoCAD 2013 - 2024,
/// .NET Framework) or acad-plugin\net8.0-windows (AutoCAD 2025 and newer, .NET 8). The app build copies both there.
/// </summary>
public static class AutoCadPlugin
{
    public const string FileName = "PurgeCad.AutoCad.Plugin.dll";

    /// <summary>Folder of the plugin build an AutoCAD release loads; null before AutoCAD 2013 (R19).</summary>
    public static string? FolderFor(int releaseMajor) => releaseMajor switch
    {
        < 19 => null,
        < 25 => "net462",
        _ => "net8.0-windows",
    };

    /// <summary>The plugin for an AutoCAD release, or null when it is not there (then the plain script runs).</summary>
    public static string? PathFor(int releaseMajor, string? baseDirectory = null)
    {
        if (FolderFor(releaseMajor) is not { } folder) return null;
        var path = Path.Combine(baseDirectory ?? AppContext.BaseDirectory, "acad-plugin", folder, FileName);
        return File.Exists(path) ? path : null;
    }
}
