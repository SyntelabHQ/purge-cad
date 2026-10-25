using Avalonia.Data.Converters;

namespace PurgeCad.App.Services;

public static class Converters
{
    /// <summary>true (right-to-left language) → RightToLeft layout.</summary>
    public static readonly IValueConverter FlowDirection =
        new FuncValueConverter<bool, Avalonia.Media.FlowDirection>(rtl =>
            rtl ? Avalonia.Media.FlowDirection.RightToLeft : Avalonia.Media.FlowDirection.LeftToRight);
}
