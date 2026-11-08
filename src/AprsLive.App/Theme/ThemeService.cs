using Avalonia;
using Avalonia.Styling;
using AprsLive.Core.Settings;

namespace AprsLive.App.Theme;

/// <summary>
/// Applies a theme choice to the running application. Kept separate from
/// <c>App.axaml</c>'s own <c>RequestedThemeVariant="Default"</c> declaration:
/// that XAML attribute is only the initial value before settings load, this
/// service is what changes it afterwards from the settings tab.
/// </summary>
public sealed class ThemeService
{
    public void Apply(AppTheme theme)
    {
        if (Application.Current is not { } app)
            return;

        app.RequestedThemeVariant = theme switch
        {
            AppTheme.Light => ThemeVariant.Light,
            AppTheme.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }
}
