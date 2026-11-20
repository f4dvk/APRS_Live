using AprsLive.Core.Aprs;
using AprsLive.Core.Ax25;
using AprsLive.Core.Messaging;

namespace AprsLive.Core.Tests;

public class MessageCenterTests
{
    private static AprsPacket Parse(byte[] frame)
    {
        Assert.True(Ax25Frame.TryParse(frame, out var f));
        return AprsParser.Parse(f!);
    }

    [Fact]
    public void Incoming_message_addressed_to_us_is_recorded_and_requests_an_ack()
    {
        var center = new MessageCenter();
        Conversation? updated = null;
        center.ConversationUpdated += c => updated = c;
        (string Peer, string Number)? ackRequest = null;
        center.AckRequested += (peer, number) => ackRequest = (peer, number);

        var frame = Ax25Builder.BuildMessage("N0CALL-9", "F4DVK-9", "Salut", "APLIVE", new[] { "WIDE1-1" }, "01");
        center.ApplyIncoming(Parse(frame), ownCallsign: "F4DVK-9");

        Assert.NotNull(updated);
        var msg = Assert.Single(updated!.Messages);
        Assert.Equal(MessageDirection.Incoming, msg.Direction);
        Assert.Equal("Salut", msg.Text);
        Assert.Equal(("N0CALL-9", "01"), ackRequest);
    }

    [Fact]
    public void Message_not_addressed_to_us_is_ignored()
    {
        var center = new MessageCenter();
        var frame = Ax25Builder.BuildMessage("N0CALL-9", "SOMEONE-1", "Pas pour nous", "APLIVE", Array.Empty<string>(), "01");

        center.ApplyIncoming(Parse(frame), ownCallsign: "F4DVK-9");

        Assert.Empty(center.Conversations);
    }

    [Fact]
    public void Ack_settles_the_matching_outgoing_message()
    {
        var center = new MessageCenter();
        center.RecordOutgoing("N0CALL-9", "Bonjour", "05");
        Assert.Equal(MessageAckState.Pending, center.GetAckState("N0CALL-9", "05"));

        var ackFrame = Ax25Builder.BuildAck("N0CALL-9", "F4DVK-9", "05", "APLIVE", Array.Empty<string>());
        center.ApplyIncoming(Parse(ackFrame), ownCallsign: "F4DVK-9");

        Assert.Equal(MessageAckState.Acked, center.GetAckState("N0CALL-9", "05"));
    }

    [Fact]
    public void Digipeated_copy_of_an_incoming_message_is_acked_again_but_not_shown_twice()
    {
        var center = new MessageCenter();
        int ackRequests = 0;
        center.AckRequested += (_, _) => ackRequests++;
        int incomingEvents = 0;
        center.IncomingMessage += (_, _) => incomingEvents++;

        var frame = Ax25Builder.BuildMessage("N0CALL-9", "F4DVK-9", "Salut", "APLIVE", new[] { "WIDE1-1" }, "01");
        center.ApplyIncoming(Parse(frame), ownCallsign: "F4DVK-9");
        center.ApplyIncoming(Parse(frame), ownCallsign: "F4DVK-9");

        var conv = Assert.Single(center.Conversations);
        Assert.Single(conv.Messages);
        Assert.Equal(1, incomingEvents);
        Assert.Equal(2, ackRequests);
    }

    [Fact]
    public void Same_number_with_different_text_is_a_new_message()
    {
        var center = new MessageCenter();

        center.ApplyIncoming(Parse(Ax25Builder.BuildMessage("N0CALL-9", "F4DVK-9", "Premier", "APLIVE", Array.Empty<string>(), "01")), "F4DVK-9");
        center.ApplyIncoming(Parse(Ax25Builder.BuildMessage("N0CALL-9", "F4DVK-9", "Second", "APLIVE", Array.Empty<string>(), "01")), "F4DVK-9");

        var conv = Assert.Single(center.Conversations);
        Assert.Equal(2, conv.Messages.Count);
    }

    [Fact]
    public void RemoveMessage_deletes_only_that_entry()
    {
        var center = new MessageCenter();
        var first = center.RecordOutgoing("N0CALL-9", "Un", "01");
        center.RecordOutgoing("N0CALL-9", "Deux", "02");

        center.RemoveMessage("N0CALL-9", first);

        var conv = Assert.Single(center.Conversations);
        var left = Assert.Single(conv.Messages);
        Assert.Equal("Deux", left.Text);
    }

    [Fact]
    public void RemoveConversation_forgets_the_whole_history()
    {
        var center = new MessageCenter();
        center.RecordOutgoing("N0CALL-9", "Un", "01");
        Conversation? removed = null;
        center.ConversationRemoved += c => removed = c;

        center.RemoveConversation("n0call-9");

        Assert.Empty(center.Conversations);
        Assert.Equal("N0CALL-9", removed?.Peer);
    }

    [Fact]
    public void MarkFailed_does_not_override_an_already_acked_message()
    {
        var center = new MessageCenter();
        center.RecordOutgoing("N0CALL-9", "Bonjour", "05");
        center.MarkAcked("N0CALL-9", "05");

        center.MarkFailed("N0CALL-9", "05");

        Assert.Equal(MessageAckState.Acked, center.GetAckState("N0CALL-9", "05"));
    }
}
