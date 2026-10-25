namespace PurgeCad.Localization;

/// <summary>One interface language.</summary>
/// <param name="Code">BCP 47 code, also the strings file name and the URL segment (e.g. "pt-BR").</param>
/// <param name="NativeName">Name in the language itself, shown first in the picker.</param>
/// <param name="EnglishName">English name, shown under the native name.</param>
/// <param name="RightToLeft">True for scripts written right to left.</param>
public sealed record Language(string Code, string NativeName, string EnglishName, bool RightToLeft = false)
{
    /// <summary>True when <paramref name="query"/> matches the native name, English name or code.</summary>
    public bool Matches(string query) =>
        string.IsNullOrWhiteSpace(query)
        || NativeName.Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase)
        || EnglishName.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase)
        || Code.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase);
}

/// <summary>The 62 supported languages, in the fixed order used on syntelab.com.</summary>
public static class Languages
{
    public const string DefaultCode = "en";

    public static IReadOnlyList<Language> All { get; } =
    [
        new("en", "English", "English"),
        new("zh-CN", "中文（简体）", "Chinese (Simplified)"),
        new("es", "Español", "Spanish"),
        new("ar", "العربية", "Arabic", RightToLeft: true),
        new("pt", "Português", "Portuguese"),
        new("id", "Bahasa Indonesia", "Indonesian"),
        new("fr", "Français", "French"),
        new("ja", "日本語", "Japanese"),
        new("ru", "Русский", "Russian"),
        new("de", "Deutsch", "German"),
        new("hi", "हिन्दी", "Hindi"),
        new("ko", "한국어", "Korean"),
        new("tr", "Türkçe", "Turkish"),
        new("it", "Italiano", "Italian"),
        new("vi", "Tiếng Việt", "Vietnamese"),
        new("pl", "Polski", "Polish"),
        new("nl", "Nederlands", "Dutch"),
        new("th", "ไทย", "Thai"),
        new("uk", "Українська", "Ukrainian"),
        new("ro", "Română", "Romanian"),
        new("el", "Ελληνικά", "Greek"),
        new("cs", "Čeština", "Czech"),
        new("sv", "Svenska", "Swedish"),
        new("hu", "Magyar", "Hungarian"),
        new("fa", "فارسی", "Persian", RightToLeft: true),
        new("pt-BR", "Português (Brasil)", "Brazilian Portuguese"),
        new("zh-TW", "中文（繁體）", "Chinese (Traditional)"),
        new("sq", "Shqip", "Albanian"),
        new("am", "አማርኛ", "Amharic"),
        new("be", "Беларуская", "Belarusian"),
        new("bn", "বাংলা", "Bengali"),
        new("bs", "Bosanski", "Bosnian"),
        new("bg", "Български", "Bulgarian"),
        new("ca", "Català", "Catalan"),
        new("hr", "Hrvatski", "Croatian"),
        new("da", "Dansk", "Danish"),
        new("et", "Eesti", "Estonian"),
        new("tl", "Filipino", "Filipino"),
        new("fi", "Suomi", "Finnish"),
        new("gu", "ગુજરાતી", "Gujarati"),
        new("ha", "Hausa", "Hausa"),
        new("he", "עברית", "Hebrew", RightToLeft: true),
        new("ga", "Gaeilge", "Irish"),
        new("jv", "ꦧꦱꦗꦮ", "Javanese"),
        new("kn", "ಕನ್ನಡ", "Kannada"),
        new("lv", "Latviešu", "Latvian"),
        new("lt", "Lietuvių", "Lithuanian"),
        new("mk", "Македонски", "Macedonian"),
        new("ms", "Bahasa Melayu", "Malay"),
        new("ml", "മലയാളം", "Malayalam"),
        new("mt", "Malti", "Maltese"),
        new("my", "မြန်မာ", "Myanmar (Burmese)"),
        new("no", "Norsk", "Norwegian"),
        new("pa", "ਪੰਜਾਬੀ", "Punjabi"),
        new("sr", "Српски", "Serbian"),
        new("sk", "Slovenčina", "Slovak"),
        new("sl", "Slovenščina", "Slovenian"),
        new("sw", "Kiswahili", "Swahili"),
        new("ta", "தமிழ்", "Tamil"),
        new("te", "తెలుగు", "Telugu"),
        new("ur", "اردو", "Urdu", RightToLeft: true),
        new("yo", "Yorùbá", "Yoruba"),
    ];

    public static Language? Find(string? code) =>
        code is null ? null : All.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Best match for an OS culture name: exact ("pt-BR"), then special cases, then the neutral language ("de-AT" → "de").
    /// Falls back to English.
    /// </summary>
    public static Language FromCulture(string cultureName)
    {
        if (Find(cultureName) is { } exact) return exact;

        var lower = cultureName.ToLowerInvariant();
        if (lower.StartsWith("zh"))
            return Find(lower.Contains("hant") || lower.EndsWith("-tw") || lower.EndsWith("-hk") || lower.EndsWith("-mo") ? "zh-TW" : "zh-CN")!;
        if (lower is "fil" || lower.StartsWith("fil-")) return Find("tl")!;
        if (lower is "nb" or "nn" || lower.StartsWith("nb-") || lower.StartsWith("nn-")) return Find("no")!;
        if (lower.StartsWith("iw")) return Find("he")!;

        var neutral = lower.Split('-')[0];
        return Find(neutral) ?? Find(DefaultCode)!;
    }
}
