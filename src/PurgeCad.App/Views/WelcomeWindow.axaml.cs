using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Markup.Xaml;
using PurgeCad.App.ViewModels;

namespace PurgeCad.App.Views;

public partial class WelcomeWindow : Window
{
    public WelcomeWindow()
    {
        AvaloniaXamlLoader.Load(this);
        Opened += async (_, _) =>
        {
            if (DataContext is not WelcomeViewModel vm) return;
            vm.UrlRequested += async (_, url) => await Launcher.LaunchUriAsync(new Uri(url));
            await vm.CheckForUpdatesAsync();
        };
    }
}
