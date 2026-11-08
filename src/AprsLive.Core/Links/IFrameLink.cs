namespace AprsLive.Core.Links;

/// <summary>
/// A transport carrying AX.25 frames to/from a TNC — a hardware KISS TNC
/// (serial or TCP) today, an audio AFSK1200 software modem in a later phase.
/// The rest of the app only ever talks to this interface, so a new transport
/// kind is purely additive.
/// </summary>
public interface IFrameLink : IAsyncDisposable
{
    FrameLinkState State { get; }

    /// <summary>Raised on every complete received frame (AX.25 bytes only).</summary>
    event Action<byte[]>? FrameReceived;

    event Action<FrameLinkState>? StateChanged;

    /// <summary>Raised with a human-readable line for the connection log.</summary>
    event Action<string>? Log;

    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>Send a raw AX.25 frame (wrapped/encoded by the implementation).</summary>
    Task SendFrameAsync(ReadOnlyMemory<byte> ax25, CancellationToken ct = default);
}
