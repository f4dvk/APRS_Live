using System.Runtime.InteropServices;

namespace AprsLive.Audio.Alsa;

public sealed class AlsaAudioInput : IAudioInput
{
    private IntPtr _pcm;
    private Thread? _thread;
    private volatile bool _running;

    public event Action<float[]>? SamplesAvailable;

    public IReadOnlyList<AudioDeviceInfo> Enumerate() => AlsaDeviceEnumerator.Enumerate();

    public void Start(string? deviceId, int sampleRate)
    {
        string name = string.IsNullOrWhiteSpace(deviceId) ? "default" : deviceId;
        int rc = AlsaNative.snd_pcm_open(out _pcm, name, AlsaNative.StreamCapture, 0);
        if (rc < 0)
            throw new InvalidOperationException($"Ouverture ALSA (capture, {name}) impossible : {AlsaNative.StrError(rc)}");

        rc = AlsaNative.snd_pcm_set_params(_pcm, AlsaNative.FormatS16Le, AlsaNative.AccessRwInterleaved,
            channels: 1, rate: (uint)sampleRate, softResample: 1, latencyUs: 100_000);
        if (rc < 0)
        {
            AlsaNative.snd_pcm_close(_pcm);
            _pcm = IntPtr.Zero;
            throw new InvalidOperationException($"Configuration ALSA (capture, {name}) impossible : {AlsaNative.StrError(rc)}");
        }

        AlsaNative.snd_pcm_prepare(_pcm);

        _running = true;
        _thread = new Thread(ReadLoop) { IsBackground = true, Name = "AlsaCapture" };
        _thread.Start();
    }

    private void ReadLoop()
    {
        const int framesPerRead = 512;
        var buffer = new short[framesPerRead];
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            while (_running)
            {
                long n = AlsaNative.snd_pcm_readi(_pcm, handle.AddrOfPinnedObject(), framesPerRead);
                if (n < 0)
                {
                    if (!_running) break;
                    AlsaNative.snd_pcm_recover(_pcm, (int)n, 1);
                    Thread.Sleep(5);
                    continue;
                }
                if (n == 0) continue;

                var samples = new float[n];
                for (int i = 0; i < n; i++)
                    samples[i] = buffer[i] / 32768f;
                SamplesAvailable?.Invoke(samples);
            }
        }
        finally
        {
            handle.Free();
        }
    }

    public void Stop()
    {
        _running = false;
        _thread?.Join(1000);
        _thread = null;
        if (_pcm != IntPtr.Zero)
        {
            AlsaNative.snd_pcm_close(_pcm);
            _pcm = IntPtr.Zero;
        }
    }

    public void Dispose() => Stop();
}
