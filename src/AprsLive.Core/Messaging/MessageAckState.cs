namespace AprsLive.Core.Messaging;

/// <summary>Only meaningful for an outgoing message — an incoming one has nothing to acknowledge from our side.</summary>
public enum MessageAckState
{
    Pending,
    Acked,
    Failed,
}
