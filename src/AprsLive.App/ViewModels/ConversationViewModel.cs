using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using AprsLive.Core.Messaging;

namespace AprsLive.App.ViewModels;

/// <summary>One row of the conversation list, and — when selected — the full message thread shown below it.</summary>
public partial class ConversationViewModel : ViewModelBase
{
    public string Peer { get; }

    public ObservableCollection<MessageRowViewModel> Messages { get; } = new();

    [ObservableProperty]
    private string _preview = "";

    [ObservableProperty]
    private DateTimeOffset _lastActivity;

    public ConversationViewModel(string peer)
    {
        Peer = peer;
    }

    public void SyncFrom(Conversation conversation)
    {
        Messages.Clear();
        foreach (var m in conversation.Messages)
            Messages.Add(new MessageRowViewModel(m));

        LastActivity = conversation.LastActivity;
        Preview = conversation.Messages.Count > 0 ? conversation.Messages[^1].Text : "";
    }
}
