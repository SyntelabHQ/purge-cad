using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace PurgeCad.App.Views;

public partial class ConfirmDialog : Window
{
    /// <summary>Needed by the XAML loader / designer.</summary>
    public ConfirmDialog() => AvaloniaXamlLoader.Load(this);

    public ConfirmDialog(string title, string body, IReadOnlyList<string> items, string ok, string cancel) : this()
    {
        Title = title;
        this.FindControl<TextBlock>("Heading")!.Text = title;
        this.FindControl<TextBlock>("Body")!.Text = body;
        this.FindControl<ListBox>("Items")!.ItemsSource = items;
        this.FindControl<Border>("ItemsCard")!.IsVisible = items.Count > 0;
        var okButton = this.FindControl<Button>("Ok")!;
        var cancelButton = this.FindControl<Button>("Cancel")!;
        okButton.Content = ok;
        cancelButton.Content = cancel;
        okButton.Click += (_, _) => Close(true);
        cancelButton.Click += (_, _) => Close(false);
    }
}
