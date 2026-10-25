using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using PurgeCad.Core.Branding;

namespace PurgeCad.Core.Updates;

/// <summary>A newer published version.</summary>
public sealed record UpdateInfo(Version Version, string Tag, string PageUrl, string Notes);

/// <summary>
/// Asks GitHub for the latest published release of <see cref="Brand.UpdateRepository"/> (drafts and pre-releases are
/// not returned by GitHub's "latest"). Only the version number goes out; nothing about the user or their files.
/// Every failure (offline, no release yet, rate limit) quietly means "no update".
/// </summary>
public static class UpdateChecker
{
    public static Version CurrentVersion { get; } =
        Assembly.GetEntryAssembly()?.GetName().Version is { } v ? new Version(v.Major, v.Minor, Math.Max(0, v.Build)) : new Version(0, 1, 0);

    public static async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default) =>
        (await TryCheckAsync(cancellationToken).ConfigureAwait(false)).Update;

    /// <summary>Like <see cref="CheckAsync"/>, and also says whether GitHub answered (Ok false = offline / no release yet).</summary>
    public static async Task<(bool Ok, UpdateInfo? Update)> TryCheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(Brand.AppName, CurrentVersion.ToString()));
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            var json = await http.GetStringAsync($"https://api.github.com/repos/{Brand.UpdateRepository}/releases/latest",
                cancellationToken).ConfigureAwait(false);
            return (true, Parse(json, CurrentVersion));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return (false, null);
        }
    }

    /// <summary>The release in GitHub's JSON, when it is newer than <paramref name="current"/>.</summary>
    public static UpdateInfo? Parse(string json, Version current)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean()) return null;
        if (root.TryGetProperty("prerelease", out var pre) && pre.GetBoolean()) return null;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (ParseVersion(tag) is not { } version || version <= current) return null;
        var page = root.TryGetProperty("html_url", out var url) ? url.GetString() : null;
        var notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "";
        return new UpdateInfo(version, tag, page ?? $"https://github.com/{Brand.UpdateRepository}/releases/latest", notes);
    }

    /// <summary>"v1.2.0", "1.2", "PurgeCAD-1.2.3" → 1.2.0 / 1.2.0 / 1.2.3; null when there is no version in it.</summary>
    public static Version? ParseVersion(string tag)
    {
        var start = tag.IndexOfAny("0123456789".ToCharArray());
        if (start < 0) return null;
        var digits = new string(tag[start..].TakeWhile(c => char.IsDigit(c) || c == '.').ToArray()).Trim('.');
        if (!Version.TryParse(digits.Contains('.') ? digits : digits + ".0", out var v)) return null;
        return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
    }
}
