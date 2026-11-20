using AprsLive.Core.Messaging;

namespace AprsLive.App.ViewModels;

/// <summary>One displayed line in a conversation thread — a read-only snapshot of a <see cref="ConversationMessage"/>.</summary>
public sealed class MessageRowViewModel
{
    public MessageRowViewModel(ConversationMessage message)
    {
        Message = message;
        IsOutgoing = message.Direction == MessageDirection.Outgoing;
        Text = message.Text;
        TimeText = message.Timestamp.LocalDateTime.ToString("HH:mm:ss");
        DirectionLabel = IsOutgoing ? "Vous" : "Station";
        StatusText = IsOutgoing
            ? message.AckState switch
            {
                MessageAckState.Pending => "Envoyé…",
                MessageAckState.Acked => "Confirmé ✓",
                MessageAckState.Failed => "Échec",
                _ => "",
            }
            : "";
    }

    /// <summary>The conversation entry this row displays — the handle for deleting it.</summary>
    public ConversationMessage Message { get; }

    public bool IsOutgoing { get; }
    public string Text { get; }
    public string TimeText { get; }
    public string DirectionLabel { get; }
    public string StatusText { get; }
}
