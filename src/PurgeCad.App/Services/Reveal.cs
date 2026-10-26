using System.Diagnostics;
using System.Runtime.InteropServices;
using PurgeCad.Core.Branding;
using PurgeCad.Core.Updates;

namespace PurgeCad.App.Services;

/// <summary>Opens Explorer on a file (selected), and builds what a problem report needs.</summary>
public static class Reveal
{
    /// <summary>Explorer opens the file's folder with the file selected; elsewhere the folder opens.</summary>
    public static void File(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            else if (Path.GetDirectoryName(path) is { } folder)
                Process.Start(new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open", $"\"{folder}\"") { UseShellExecute = false });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException) { }
    }

    /// <summary>Version, system and engines: pasted into a report so we know where the problem happened.</summary>
    public static string SystemInfo()
    {
        var host = AppHost.Current;
        var engines = host.Engines.Engines.Select(e =>
        {
            var a = e.CheckAvailability();
            return $"{Loc.Instance[e.NameKey]}: " + (a.IsAvailable ? "available" : "not available" + (a.ReasonKey is { } k ? $" ({k})" : ""));
        });
        return string.Join("\n",
            $"{Brand.AppName} {UpdateChecker.CurrentVersion}" + (UpdateService.IsInstalled ? " (installed)" : " (not installed)"),
            $"System: {RuntimeInformation.OSDescription} {RuntimeInformation.OSArchitecture}",
            $".NET: {RuntimeInformation.FrameworkDescription}",
            $"Language: {Loc.Instance.Language.Code}",
            $"Engine chosen: {host.Settings.Batch.EngineId}",
            string.Join("\n", engines));
    }

    /// <summary>A new GitHub issue with the system info filled in (the user adds what happened and the log).</summary>
    public static string NewIssueUrl()
    {
        var body = "### What happened\n\n\n### What you expected\n\n\n"
                   + "### Log\nPlease attach the log of the drawing (in PurgeCAD: **Show log** next to the file). "
                   + "Never attach the drawing itself if it belongs to a client.\n\n"
                   + "### System\n```\n" + SystemInfo() + "\n```\n";
        return $"https://github.com/{Brand.UpdateRepository}/issues/new?title={Uri.EscapeDataString("Problem: ")}&body={Uri.EscapeDataString(body)}";
    }
}
