using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using AprsLive.App.ViewModels;
using AprsLive.App.Views;

namespace AprsLive.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // CommunityToolkit.Mvvm already validates; drop Avalonia's duplicate.
            BindingPlugins.DataValidators.RemoveAt(0);
            desktop.MainWindow = new MainWindow { DataContext = new MainViewModel() };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
