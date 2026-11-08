using AprsLive.Audio.Alsa;
using AprsLive.Audio.WinMm;

namespace AprsLive.Audio;

/// <summary>Picks the OS-native audio backend — no bundled native libraries, no RID-specific packaging (see APRS_Live's build notes on why WinMM/ALSA were chosen over PortAudio).</summary>
public static class AudioBackend
{
    public static IAudioInput CreateInput()
    {
        if (OperatingSystem.IsWindows()) return new WinMmAudioInput();
        if (OperatingSystem.IsLinux()) return new AlsaAudioInput();
        throw new PlatformNotSupportedException("Modem audio non pris en charge sur cette plateforme.");
    }

    public static IAudioOutput CreateOutput()
    {
        if (OperatingSystem.IsWindows()) return new WinMmAudioOutput();
        if (OperatingSystem.IsLinux()) return new AlsaAudioOutput();
        throw new PlatformNotSupportedException("Modem audio non pris en charge sur cette plateforme.");
    }
}
