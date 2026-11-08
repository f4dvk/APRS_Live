namespace AprsLive.Core.Messaging;

/// <summary>The exchange of APRS messages with a single peer callsign.</summary>
public sealed class Conversation
{
    public required string Peer { get; init; }

    public List<ConversationMessage> Messages { get; } = new();

    public DateTimeOffset LastActivity { get; set; }
}
