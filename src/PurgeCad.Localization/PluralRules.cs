namespace PurgeCad.Localization;

/// <summary>
/// CLDR plural categories for whole numbers ("zero", "one", "two", "few", "many", "other").
/// Only the integer rules are needed: PurgeCAD pluralizes counts of files and folders.
/// </summary>
public static class PluralRules
{
    public static string Category(string languageCode, long count)
    {
        var n = Math.Abs(count);
        var n10 = n % 10;
        var n100 = n % 100;
        var language = languageCode.Split('-')[0].ToLowerInvariant();

        switch (language)
        {
            // No plural forms
            case "zh" or "ja" or "ko" or "th" or "vi" or "id" or "ms" or "jv" or "my" or "yo":
                return "other";

            // 0 and 1 are "one"
            case "fr" or "pt" or "hi" or "bn" or "fa" or "am" or "pa" or "gu" or "kn":
                return n <= 1 ? "one" : "other";

            case "ro":
                return n == 1 ? "one" : n == 0 || n100 is >= 2 and <= 19 ? "few" : "other";

            case "ru" or "uk" or "be":
                return n10 == 1 && n100 != 11 ? "one"
                    : n10 is >= 2 and <= 4 && n100 is < 12 or > 14 ? "few"
                    : "many";

            case "pl":
                return n == 1 ? "one"
                    : n10 is >= 2 and <= 4 && n100 is < 12 or > 14 ? "few"
                    : "many";

            case "cs" or "sk":
                return n == 1 ? "one" : n is >= 2 and <= 4 ? "few" : "other";

            case "hr" or "sr" or "bs":
                return n10 == 1 && n100 != 11 ? "one"
                    : n10 is >= 2 and <= 4 && n100 is < 12 or > 14 ? "few"
                    : "other";

            case "sl":
                return n100 == 1 ? "one" : n100 == 2 ? "two" : n100 is 3 or 4 ? "few" : "other";

            case "lt":
                return n10 == 1 && n100 is < 11 or > 19 ? "one"
                    : n10 >= 2 && n100 is < 11 or > 19 ? "few"
                    : "other";

            case "lv":
                return n10 == 0 || n100 is >= 11 and <= 19 ? "zero"
                    : n10 == 1 && n100 != 11 ? "one"
                    : "other";

            case "mk":
                return n10 == 1 && n100 != 11 ? "one" : "other";

            case "tl":
                return n is 1 or 2 or 3 || n10 is not (4 or 6 or 9) ? "one" : "other";

            case "he":
                return n == 1 ? "one" : n == 2 ? "two" : "other";

            case "ar":
                return n == 0 ? "zero" : n == 1 ? "one" : n == 2 ? "two"
                    : n100 is >= 3 and <= 10 ? "few"
                    : n100 is >= 11 and <= 99 ? "many"
                    : "other";

            case "ga":
                return n == 1 ? "one" : n == 2 ? "two" : n is >= 3 and <= 6 ? "few" : n is >= 7 and <= 10 ? "many" : "other";

            case "mt":
                return n == 1 ? "one" : n == 2 ? "two"
                    : n == 0 || n100 is >= 3 and <= 10 ? "few"
                    : n100 is >= 11 and <= 19 ? "many"
                    : "other";

            default:
                return n == 1 ? "one" : "other";
        }
    }
}
