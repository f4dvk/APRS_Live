namespace AprsLive.Core.Messaging;

/// <summary>One line of an APRS message conversation, in either direction.</summary>
public sealed class ConversationMessage
{
    public required MessageDirection Direction { get; init; }

    public required string Text { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    /// <summary>The APRS message number ({nn style), when the frame carried one.</summary>
    public string? Number { get; init; }

    /// <summary>Delivery state — only tracked for an <see cref="MessageDirection.Outgoing"/> message.</summary>
    public MessageAckState AckState { get; set; } = MessageAckState.Pending;
}
