using System.Runtime.InteropServices;

namespace AprsLive.Audio.Alsa;

public sealed class AlsaAudioOutput : IAudioOutput
{
    private IntPtr _pcm;

    public IReadOnlyList<AudioDeviceInfo> Enumerate() => AlsaDeviceEnumerator.Enumerate();

    public void Start(string? deviceId, int sampleRate)
    {
        string name = string.IsNullOrWhiteSpace(deviceId) ? "default" : deviceId;
        int rc = AlsaNative.snd_pcm_open(out _pcm, name, AlsaNative.StreamPlayback, 0);
        if (rc < 0)
            throw new InvalidOperationException($"Ouverture ALSA (lecture, {name}) impossible : {AlsaNative.StrError(rc)}");

        rc = AlsaNative.snd_pcm_set_params(_pcm, AlsaNative.FormatS16Le, AlsaNative.AccessRwInterleaved,
            channels: 1, rate: (uint)sampleRate, softResample: 1, latencyUs: 100_000);
        if (rc < 0)
        {
            AlsaNative.snd_pcm_close(_pcm);
            _pcm = IntPtr.Zero;
            throw new InvalidOperationException($"Configuration ALSA (lecture, {name}) impossible : {AlsaNative.StrError(rc)}");
        }
    }

    /// <summary>
    /// Writes all samples then calls snd_pcm_drain, which only returns once
    /// the hardware has actually finished playing them — this is what lets a
    /// PTT keyer release right when the audio truly stops, not merely when
    /// it was handed to the driver's buffer.
    /// </summary>
    public Task PlayAsync(float[] samples) => Task.Run(() =>
    {
        if (_pcm == IntPtr.Zero)
            throw new InvalidOperationException("Sortie audio ALSA non démarrée.");

        var pcm16 = new short[samples.Length];
        for (int i = 0; i < samples.Length; i++)
            pcm16[i] = (short)Math.Clamp(samples[i] * 32767f, short.MinValue, short.MaxValue);

        var handle = GCHandle.Alloc(pcm16, GCHandleType.Pinned);
        try
        {
            int offset = 0;
            while (offset < pcm16.Length)
            {
                int remaining = pcm16.Length - offset;
                IntPtr ptr = handle.AddrOfPinnedObject() + offset * sizeof(short);
                long n = AlsaNative.snd_pcm_writei(_pcm, ptr, (ulong)remaining);
                if (n < 0)
                {
                    AlsaNative.snd_pcm_recover(_pcm, (int)n, 1);
                    continue;
                }
                offset += (int)n;
            }
        }
        finally
        {
            handle.Free();
        }

        AlsaNative.snd_pcm_drain(_pcm);
        AlsaNative.snd_pcm_prepare(_pcm); // ready for the next burst
    });

    public void Stop()
    {
        if (_pcm != IntPtr.Zero)
        {
            AlsaNative.snd_pcm_drop(_pcm);
            AlsaNative.snd_pcm_close(_pcm);
            _pcm = IntPtr.Zero;
        }
    }

    public void Dispose() => Stop();
}
