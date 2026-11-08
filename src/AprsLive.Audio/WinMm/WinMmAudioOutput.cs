using System.Runtime.InteropServices;

namespace AprsLive.Audio.WinMm;

/// <summary>
/// Mono 16-bit PCM playback via WinMM (waveOut), one buffer per
/// <see cref="PlayAsync"/> call, driven by a Win32 event handle. Not
/// exercisable on this development machine (Linux) — implemented against
/// the documented WinMM API and not yet verified on real Windows hardware.
/// </summary>
public sealed class WinMmAudioOutput : IAudioOutput
{
    private IntPtr _hwo;
    private IntPtr _event;
    private int _sampleRate;

    public IReadOnlyList<AudioDeviceInfo> Enumerate()
    {
        var list = new List<AudioDeviceInfo> { new(WinMmNative.WaveMapper.ToString(), "Périphérique par défaut") };
        uint count = WinMmNative.waveOutGetNumDevs();
        for (uint i = 0; i < count; i++)
        {
            if (WinMmNative.waveOutGetDevCaps(new UIntPtr(i), out var caps, (uint)Marshal.SizeOf<WaveOutCaps>()) == 0)
                list.Add(new AudioDeviceInfo(i.ToString(), caps.szPname));
        }
        return list;
    }

    public void Start(string? deviceId, int sampleRate)
    {
        _sampleRate = sampleRate;
        uint deviceIndex = string.IsNullOrWhiteSpace(deviceId) || !uint.TryParse(deviceId, out var parsed)
            ? WinMmNative.WaveMapper
            : parsed;

        var format = WinMmNative.MonoPcm16(sampleRate);
        _event = WinMmNative.CreateEvent(IntPtr.Zero, false, false, null);

        uint rc = WinMmNative.waveOutOpen(out _hwo, deviceIndex, ref format, _event, IntPtr.Zero, WinMmNative.CallbackEvent);
        if (rc != 0)
            throw new InvalidOperationException($"waveOutOpen a échoué (code {rc}).");
    }

    public Task PlayAsync(float[] samples) => Task.Run(() =>
    {
        if (_hwo == IntPtr.Zero)
            throw new InvalidOperationException("Sortie audio WinMM non démarrée.");

        var bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short s = (short)Math.Clamp(samples[i] * 32767f, short.MinValue, short.MaxValue);
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 2, 2), s);
        }

        var dataHandle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        var header = new WaveHdr { lpData = dataHandle.AddrOfPinnedObject(), dwBufferLength = (uint)bytes.Length };
        var headerHandle = GCHandle.Alloc(header, GCHandleType.Pinned);
        try
        {
            WinMmNative.waveOutPrepareHeader(_hwo, ref header, Marshal.SizeOf<WaveHdr>());
            WinMmNative.waveOutWrite(_hwo, ref header, Marshal.SizeOf<WaveHdr>());
            Marshal.StructureToPtr(header, headerHandle.AddrOfPinnedObject(), false);

            while (true)
            {
                var current = Marshal.PtrToStructure<WaveHdr>(headerHandle.AddrOfPinnedObject());
                if ((current.dwFlags & WinMmNative.WhdrDone) != 0)
                    break;
                WinMmNative.WaitForSingleObject(_event, 500);
            }

            var finalHeader = Marshal.PtrToStructure<WaveHdr>(headerHandle.AddrOfPinnedObject());
            WinMmNative.waveOutUnprepareHeader(_hwo, ref finalHeader, Marshal.SizeOf<WaveHdr>());
        }
        finally
        {
            dataHandle.Free();
            headerHandle.Free();
        }
    });

    public void Stop()
    {
        if (_hwo != IntPtr.Zero)
        {
            WinMmNative.waveOutReset(_hwo);
            WinMmNative.waveOutClose(_hwo);
            _hwo = IntPtr.Zero;
        }
        if (_event != IntPtr.Zero) { WinMmNative.CloseHandle(_event); _event = IntPtr.Zero; }
    }

    public void Dispose() => Stop();
}
