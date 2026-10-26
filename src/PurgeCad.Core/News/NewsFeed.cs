using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using PurgeCad.Core.Branding;
using PurgeCad.Core.Updates;

namespace PurgeCad.Core.News;

/// <summary>A message from the publisher, shown in the main window (newest one) and in About → Messages.</summary>
public sealed record NewsItem(string Id, DateOnly? Date, string Title, string Text, string? Link, bool IsWarning);

/// <summary>A published release, for About → What's new.</summary>
public sealed record ReleaseNote(string Tag, DateOnly? Date, string Notes, string PageUrl);

/// <summary>
/// Messages come from <c>news.json</c> in the repository (see docs/NEWS.md): no server, and nothing about the user
/// is sent. Each message can be limited to some versions, languages or dates. Every failure means "no messages".
/// </summary>
public static class NewsFeed
{
    public static string Url => Environment.GetEnvironmentVariable("PURGECAD_NEWS")
                                ?? $"https://raw.githubusercontent.com/{Brand.UpdateRepository}/main/news.json";

    public static async Task<IReadOnlyList<NewsItem>> LoadAsync(string language, CancellationToken ct = default)
    {
        try
        {
            var source = Url;
            var json = source.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? await Http().GetStringAsync(source, ct).ConfigureAwait(false)
                : await File.ReadAllTextAsync(source, ct).ConfigureAwait(false);
            return Parse(json, language, UpdateChecker.CurrentVersion, DateOnly.FromDateTime(DateTime.Today));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException
                                      or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return [];
        }
    }

    /// <summary>
    /// The messages for this version, language and day, newest first. A text is either a string or an object by
    /// language code ({"en": "...", "ro": "..."}), English when the language has none.
    /// </summary>
    public static IReadOnlyList<NewsItem> Parse(string json, string language, Version version, DateOnly today)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array) return [];
        var list = new List<NewsItem>();
        foreach (var m in messages.EnumerateArray())
        {
            var id = Str(m, "id");
            var title = Text(m, "title", language);
            if (id is null || string.IsNullOrWhiteSpace(title)) continue;
            if (Str(m, "minVersion") is { } min && UpdateChecker.ParseVersion(min) is { } lo && version < lo) continue;
            if (Str(m, "maxVersion") is { } max && UpdateChecker.ParseVersion(max) is { } hi && version > hi) continue;
            if (Date(Str(m, "until")) is { } until && today > until) continue;
            if (m.TryGetProperty("languages", out var langs) && langs.ValueKind == JsonValueKind.Array
                && !langs.EnumerateArray().Any(l => string.Equals(l.GetString(), language, StringComparison.OrdinalIgnoreCase)))
                continue;
            list.Add(new NewsItem(id, Date(Str(m, "date")), title!, Text(m, "text", language) ?? "", Str(m, "link"),
                string.Equals(Str(m, "level"), "warning", StringComparison.OrdinalIgnoreCase)));
        }
        return list.OrderByDescending(n => n.Date ?? DateOnly.MinValue).ToList();
    }

    /// <summary>The last few releases (newest first), with their notes.</summary>
    public static async Task<IReadOnlyList<ReleaseNote>> ReleasesAsync(int count = 5, CancellationToken ct = default)
    {
        try
        {
            var json = await Http().GetStringAsync(
                $"https://api.github.com/repos/{Brand.UpdateRepository}/releases?per_page={count}", ct).ConfigureAwait(false);
            return ParseReleases(json);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return [];
        }
    }

    public static IReadOnlyList<ReleaseNote> ParseReleases(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];
        var list = new List<ReleaseNote>();
        foreach (var r in doc.RootElement.EnumerateArray())
        {
            if (r.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True) continue;
            if (r.TryGetProperty("prerelease", out var p) && p.ValueKind == JsonValueKind.True) continue;
            var tag = Str(r, "tag_name") ?? "";
            var published = Str(r, "published_at") is { } at && DateTime.TryParse(at, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal, out var t) ? DateOnly.FromDateTime(t) : (DateOnly?)null;
            list.Add(new ReleaseNote(tag, published, Plain(Str(r, "body") ?? ""),
                Str(r, "html_url") ?? $"https://github.com/{Brand.UpdateRepository}/releases"));
        }
        return list;
    }

    /// <summary>Release notes are Markdown; the window shows them as plain text (headings and bold marks dropped).</summary>
    public static string Plain(string markdown) => string.Join("\n", markdown.Replace("\r", "").Split('\n')
            .Select(l => l.TrimEnd())
            .Select(l => l.StartsWith('#') ? l.TrimStart('#', ' ') : l)
            .Select(l => l.StartsWith("* ") || l.StartsWith("- ") ? "• " + l[2..] : l)
            .Select(l => l.Replace("**", "").Replace("`", "")))
        .Trim();

    static HttpClient Http()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(Brand.AppName, UpdateChecker.CurrentVersion.ToString()));
        return http;
    }

    static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static string? Text(JsonElement e, string name, string language)
    {
        if (!e.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.String) return v.GetString();
        if (v.ValueKind != JsonValueKind.Object) return null;
        foreach (var code in new[] { language, language.Split('-')[0], "en" })
            if (v.TryGetProperty(code, out var t) && t.ValueKind == JsonValueKind.String) return t.GetString();
        return null;
    }

    static DateOnly? Date(string? text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
