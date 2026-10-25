using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using PurgeCad.App.ViewModels;

namespace PurgeCad.App.Controls;

public partial class LanguagePicker : UserControl
{
    public LanguagePicker()
    {
        AvaloniaXamlLoader.Load(this);
        var list = this.FindControl<ListBox>("List")!;
        var search = this.FindControl<TextBox>("Search")!;

        // Pick on click or Enter; selection alone (arrow keys) only moves the highlight.
        list.Tapped += (_, _) => Choose(list.SelectedItem);
        list.KeyDown += (_, e) => { if (e.Key == Key.Enter) Choose(list.SelectedItem); };
        search.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Down && list.ItemCount > 0) { list.SelectedIndex = 0; list.Focus(); }
            if (e.Key == Key.Enter && list.ItemCount > 0) Choose(list.Items[0]);
        };
    }

    void Choose(object? item)
    {
        if (DataContext is LanguagePickerViewModel vm) vm.Choose(item as LanguageItem);
    }
}
