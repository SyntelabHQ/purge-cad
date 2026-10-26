using System.Runtime.Versioning;
using Microsoft.Win32;

namespace PurgeCad.App.Services;

/// <summary>
/// The Windows Explorer right-click entry: on folders (and the empty space inside a folder) and on .dwg / .dxf files.
/// It opens PurgeCAD with that folder, or with the chosen drawings ticked. Written for the current user only
/// (HKCU\Software\Classes, no administrator rights), on install, on update and at every start (so the text follows
/// the chosen language); removed on uninstall. On Windows 11 it sits under "Show more options".
/// </summary>
public static class ShellIntegration
{
    const string Verb = "PurgeCAD";

    static readonly string[] FolderKeys = [@"Software\Classes\Directory\shell\" + Verb, @"Software\Classes\Directory\Background\shell\" + Verb];
    static readonly string[] FileKeys =
    [
        @"Software\Classes\SystemFileAssociations\.dwg\shell\" + Verb,
        @"Software\Classes\SystemFileAssociations\.dxf\shell\" + Verb,
    ];

    /// <summary>Adds or refreshes the entries. Never throws: the menu is a convenience.</summary>
    public static void Register(string? folderText = null, string? filesText = null)
    {
        if (!OperatingSystem.IsWindows() || Environment.ProcessPath is not { } exe) return;
        try
        {
            Write(FolderKeys[0], folderText ?? "Purge with PurgeCAD", exe, "%1", multiple: false);
            Write(FolderKeys[1], folderText ?? "Purge with PurgeCAD", exe, "%V", multiple: false);
            foreach (var key in FileKeys) Write(key, filesText ?? "Open in PurgeCAD", exe, "%1", multiple: true);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException) { }
    }

    public static void Unregister()
    {
        if (!OperatingSystem.IsWindows()) return;
        foreach (var key in FolderKeys.Concat(FileKeys))
            try { Registry.CurrentUser.DeleteSubKeyTree(key, throwOnMissingSubKey: false); }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException) { }
    }

    [SupportedOSPlatform("windows")]
    static void Write(string path, string text, string exe, string argument, bool multiple)
    {
        using var key = Registry.CurrentUser.CreateSubKey(path);
        key.SetValue("MUIVerb", text);
        key.SetValue("Icon", $"\"{exe}\",0");
        // more than 15 selected files: still one entry (each file starts PurgeCAD; the first instance collects them)
        if (multiple) key.SetValue("MultiSelectModel", "Player");
        using var command = key.CreateSubKey("command");
        command.SetValue("", $"\"{exe}\" \"{argument}\"");
    }
}
