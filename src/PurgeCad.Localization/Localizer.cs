using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace PurgeCad.Localization;

/// <summary>
/// Loads interface strings from the embedded JSON files (Strings/&lt;code&gt;.json).
/// English is the source and the fallback for every missing key.
/// </summary>
public sealed class Localizer
{
    readonly Dictionary<string, string> _english;
    Dictionary<string, string> _current;

    public Localizer(string languageCode)
    {
        _english = Load(Languages.DefaultCode) ?? new();
        _current = _english;
        Language = Languages.Find(Languages.DefaultCode)!;
        SetLanguage(languageCode);
    }

    public Language Language { get; private set; }

    /// <summary>Culture used for numbers and dates; invariant if the OS does not know the language.</summary>
    public CultureInfo Culture
    {
        get
        {
            try { return CultureInfo.GetCultureInfo(Language.Code); }
            catch (CultureNotFoundException) { return CultureInfo.InvariantCulture; }
        }
    }

    /// <summary>Raised after the language changes, so bound views can refresh.</summary>
    public event EventHandler? LanguageChanged;

    public void SetLanguage(string code)
    {
        var language = Languages.Find(code) ?? Languages.Find(Languages.DefaultCode)!;
        _current = Load(language.Code) ?? _english;
        Language = language;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Text for <paramref name="key"/>; the key itself if it exists in no language (makes gaps visible).</summary>
    public string this[string key] =>
        _current.TryGetValue(key, out var text) || _english.TryGetValue(key, out text) ? text : key;

    /// <summary>Text with named placeholders: Format("scan.found", ("dwg", 3)) → "Found: 3 DWG".</summary>
    public string Format(string key, params (string Name, object Value)[] args)
    {
        var text = this[key];
        var culture = Culture;
        foreach (var (name, value) in args)
        {
            var shown = value is IFormattable f ? f.ToString(value is int or long ? "N0" : null, culture) : value?.ToString();
            text = text.Replace("{" + name + "}", shown);
        }
        return text;
    }

    /// <summary>
    /// Plural form: looks for "key.&lt;category&gt;" with the language's CLDR category (one, few, many…),
    /// then "key.other" in the same language, then the English forms.
    /// </summary>
    public string Plural(string key, long count, params (string Name, object Value)[] args)
    {
        var category = PluralRules.Category(Language.Code, count);
        var formKey = key + "." + category;
        if (!_current.ContainsKey(formKey))
            formKey = _current.ContainsKey(key + ".other") ? key + ".other" : key + (count == 1 ? ".one" : ".other");
        return Format(formKey, [("count", count), .. args]);
    }

    /// <summary>All keys in the English source, used by tests to check that every language has every key.</summary>
    public static IReadOnlyCollection<string> SourceKeys() => (Load(Languages.DefaultCode) ?? new()).Keys;

    /// <summary>Keys of one language file, or null when the file does not exist yet.</summary>
    public static IReadOnlyCollection<string>? KeysOf(string code) => Load(code)?.Keys;

    static Dictionary<string, string>? Load(string code)
    {
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"PurgeCad.Strings.{code}.json");
        if (stream is null) return null;
        using (stream)
            return JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
    }
}
