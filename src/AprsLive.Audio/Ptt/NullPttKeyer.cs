namespace AprsLive.Audio.Ptt;

/// <summary>No PTT line at all — VOX on the radio keys from the audio itself.</summary>
public sealed class NullPttKeyer : IPttKeyer
{
    private sealed class NoopHandle : IDisposable { public void Dispose() { } }

    public IDisposable Key() => new NoopHandle();

    public void Dispose() { }
}
