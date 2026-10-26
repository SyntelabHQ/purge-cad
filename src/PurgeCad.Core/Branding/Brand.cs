namespace PurgeCad.Core.Branding;

/// <summary>
/// Names, links and the attribution notice. Single source for the app, the About window and reports.
/// The attribution notice is required by ADDITIONAL-TERMS.md (GPLv3 section 7(b)) and must not be removed.
/// </summary>
public static class Brand
{
    public const string AppName = "PurgeCAD";

    public const string WebsiteUrl = "https://syntelab.com";
    public const string AppsUrl = "https://syntelab.com/apps";
    public const string SourceCodeUrl = "https://github.com/SyntelabHQ/purge-cad";

    /// <summary>
    /// GitHub repository ("owner/name") whose releases the app checks for updates. A fork that publishes its own
    /// releases changes this to its repository.
    /// </summary>
    public const string UpdateRepository = "SyntelabHQ/purge-cad";

    const string AppSlug = "purge-cad";

    /// <summary>Official page: English has no language segment, other languages use /apps/&lt;code&gt;/purge-cad.</summary>
    public static string AppPageUrl(string languageCode) =>
        string.IsNullOrEmpty(languageCode) || languageCode.Equals("en", StringComparison.OrdinalIgnoreCase)
            ? $"{AppsUrl}/{AppSlug}"
            : $"{AppsUrl}/{languageCode}/{AppSlug}";

    /// <summary>Support page: English has no language segment, other languages use /&lt;code&gt;/support.</summary>
    public static string SupportUrl(string languageCode) =>
        string.IsNullOrEmpty(languageCode) || languageCode.Equals("en", StringComparison.OrdinalIgnoreCase)
            ? $"{WebsiteUrl}/support"
            : $"{WebsiteUrl}/{languageCode}/support";

    public const string AttributionNotice =
        "Based on PurgeCAD by Syntelab — https://syntelab.com/apps/purge-cad\n" +
        "Part of Syntelab Apps — https://syntelab.com/apps";
}
