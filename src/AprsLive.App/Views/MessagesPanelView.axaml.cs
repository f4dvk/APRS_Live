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

    private void OnDraftKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not MessagesViewModel vm)
            return;

        vm.SendCommand.Execute(null);
        e.Handled = true;
    }
}
