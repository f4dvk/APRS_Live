namespace AprsLive.Audio;

/// <summary>Mono audio playback of float samples in [-1, 1].</summary>
public interface IAudioOutput : IDisposable
{
    IReadOnlyList<AudioDeviceInfo> Enumerate();

    void Start(string? deviceId, int sampleRate);

    /// <summary>Plays the samples and completes only once they have actually finished draining out the device — callers time PTT release off this.</summary>
    Task PlayAsync(float[] samples);

    void Stop();
}
