using System.Runtime.InteropServices;

namespace AprsLive.Audio.Alsa;

/// <summary>P/Invoke bindings for the subset of libasound (ALSA) used for simple mono capture/playback.</summary>
internal static class AlsaNative
{
    private const string Lib = "libasound.so.2";

    public const int StreamPlayback = 0;
    public const int StreamCapture = 1;

    public const int FormatS16Le = 2; // SND_PCM_FORMAT_S16_LE
    public const int AccessRwInterleaved = 3; // SND_PCM_ACCESS_RW_INTERLEAVED

    [DllImport(Lib)]
    public static extern int snd_pcm_open(out IntPtr pcm, string name, int stream, int mode);

    [DllImport(Lib)]
    public static extern int snd_pcm_set_params(IntPtr pcm, int format, int access, uint channels, uint rate, int softResample, uint latencyUs);

    [DllImport(Lib)]
    public static extern long snd_pcm_readi(IntPtr pcm, IntPtr buffer, ulong size);

    [DllImport(Lib)]
    public static extern long snd_pcm_writei(IntPtr pcm, IntPtr buffer, ulong size);

    [DllImport(Lib)]
    public static extern int snd_pcm_recover(IntPtr pcm, int err, int silent);

    [DllImport(Lib)]
    public static extern int snd_pcm_prepare(IntPtr pcm);

    [DllImport(Lib)]
    public static extern int snd_pcm_drain(IntPtr pcm);

    [DllImport(Lib)]
    public static extern int snd_pcm_drop(IntPtr pcm);

    [DllImport(Lib)]
    public static extern int snd_pcm_close(IntPtr pcm);

    [DllImport(Lib)]
    public static extern int snd_pcm_state(IntPtr pcm);

    [DllImport(Lib)]
    public static extern int snd_pcm_start(IntPtr pcm);

    [DllImport(Lib)]
    public static extern long snd_pcm_avail(IntPtr pcm);

    [DllImport(Lib)]
    private static extern IntPtr snd_strerror(int errnum);

    public static string StrError(long errnum)
    {
        try
        {
            var ptr = snd_strerror((int)errnum);
            return ptr == IntPtr.Zero ? $"erreur {errnum}" : Marshal.PtrToStringAnsi(ptr) ?? $"erreur {errnum}";
        }
        catch
        {
            return $"erreur {errnum}";
        }
    }
}
