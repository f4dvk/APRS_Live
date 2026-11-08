using System.Text.RegularExpressions;

namespace AprsLive.Audio.Alsa;

/// <summary>
/// Lists ALSA devices by reading <c>/proc/asound/cards</c> rather than
/// walking ALSA's native device-hint API — far less native marshaling for
/// the same practical result, and works identically whether audio is
/// routed through PulseAudio/PipeWire's ALSA plugin or bare hardware.
/// </summary>
internal static partial class AlsaDeviceEnumerator
{
    public static IReadOnlyList<AudioDeviceInfo> Enumerate()
    {
        var devices = new List<AudioDeviceInfo> { new("default", "Périphérique par défaut") };

        const string path = "/proc/asound/cards";
        if (!File.Exists(path))
            return devices;

        try
        {
            foreach (var line in File.ReadAllLines(path))
            {
                var match = CardLineRegex().Match(line);
                if (!match.Success)
                    continue;

                string index = match.Groups[1].Value;
                string id = match.Groups[2].Value.Trim();
                string description = match.Groups[3].Value.Trim();

                // "plughw" lets ALSA convert rate/format as needed — the raw
                // "hw" device often refuses anything but its native format.
                devices.Add(new AudioDeviceInfo($"plughw:{index},0", $"{description} ({id})"));
            }
        }
        catch
        {
            // best effort — "default" above is still usable
        }

        return devices;
    }

    [GeneratedRegex(@"^\s*(\d+)\s*\[([^\]]+)\]:\s*\S+\s*-\s*(.+)$")]
    private static partial Regex CardLineRegex();
}
