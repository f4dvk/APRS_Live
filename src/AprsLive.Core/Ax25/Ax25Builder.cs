using System.Text;

namespace AprsLive.Core.Ax25;

/// <summary>Builds AX.25 UI frames (no FCS — KISS adds none).</summary>
public static class Ax25Builder
{
    /// <summary>
    /// Build a UI frame. <paramref name="path"/> entries may carry a trailing
    /// <c>*</c> to set the has-been-repeated bit.
    /// </summary>
    public static byte[] BuildUi(string source, string destination, IEnumerable<string> path, string info)
    {
        var bytes = new List<byte>(64);
        var addresses = new List<string> { destination, source };
        addresses.AddRange(path);

        for (int i = 0; i < addresses.Count; i++)
            WriteAddress(bytes, addresses[i], last: i == addresses.Count - 1);

        bytes.Add(0x03); // UI control
        bytes.Add(0xF0); // no layer-3 protocol
        bytes.AddRange(Encoding.Latin1.GetBytes(info));
        return bytes.ToArray();
    }

    /// <summary>
    /// An APRS message frame: <c>:ADDRESSEE :text{nn</c>. <paramref name="destination"/>
    /// and <paramref name="path"/> come from the station's own settings (tocall,
    /// digipeater path) — there is no built-in default, unlike UIView32-style
    /// tools that hardcode an experimental tocall.
    /// </summary>
    public static byte[] BuildMessage(string source, string addressee, string text,
        string destination, IEnumerable<string> path, string? number = null)
    {
        string body = $":{addressee.PadRight(9)[..9]}:{text}";
        if (!string.IsNullOrEmpty(number))
            body += "{" + number;
        return BuildUi(source, destination, path, body);
    }

    /// <summary>An APRS ack for a received message number.</summary>
    public static byte[] BuildAck(string source, string addressee, string number,
        string destination, IEnumerable<string> path) =>
        BuildUi(source, destination, path,
            $":{addressee.PadRight(9)[..9]}:ack{number}");

    /// <summary>
    /// An uncompressed APRS position report (no timestamp): <c>!DDMM.mmN&lt;table&gt;DDDMM.mmE&lt;code&gt;comment</c>
    /// — the station beacon sent from the Station tab, manually or on its interval.
    /// </summary>
    public static byte[] BuildPositionReport(string source, string destination, IEnumerable<string> path,
        double latitude, double longitude, char symbolTable, char symbolCode, string comment) =>
        BuildUi(source, destination, path,
            $"!{FormatLat(latitude)}{symbolTable}{FormatLon(longitude)}{symbolCode}{comment}");

    /// <summary>The APRS recommended max length for a status text (APRS101 §16) — enforced by truncation, never by rejecting the send.</summary>
    public const int MaxStatusTextLength = 62;

    /// <summary>An APRS status report (no timestamp): <c>&gt;free text</c>, truncated to <see cref="MaxStatusTextLength"/>.</summary>
    public static byte[] BuildStatusReport(string source, string destination, IEnumerable<string> path, string statusText)
    {
        string text = statusText.Length > MaxStatusTextLength ? statusText[..MaxStatusTextLength] : statusText;
        return BuildUi(source, destination, path, $">{text}");
    }

    /// <summary>
    /// An APRS object report: <c>;NAME     *DDHHMMzDDMM.mmN/DDDMM.mmWscomment</c> —
    /// sent under a made-up 9-character name rather than the station's own
    /// callsign, to mark something that isn't itself a transmitting station
    /// (an event, a relay, a hazard...). <paramref name="live"/> false sends
    /// a kill (<c>_</c>) instead of a live (<c>*</c>) report, retracting it.
    /// </summary>
    public static byte[] BuildObjectReport(string source, string destination, IEnumerable<string> path,
        string objectName, bool live, DateTime timestampUtc,
        double latitude, double longitude, char symbolTable, char symbolCode, string comment) =>
        BuildUi(source, destination, path,
            $";{objectName.PadRight(9)[..9]}{(live ? '*' : '_')}{timestampUtc:ddHHmm}z" +
            $"{FormatLat(latitude)}{symbolTable}{FormatLon(longitude)}{symbolCode}{comment}");

    private static string FormatLat(double lat)
    {
        char hemi = lat < 0 ? 'S' : 'N';
        double a = Math.Abs(lat);
        int deg = (int)a;
        double min = (a - deg) * 60;
        return $"{deg:00}{min:00.00}{hemi}";
    }

    private static string FormatLon(double lon)
    {
        char hemi = lon < 0 ? 'W' : 'E';
        double a = Math.Abs(lon);
        int deg = (int)a;
        double min = (a - deg) * 60;
        return $"{deg:000}{min:00.00}{hemi}";
    }

    private static void WriteAddress(List<byte> outBuf, string spec, bool last)
    {
        bool repeated = spec.EndsWith('*');
        if (repeated) spec = spec[..^1];

        string call = spec;
        int ssid = 0;
        int dash = spec.IndexOf('-');
        if (dash >= 0)
        {
            call = spec[..dash];
            _ = int.TryParse(spec[(dash + 1)..], out ssid);
        }
        call = call.ToUpperInvariant();

        for (int i = 0; i < 6; i++)
        {
            char c = i < call.Length ? call[i] : ' ';
            outBuf.Add((byte)(c << 1));
        }

        byte ssidByte = (byte)(0x60 | ((ssid & 0x0F) << 1));
        if (repeated) ssidByte |= 0x80;
        if (last) ssidByte |= 0x01;
        outBuf.Add(ssidByte);
    }
}
