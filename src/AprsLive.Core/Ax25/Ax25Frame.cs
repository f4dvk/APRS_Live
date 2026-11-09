using System.Text;

namespace AprsLive.Core.Ax25;

/// <summary>
/// A decoded AX.25 UI frame as used by APRS: destination, source, up to 8
/// digipeaters, and the information field. FCS is assumed already stripped
/// (KISS frames do not carry it).
/// </summary>
public sealed class Ax25Frame
{
    public required Ax25Address Destination { get; init; }
    public required Ax25Address Source { get; init; }
    public required IReadOnlyList<Ax25Address> Path { get; init; }
    public required byte Control { get; init; }
    public required byte Pid { get; init; }
    public required byte[] Info { get; init; }

    /// <summary>Info field as Latin-1 text (APRS is byte-oriented ASCII-ish).</summary>
    public string InfoText => Encoding.Latin1.GetString(Info);

    public bool IsUi => (Control & 0xEF) == 0x03;

    /// <summary>
    /// The path carries an APRS-IS marker (TCPIP, TCPXX or a q construct such
    /// as qAR): the packet came off the Internet, even if it arrived on a radio
    /// port — e.g. a KISS TNC that bridges APRS-IS traffic to its clients.
    /// </summary>
    public bool HasInternetPath => Path.Any(a =>
        a.Callsign.Equals("TCPIP", StringComparison.OrdinalIgnoreCase) ||
        a.Callsign.Equals("TCPXX", StringComparison.OrdinalIgnoreCase) ||
        (a.Callsign.Length == 3 && a.Callsign.StartsWith("qA", StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// Path entries the sender itself wrote in, at the head of the path: a
    /// digi beaconing through its own digipeater sends
    /// <c>F1CNH-3&gt;APRS,F1CNH-3*,WIDE2-1</c> (with <c>F1CNH-3*,WIDE1*</c>, the
    /// alias it used up too). Nobody else has repeated those hops.
    /// </summary>
    public int SenderHops
    {
        get
        {
            if (Path.Count == 0
                || Path[0].Ssid != Source.Ssid
                || !string.Equals(Path[0].Callsign, Source.Callsign, StringComparison.OrdinalIgnoreCase))
                return 0;
            // A used alias right after it: used up with it (WIDE2 with no SSID left, on APRS-IS no star).
            return Path.Count > 1 && Path[1] is { IsGenericAlias: true } next && (next.HasBeenRepeated || next.Ssid == 0) ? 2 : 1;
        }
    }

    /// <summary>
    /// Repeated by a digipeater: a hop marked repeated, other than the
    /// sender's own (<see cref="SenderHops"/>). Otherwise heard direct.
    /// </summary>
    public bool HasBeenDigipeated => Path.Skip(SenderHops).Any(a => a.HasBeenRepeated);

    /// <summary>TNC2 monitor form: <c>SRC&gt;DST,PATH:info</c>.</summary>
    public string ToTnc2()
    {
        var sb = new StringBuilder();
        sb.Append(Source).Append('>').Append(Destination);
        foreach (var d in Path)
            sb.Append(',').Append(d.ToStringWithFlag());
        sb.Append(':').Append(InfoText);
        return sb.ToString();
    }

    public static bool TryParse(ReadOnlySpan<byte> frame, out Ax25Frame? result)
    {
        result = null;
        if (frame.Length < 16) // 2 addresses + control + pid
            return false;

        var path = new List<Ax25Address>();
        int pos = 0;
        Ax25Address dest = Ax25Address.Decode(frame.Slice(pos, 7), out bool last);
        pos += 7;
        if (last || frame.Length < pos + 7)
            return false;
        Ax25Address src = Ax25Address.Decode(frame.Slice(pos, 7), out last);
        pos += 7;

        while (!last)
        {
            if (path.Count >= 8 || frame.Length < pos + 7)
                return false;
            path.Add(Ax25Address.Decode(frame.Slice(pos, 7), out last));
            pos += 7;
        }

        if (frame.Length < pos + 2)
            return false;
        byte control = frame[pos++];
        byte pid = frame[pos++];

        result = new Ax25Frame
        {
            Destination = dest,
            Source = src,
            Path = path,
            Control = control,
            Pid = pid,
            Info = frame[pos..].ToArray()
        };
        return result.IsUi;
    }
}
