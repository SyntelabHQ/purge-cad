using Avalonia;
using Avalonia.Styling;
using PurgeCad.Core.Settings;

namespace PurgeCad.App.Services;

/// <summary>Switches between the Light and Dark token dictionaries.</summary>
public static class ThemeService
{
    public static void Apply(ThemeChoice theme)
    {
        if (Application.Current is { } app)
            app.RequestedThemeVariant = theme == ThemeChoice.Light ? ThemeVariant.Light : ThemeVariant.Dark;
    }

    /// <summary>The user picked a theme: remember it and apply it now. Returns false when it was already active.</summary>
    public static bool Choose(ThemeChoice theme)
    {
        var settings = AppHost.Current.Settings;
        if (settings.Theme == theme) return false;
        settings.Theme = theme;
        Apply(theme);
        return true;
    }
}
