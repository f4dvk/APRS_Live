using Avalonia.Controls;
using Avalonia.Input;
using AprsLive.App.ViewModels;

namespace AprsLive.App.Views;

public partial class MessagesPanelView : UserControl
{
    public MessagesPanelView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Keeps the thread scrolled to its newest message: the view model rebuilds
    /// the Messages collection on every update (which resets the scroll offset
    /// anyway), so any extent change jumps to the end.
    /// </summary>
    private void OnThreadScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentDelta.Y != 0 && sender is ScrollViewer sv)
            sv.ScrollToEnd();
    }

    private void OnDraftKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not MessagesViewModel vm)
            return;

        vm.SendCommand.Execute(null);
        e.Handled = true;
    }
}
