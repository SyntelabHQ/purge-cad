using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PurgeCad.Core.Scanning;

/// <summary>
/// Filters drawings by file name. Terms are separated by commas or semicolons; a drawing matches when its name
/// contains any of them. A term may use * and ? wildcards ("PAD*propunere"), and a term starting with "-" leaves
/// matching names out ("-vechi"). Case and diacritics are ignored ("propunere" matches "Propunere_ȘCOALĂ.dwg").
/// The extension is not part of the name. An empty filter matches everything.
/// </summary>
public sealed class NameFilter
{
    readonly List<Regex> _include = [];
    readonly List<Regex> _exclude = [];

    public static readonly NameFilter All = new("");

    public NameFilter(string? text)
    {
        foreach (var raw in (text ?? "").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var exclude = raw.StartsWith('-');
            var term = Normalize(exclude ? raw[1..].Trim() : raw);
            if (term.Length == 0) continue;
            var pattern = Regex.Escape(term).Replace(@"\*", ".*").Replace(@"\?", ".");
            (exclude ? _exclude : _include).Add(new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase));
        }
    }

    public bool IsEmpty => _include.Count == 0 && _exclude.Count == 0;

    /// <summary>True when a file (path or name) passes the filter.</summary>
    public bool Matches(string path)
    {
        if (IsEmpty) return true;
        // both separators, whatever the OS: the name is what follows the last one
        var name = Normalize(Path.GetFileNameWithoutExtension(path[(path.LastIndexOfAny(['\\', '/']) + 1)..]));
        if (_exclude.Any(r => r.IsMatch(name))) return false;
        return _include.Count == 0 || _include.Any(r => r.IsMatch(name));
    }

    /// <summary>The drawings that pass, with their auxiliary files (see <see cref="ScanFilter"/>).</summary>
    public ScanResult Apply(ScanResult scan) => ScanFilter.Apply(scan, this, SizeRange.Any);

    /// <summary>Lower case without diacritics: "Școală" → "scoala".</summary>
    static string Normalize(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
