using Avalonia.Controls;
using Avalonia.Input;
using AprsLive.App.ViewModels;

namespace AprsLive.App.Views;

public partial class CoverageView : UserControl
{
    public CoverageView()
    {
        InitializeComponent();
    }

    // The "Ne plus suivre" button handles its own press, so it never gets here.
    private void OnRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: CoverageRowViewModel row } control
            && e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            row.ToggleCommand.Execute(null);
            e.Handled = true;
        }
    }
}
