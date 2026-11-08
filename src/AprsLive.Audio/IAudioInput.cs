namespace AprsLive.Audio;

/// <summary>Mono audio capture, delivered as float samples in [-1, 1].</summary>
public interface IAudioInput : IDisposable
{
    IReadOnlyList<AudioDeviceInfo> Enumerate();

    /// <summary>Opens <paramref name="deviceId"/> (or the system default if null/empty) and starts delivering samples.</summary>
    void Start(string? deviceId, int sampleRate);

    void Stop();

    /// <summary>Raised on a background capture thread with newly captured samples.</summary>
    event Action<float[]>? SamplesAvailable;
}
