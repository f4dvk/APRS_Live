using System.Runtime.InteropServices;

namespace AprsLive.Audio.WinMm;

[StructLayout(LayoutKind.Sequential)]
internal struct WaveFormatEx
{
    public ushort wFormatTag;
    public ushort nChannels;
    public uint nSamplesPerSec;
    public uint nAvgBytesPerSec;
    public ushort nBlockAlign;
    public ushort wBitsPerSample;
    public ushort cbSize;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WaveHdr
{
    public IntPtr lpData;
    public uint dwBufferLength;
    public uint dwBytesRecorded;
    public IntPtr dwUser;
    public uint dwFlags;
    public uint dwLoops;
    public IntPtr lpNext;
    public IntPtr reserved;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct WaveInCaps
{
    public ushort wMid;
    public ushort wPid;
    public uint vDriverVersion;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string szPname;
    public uint dwFormats;
    public ushort wChannels;
    public ushort wReserved1;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct WaveOutCaps
{
    public ushort wMid;
    public ushort wPid;
    public uint vDriverVersion;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string szPname;
    public uint dwFormats;
    public ushort wChannels;
    public ushort wReserved1;
    public uint dwSupport;
}

/// <summary>
/// P/Invoke bindings for the subset of winmm.dll used for simple mono
/// capture/playback, using the CALLBACK_EVENT mechanism (a Win32 event
/// handle signaled on buffer completion) rather than CALLBACK_FUNCTION —
/// no native callback/delegate marshaling to get wrong, just
/// WaitForSingleObject on a background thread.
/// </summary>
internal static class WinMmNative
{
    public const uint WaveFormatPcm = 1;
    public const uint CallbackEvent = 0x00050000;
    public const uint WhdrDone = 0x00000001;
    public const uint WaveMapper = unchecked((uint)-1);

    [DllImport("winmm.dll")]
    public static extern uint waveInGetNumDevs();

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    public static extern uint waveInGetDevCaps(UIntPtr uDeviceID, out WaveInCaps caps, uint cbwic);

    [DllImport("winmm.dll")]
    public static extern uint waveInOpen(out IntPtr phwi, uint uDeviceID, ref WaveFormatEx pwfx, IntPtr dwCallback, IntPtr dwInstance, uint fdwOpen);

    [DllImport("winmm.dll")]
    public static extern uint waveInPrepareHeader(IntPtr hwi, ref WaveHdr pwh, int cbwh);

    [DllImport("winmm.dll")]
    public static extern uint waveInUnprepareHeader(IntPtr hwi, ref WaveHdr pwh, int cbwh);

    [DllImport("winmm.dll")]
    public static extern uint waveInAddBuffer(IntPtr hwi, ref WaveHdr pwh, int cbwh);

    [DllImport("winmm.dll")]
    public static extern uint waveInStart(IntPtr hwi);

    [DllImport("winmm.dll")]
    public static extern uint waveInStop(IntPtr hwi);

    [DllImport("winmm.dll")]
    public static extern uint waveInReset(IntPtr hwi);

    [DllImport("winmm.dll")]
    public static extern uint waveInClose(IntPtr hwi);

    [DllImport("winmm.dll")]
    public static extern uint waveOutGetNumDevs();

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    public static extern uint waveOutGetDevCaps(UIntPtr uDeviceID, out WaveOutCaps caps, uint cbwoc);

    [DllImport("winmm.dll")]
    public static extern uint waveOutOpen(out IntPtr phwo, uint uDeviceID, ref WaveFormatEx pwfx, IntPtr dwCallback, IntPtr dwInstance, uint fdwOpen);

    [DllImport("winmm.dll")]
    public static extern uint waveOutPrepareHeader(IntPtr hwo, ref WaveHdr pwh, int cbwh);

    [DllImport("winmm.dll")]
    public static extern uint waveOutUnprepareHeader(IntPtr hwo, ref WaveHdr pwh, int cbwh);

    [DllImport("winmm.dll")]
    public static extern uint waveOutWrite(IntPtr hwo, ref WaveHdr pwh, int cbwh);

    [DllImport("winmm.dll")]
    public static extern uint waveOutReset(IntPtr hwo);

    [DllImport("winmm.dll")]
    public static extern uint waveOutClose(IntPtr hwo);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string? lpName);

    [DllImport("kernel32.dll")]
    public static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll")]
    public static extern bool CloseHandle(IntPtr hObject);

    public static WaveFormatEx MonoPcm16(int sampleRate) => new()
    {
        wFormatTag = (ushort)WaveFormatPcm,
        nChannels = 1,
        nSamplesPerSec = (uint)sampleRate,
        wBitsPerSample = 16,
        nBlockAlign = 2,
        nAvgBytesPerSec = (uint)sampleRate * 2,
        cbSize = 0,
    };
}
