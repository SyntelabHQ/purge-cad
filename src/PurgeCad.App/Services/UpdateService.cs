using Avalonia.Threading;
using PurgeCad.Core.Branding;
using PurgeCad.Core.Updates;
using Velopack;
using Velopack.Sources;

namespace PurgeCad.App.Services;

/// <summary>A newer version and how to get it.</summary>
public sealed class AvailableUpdate(Version version, string pageUrl, Velopack.UpdateInfo? package)
{
    public Version Version { get; } = version;
    public string PageUrl { get; } = pageUrl;

    /// <summary>Set when the app was installed with the setup: the update installs itself with one click.</summary>
    internal Velopack.UpdateInfo? Package { get; } = package;

    public bool CanInstall => Package is not null;
}

/// <summary>
/// Updates from GitHub releases (<see cref="Brand.UpdateRepository"/>).
///  - Installed with PurgeCAD-win-Setup.exe (Velopack): the new version downloads and installs with one click, then
///    PurgeCAD restarts. Settings live in %APPDATA% and are kept.
///  - Run from a build folder or a copied folder: only a link to the release page (it cannot replace itself).
/// </summary>
public static class UpdateService
{
    static UpdateManager? _manager;

    static UpdateManager? Manager
    {
        get
        {
            if (_manager is not null) return _manager;
            try
            {
                var manager = new UpdateManager(new GithubSource($"https://github.com/{Brand.UpdateRepository}", null, false));
                return _manager = manager.IsInstalled ? manager : null;
            }
            catch (Exception) { return null; } // not installed by Velopack, or its files are missing
        }
    }

    public static bool IsInstalled => Manager is not null;

    /// <summary>The newer version, or null (none, offline, no release yet). Never throws.</summary>
    public static async Task<AvailableUpdate?> CheckAsync() => (await TryCheckAsync().ConfigureAwait(false)).Update;

    /// <summary>The newer version (or null), and whether the check worked at all (false = offline, no release yet).</summary>
    public static async Task<(bool Ok, AvailableUpdate? Update)> TryCheckAsync()
    {
        try
        {
            if (Manager is { } manager)
            {
                var info = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
                if (info is null) return (true, null);
                var v = info.TargetFullRelease.Version;
                return (true, new AvailableUpdate(new Version(v.Major, v.Minor, v.Patch),
                    $"https://github.com/{Brand.UpdateRepository}/releases/latest", info));
            }
            var (ok, release) = await UpdateChecker.TryCheckAsync().ConfigureAwait(false);
            return (ok, release is null ? null : new AvailableUpdate(release.Version, release.PageUrl, null));
        }
        catch (Exception)
        {
            return (false, null);
        }
    }

    /// <summary>
    /// Installs the update and restarts, reporting "Downloading… n %" through <paramref name="status"/> (on the UI thread).
    /// Returns false when it failed; the status then says so.
    /// </summary>
    public static async Task<bool> TryInstallAndRestartAsync(AvailableUpdate update, Action<string> status)
    {
        try
        {
            await InstallAndRestartAsync(update,
                p => Dispatcher.UIThread.Post(() => status(Loc.Instance.Format("update.downloading", ("percent", p)))));
            return true;
        }
        catch (Exception)
        {
            status(Loc.Instance["update.failed"]);
            return false;
        }
    }

    /// <summary>Downloads the update (progress 0-100), then closes PurgeCAD, installs it and starts the new version.</summary>
    public static async Task InstallAndRestartAsync(AvailableUpdate update, Action<int> progress, CancellationToken token = default)
    {
        if (Manager is not { } manager || update.Package is not { } package)
            throw new InvalidOperationException("This copy of PurgeCAD was not installed with the setup.");
        await manager.DownloadUpdatesAsync(package, progress, token).ConfigureAwait(false);
        manager.ApplyUpdatesAndRestart(package.TargetFullRelease);
    }
}
