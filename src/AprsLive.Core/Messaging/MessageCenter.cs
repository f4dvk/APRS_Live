using AprsLive.Core.Aprs;

namespace AprsLive.Core.Messaging;

/// <summary>
/// Tracks every APRS message conversation, keyed by the peer callsign. Feeds
/// outgoing messages in (as the app sends them) and incoming message packets
/// in (as they're received on any channel) — matching acks/rejects to the
/// outgoing message they settle, and raising <see cref="AckRequested"/> for
/// an incoming message that itself asks for one back.
/// </summary>
public sealed class MessageCenter
{
    private readonly Dictionary<string, Conversation> _conversations = new(StringComparer.OrdinalIgnoreCase);

    public event Action<Conversation>? ConversationUpdated;

    /// <summary>Raised for a genuine incoming message (not an ack/reject).</summary>
    public event Action<Conversation, ConversationMessage>? IncomingMessage;

    /// <summary>Raised when an incoming message asks for an ack back: (peer, message number).</summary>
    public event Action<string, string>? AckRequested;

    public IReadOnlyCollection<Conversation> Conversations => _conversations.Values;

    public Conversation GetOrCreate(string peer)
    {
        peer = peer.Trim().ToUpperInvariant();
        if (!_conversations.TryGetValue(peer, out var conv))
        {
            conv = new Conversation { Peer = peer };
            _conversations[peer] = conv;
        }
        return conv;
    }

    public ConversationMessage RecordOutgoing(string peer, string text, string number)
    {
        var conv = GetOrCreate(peer);
        var msg = new ConversationMessage
        {
            Direction = MessageDirection.Outgoing,
            Text = text,
            Number = number,
        };
        conv.Messages.Add(msg);
        conv.LastActivity = msg.Timestamp;
        ConversationUpdated?.Invoke(conv);
        return msg;
    }

    public void MarkAcked(string peer, string number) => UpdateOutgoingState(peer, number, MessageAckState.Acked);

    public void MarkFailed(string peer, string number) => UpdateOutgoingState(peer, number, MessageAckState.Failed);

    public MessageAckState? GetAckState(string peer, string number)
    {
        var conv = GetOrCreate(peer);
        var msg = conv.Messages.LastOrDefault(m => m.Direction == MessageDirection.Outgoing && m.Number == number);
        return msg?.AckState;
    }

    private void UpdateOutgoingState(string peer, string number, MessageAckState state)
    {
        var conv = GetOrCreate(peer);
        var msg = conv.Messages.LastOrDefault(m => m.Direction == MessageDirection.Outgoing && m.Number == number);
        if (msg is null || msg.AckState == MessageAckState.Acked)
            return;

        msg.AckState = state;
        ConversationUpdated?.Invoke(conv);
    }

    /// <summary>
    /// Feeds a received packet in. Only packets addressed to <paramref name="ownCallsign"/>
    /// are handled — anything else (overheard between other stations, e.g. on
    /// the APRS-IS firehose) is ignored.
    /// </summary>
    public void ApplyIncoming(AprsPacket packet, string ownCallsign)
    {
        if (packet.Kind != AprsPacketKind.Message)
            return;
        if (string.IsNullOrWhiteSpace(ownCallsign))
            return;
        if (!string.Equals(packet.MessageAddressee?.Trim(), ownCallsign.Trim(), StringComparison.OrdinalIgnoreCase))
            return;

        string peer = packet.Source;

        if (packet.IsAck)
        {
            if (packet.MessageNumber is { Length: > 0 } n) MarkAcked(peer, n);
            return;
        }
        if (packet.IsRej)
        {
            if (packet.MessageNumber is { Length: > 0 } n) MarkFailed(peer, n);
            return;
        }

        var conv = GetOrCreate(peer);
        var msg = new ConversationMessage
        {
            Direction = MessageDirection.Incoming,
            Text = packet.MessageText ?? "",
            Number = packet.MessageNumber,
        };
        conv.Messages.Add(msg);
        conv.LastActivity = msg.Timestamp;
        ConversationUpdated?.Invoke(conv);
        IncomingMessage?.Invoke(conv, msg);

        if (!string.IsNullOrEmpty(packet.MessageNumber))
            AckRequested?.Invoke(peer, packet.MessageNumber);
    }
}
