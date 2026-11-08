using System.Runtime.InteropServices;

namespace AprsLive.Audio.WinMm;

/// <summary>
/// Mono 16-bit PCM capture via WinMM (waveIn), double-buffered, driven by a
/// Win32 event handle rather than a native callback delegate. Not
/// exercisable on this development machine (Linux) — implemented against
/// the documented WinMM API and not yet verified on real Windows hardware.
/// </summary>
public sealed class WinMmAudioInput : IAudioInput
{
    private const int BufferFrames = 512;
    private const int BufferCount = 4;

    private IntPtr _hwi;
    private IntPtr _event;
    private Thread? _thread;
    private volatile bool _running;
    private readonly List<(WaveHdr Header, GCHandle DataHandle, GCHandle HeaderHandle)> _buffers = new();

    public event Action<float[]>? SamplesAvailable;

    public IReadOnlyList<AudioDeviceInfo> Enumerate()
    {
        var list = new List<AudioDeviceInfo> { new(WinMmNative.WaveMapper.ToString(), "Périphérique par défaut") };
        uint count = WinMmNative.waveInGetNumDevs();
        for (uint i = 0; i < count; i++)
        {
            if (WinMmNative.waveInGetDevCaps(new UIntPtr(i), out var caps, (uint)Marshal.SizeOf<WaveInCaps>()) == 0)
                list.Add(new AudioDeviceInfo(i.ToString(), caps.szPname));
        }
        return list;
    }

    public void Start(string? deviceId, int sampleRate)
    {
        uint deviceIndex = string.IsNullOrWhiteSpace(deviceId) || !uint.TryParse(deviceId, out var parsed)
            ? WinMmNative.WaveMapper
            : parsed;

        var format = WinMmNative.MonoPcm16(sampleRate);
        _event = WinMmNative.CreateEvent(IntPtr.Zero, false, false, null);

        uint rc = WinMmNative.waveInOpen(out _hwi, deviceIndex, ref format, _event, IntPtr.Zero, WinMmNative.CallbackEvent);
        if (rc != 0)
            throw new InvalidOperationException($"waveInOpen a échoué (code {rc}).");

        for (int i = 0; i < BufferCount; i++)
            QueueBuffer();

        rc = WinMmNative.waveInStart(_hwi);
        if (rc != 0)
            throw new InvalidOperationException($"waveInStart a échoué (code {rc}).");

        _running = true;
        _thread = new Thread(EventLoop) { IsBackground = true, Name = "WinMmCapture" };
        _thread.Start();
    }

    private void QueueBuffer()
    {
        var data = new byte[BufferFrames * 2];
        var dataHandle = GCHandle.Alloc(data, GCHandleType.Pinned);
        var header = new WaveHdr
        {
            lpData = dataHandle.AddrOfPinnedObject(),
            dwBufferLength = (uint)data.Length,
        };
        var headerHandle = GCHandle.Alloc(header, GCHandleType.Pinned);

        WinMmNative.waveInPrepareHeader(_hwi, ref header, Marshal.SizeOf<WaveHdr>());
        WinMmNative.waveInAddBuffer(_hwi, ref header, Marshal.SizeOf<WaveHdr>());

        Marshal.StructureToPtr(header, headerHandle.AddrOfPinnedObject(), false);
        _buffers.Add((header, dataHandle, headerHandle));
    }

    private void EventLoop()
    {
        while (_running)
        {
            uint wait = WinMmNative.WaitForSingleObject(_event, 200);
            if (!_running) break;
            if (wait != 0) continue; // timeout, loop to re-check _running

            for (int i = 0; i < _buffers.Count; i++)
            {
                var (header, dataHandle, headerHandle) = _buffers[i];
                var current = Marshal.PtrToStructure<WaveHdr>(headerHandle.AddrOfPinnedObject());
                if ((current.dwFlags & WinMmNative.WhdrDone) == 0)
                    continue;

                int n = (int)current.dwBytesRecorded / 2;
                var samples = new float[n];
                var bytes = new byte[current.dwBytesRecorded];
                Marshal.Copy(current.lpData, bytes, 0, bytes.Length);
                for (int s = 0; s < n; s++)
                    samples[s] = BitConverter.ToInt16(bytes, s * 2) / 32768f;
                SamplesAvailable?.Invoke(samples);

                // requeue this buffer
                WinMmNative.waveInAddBuffer(_hwi, ref current, Marshal.SizeOf<WaveHdr>());
                Marshal.StructureToPtr(current, headerHandle.AddrOfPinnedObject(), false);
                _buffers[i] = (current, dataHandle, headerHandle);
            }
        }
    }

    public void Stop()
    {
        _running = false;
        if (_hwi != IntPtr.Zero)
        {
            WinMmNative.waveInStop(_hwi);
            WinMmNative.waveInReset(_hwi);
        }
        _thread?.Join(1000);
        _thread = null;

        foreach (var (header, dataHandle, headerHandle) in _buffers)
        {
            var h = header;
            WinMmNative.waveInUnprepareHeader(_hwi, ref h, Marshal.SizeOf<WaveHdr>());
            dataHandle.Free();
            headerHandle.Free();
        }
        _buffers.Clear();

        if (_hwi != IntPtr.Zero) { WinMmNative.waveInClose(_hwi); _hwi = IntPtr.Zero; }
        if (_event != IntPtr.Zero) { WinMmNative.CloseHandle(_event); _event = IntPtr.Zero; }
    }

    public void Dispose() => Stop();
}
