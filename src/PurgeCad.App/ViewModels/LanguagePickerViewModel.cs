using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PurgeCad.App.Services;
using PurgeCad.Localization;

namespace PurgeCad.App.ViewModels;

/// <summary>One row: "System language" (Code = null) or one of the 62 languages.</summary>
public sealed record LanguageItem(string? Code, string Title, string Subtitle, string CodeLabel, Language? Language)
{
    public bool Matches(string query) =>
        Language is null
            ? string.IsNullOrWhiteSpace(query) || Title.Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase)
            : Language.Matches(query);
}

/// <summary>Searchable language list like on syntelab.com: "System language" first, then the 62 in fixed order.</summary>
public sealed partial class LanguagePickerViewModel : ObservableObject
{
    readonly List<LanguageItem> _all;

    public LanguagePickerViewModel()
    {
        _all = BuildItems();
        Filtered = new ObservableCollection<LanguageItem>(_all);
        var settings = AppHost.Current.Settings;
        _selected = settings.FollowSystemLanguage ? _all[0] : _all.FirstOrDefault(i => i.Code == settings.LanguageCode) ?? _all[0];
        Loc.Instance.LanguageChanged += (_, _) => RefreshSystemItem();
    }

    public ObservableCollection<LanguageItem> Filtered { get; }

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private LanguageItem _selected;
    [ObservableProperty] private bool _isOpen;

    partial void OnSearchTextChanged(string value)
    {
        Filtered.Clear();
        foreach (var item in _all.Where(i => i.Matches(value))) Filtered.Add(item);
    }

    /// <summary>Called by the list when the user picks a row.</summary>
    public void Choose(LanguageItem? item)
    {
        if (item is null) return;
        var settings = AppHost.Current.Settings;
        settings.FollowSystemLanguage = item.Code is null;
        if (item.Code is not null) settings.LanguageCode = item.Code;
        AppHost.Current.ApplyLanguage();
        AppHost.Current.SaveSettings();
        Selected = _all.First(i => i.Code == item.Code);
        IsOpen = false;
        SearchText = "";
    }

    static List<LanguageItem> BuildItems()
    {
        var items = new List<LanguageItem> { SystemItem() };
        items.AddRange(Languages.All.Select(l => new LanguageItem(l.Code, l.NativeName, l.EnglishName, l.Code, l)));
        return items;
    }

    static LanguageItem SystemItem()
    {
        var system = Languages.FromCulture(System.Globalization.CultureInfo.CurrentUICulture.Name);
        return new LanguageItem(null, Loc.Instance["language.system"], system.NativeName, "", null);
    }

    void RefreshSystemItem()
    {
        var wasSelected = Selected.Code is null;
        _all[0] = SystemItem();
        OnSearchTextChanged(SearchText);
        if (wasSelected) Selected = _all[0];
    }
}
