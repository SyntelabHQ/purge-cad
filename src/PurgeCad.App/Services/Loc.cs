using System.ComponentModel;
using System.Globalization;
using PurgeCad.Localization;

namespace PurgeCad.App.Services;

/// <summary>
/// Localized strings for XAML: Text="{Binding [main.start], Source={x:Static s:Loc.Instance}}".
/// Changing the language refreshes every binding at once.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    Localizer _localizer = new(Languages.DefaultCode);

    public event PropertyChangedEventHandler? PropertyChanged;

    public Localizer Localizer => _localizer;

    public Language Language => _localizer.Language;

    public string this[string key] => _localizer[key];

    public string Format(string key, params (string Name, object Value)[] args) => _localizer.Format(key, args);

    public string Plural(string key, long count, params (string Name, object Value)[] args) => _localizer.Plural(key, count, args);

    public CultureInfo Culture => _localizer.Culture;

    public void SetLanguage(string code)
    {
        _localizer.SetLanguage(code);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised after the language changes (view models refresh composed texts).</summary>
    public event EventHandler? LanguageChanged;

    /// <summary>"1.2 MB" style sizes in the current culture.</summary>
    public string Size(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (Math.Abs(value) >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return value.ToString(unit == 0 ? "0" : "0.#", Culture) + " " + units[unit];
    }
}
