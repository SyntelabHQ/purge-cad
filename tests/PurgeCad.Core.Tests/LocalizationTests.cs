using PurgeCad.Localization;

namespace PurgeCad.Core.Tests;

public class LocalizationTests
{
    [Fact]
    public void Has62LanguagesInFixedOrder()
    {
        Assert.Equal(62, Languages.All.Count);
        Assert.Equal("en", Languages.All[0].Code);
        Assert.Equal("zh-CN", Languages.All[1].Code);
        Assert.Equal("yo", Languages.All[^1].Code);
        Assert.Equal(62, Languages.All.Select(l => l.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("fa")]
    [InlineData("ur")]
    [InlineData("he")]
    public void RightToLeftLanguagesAreFlagged(string code) => Assert.True(Languages.Find(code)!.RightToLeft);

    [Theory]
    [InlineData("pt-BR", "pt-BR")]
    [InlineData("pt-PT", "pt")]
    [InlineData("de-AT", "de")]
    [InlineData("zh-Hant-TW", "zh-TW")]
    [InlineData("zh-HK", "zh-TW")]
    [InlineData("zh-Hans", "zh-CN")]
    [InlineData("nb-NO", "no")]
    [InlineData("fil-PH", "tl")]
    [InlineData("xx-YY", "en")]
    public void MapsOsCultures(string culture, string expected) => Assert.Equal(expected, Languages.FromCulture(culture).Code);

    [Theory]
    [InlineData("rom")]
    [InlineData("Română")]
    [InlineData("ro")]
    public void SearchMatchesNativeEnglishAndCode(string query) => Assert.True(Languages.Find("ro")!.Matches(query));

    static readonly string[] ExtraPluralForms = [".zero", ".two", ".few", ".many"];

    [Fact]
    public void EveryLanguageHasAFileWithEveryKey()
    {
        var source = Localizer.SourceKeys().ToHashSet();
        Assert.NotEmpty(source);
        foreach (var language in Languages.All)
        {
            var keys = Localizer.KeysOf(language.Code);
            Assert.True(keys is not null, $"{language.Code}: no strings file");
            Assert.True(!source.Except(keys).Any(), $"{language.Code} misses: {string.Join(", ", source.Except(keys))}");
            // Extra keys are allowed only as plural forms of a pluralized key (e.g. "results.files.few")
            var extra = keys.Except(source).Where(k =>
                !ExtraPluralForms.Any(f => k.EndsWith(f) && source.Contains(k[..^f.Length] + ".other")));
            Assert.True(!extra.Any(), $"{language.Code} has unknown keys: {string.Join(", ", extra)}");
        }
    }

    [Fact]
    public void EveryTranslationKeepsThePlaceholders()
    {
        var english = new Localizer("en");
        foreach (var language in Languages.All)
        {
            var localizer = new Localizer(language.Code);
            foreach (var key in Localizer.KeysOf(language.Code)!)
            {
                var sourceKey = Localizer.SourceKeys().Contains(key) ? key : key[..key.LastIndexOf('.')] + ".other";
                var expected = Placeholders(english[sourceKey]);
                Assert.True(expected.SetEquals(Placeholders(localizer[key])), $"{language.Code} {key}: placeholders differ");
            }
        }
    }

    static HashSet<string> Placeholders(string text) =>
        System.Text.RegularExpressions.Regex.Matches(text, @"\{[a-z]+\}").Select(m => m.Value).ToHashSet();

    [Theory]
    [InlineData("en", 1, "one")]
    [InlineData("en", 0, "other")]
    [InlineData("fr", 0, "one")]
    [InlineData("ro", 1, "one")]
    [InlineData("ro", 5, "few")]
    [InlineData("ro", 20, "other")]
    [InlineData("ro", 101, "other")]
    [InlineData("ro", 119, "few")]
    [InlineData("ru", 21, "one")]
    [InlineData("ru", 3, "few")]
    [InlineData("ru", 12, "many")]
    [InlineData("pl", 22, "few")]
    [InlineData("pl", 25, "many")]
    [InlineData("cs", 3, "few")]
    [InlineData("ar", 0, "zero")]
    [InlineData("ar", 2, "two")]
    [InlineData("ar", 7, "few")]
    [InlineData("ar", 50, "many")]
    [InlineData("ar", 100, "other")]
    [InlineData("lv", 10, "zero")]
    [InlineData("zh-CN", 1, "other")]
    public void PluralCategories(string code, long count, string expected) =>
        Assert.Equal(expected, PluralRules.Category(code, count));

    [Theory]
    [InlineData("ro", 1, "1 fișier")]
    [InlineData("ro", 3, "3 fișiere")]
    [InlineData("ro", 20, "20 de fișiere")]
    [InlineData("en", 20, "20 files")]
    public void PluralPicksTheForm(string code, long count, string expected) =>
        Assert.Equal(expected, new Localizer(code).Plural("results.files", count));

    [Fact]
    public void UnknownLanguageFallsBackToEnglish()
    {
        var localizer = new Localizer("xx");
        Assert.Equal("Start", localizer["run.start"]);
    }

    [Fact]
    public void FormatsNamedPlaceholders()
    {
        var localizer = new Localizer("en");
        Assert.Equal("Found: 3 DWG · 1 DXF · 0 auxiliary files",
            localizer.Format("scan.found", ("dwg", 3), ("dxf", 1), ("aux", 0)));
    }
}
