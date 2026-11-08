namespace AprsLive.Audio.Ptt;

/// <summary>Keys/unkeys the transmitter for the duration of an audio burst.</summary>
public interface IPttKeyer : IDisposable
{
    /// <summary>Keys the transmitter and returns a handle that unkeys it on <see cref="IDisposable.Dispose"/> — a stuck transmitter should be structurally impossible: always use <c>using</c>.</summary>
    IDisposable Key();
}
