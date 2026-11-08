using System.Globalization;
using System.Text;
using AprsLive.Core.Ax25;

namespace AprsLive.Core.Aprs;

/// <summary>
/// Parser for the APRS information field: position (uncompressed &amp;
/// compressed &amp; MIC-E), object, item, message (incl. ack/rej), and
/// status. Anything else comes back as <see cref="AprsPacketKind.Unknown"/>
/// with the raw info in <see cref="AprsPacket.Comment"/>.
/// </summary>
public static class AprsParser
{
    public static AprsPacket Parse(Ax25Frame frame, DateTimeOffset? receivedAt = null)
    {
        var p = new AprsPacket { Frame = frame, ReceivedAt = receivedAt ?? DateTimeOffset.Now };
        var info = frame.Info;
        if (info.Length == 0)
            return p;

        char type = (char)info[0];
        switch (type)
        {
            case '!' or '=' or '/' or '@':
                ParsePosition(frame, p, hasTimestamp: type is '/' or '@');
                break;
            case ';':
                ParseObject(frame, p);
                break;
            case ')':
                ParseItem(frame, p);
                break;
            case ':':
                ParseMessage(p, frame.InfoText);
                break;
            case '>':
                p.Kind = AprsPacketKind.Status;
                p.Comment = frame.InfoText[1..].Trim();
                break;
            case '`' or '\'' or (char)0x1c or (char)0x1d:
                if (MicE.TryDecode(frame, p))
                    p.Kind = AprsPacketKind.Position;
                break;
            case 'T':
                p.Kind = AprsPacketKind.Telemetry;
                p.Comment = frame.InfoText;
                break;
            default:
                p.Comment = frame.InfoText;
                break;
        }
        return p;
    }

    // -------- position --------

    private static void ParsePosition(Ax25Frame frame, AprsPacket p, bool hasTimestamp)
    {
        var info = frame.Info;
        int i = 1;
        if (hasTimestamp)
            i += 7; // DHM/HMS timestamp, 7 chars

        if (info.Length <= i)
            return;

        // compressed form: symbol table char is a letter/'\'/'/' followed by
        // 4+4 base-91 chars; uncompressed starts with a digit or space, so the
        // first char alone tells them apart. The base-91 chars that follow may
        // well be digits: the first latitude one is, from ~40.5 to ~60.3 N.
        char first = (char)info[i];
        bool compressed = first is (>= 'A' and <= 'Z') or (>= 'a' and <= 'j') or '/' or '\\'
                          && info.Length >= i + 13;

        if (compressed && TryParseCompressed(info, i, p))
        {
            p.Kind = AprsPacketKind.Position;
            int after = i + 13;
            if (info.Length > after)
                p.Comment = Encoding.Latin1.GetString(info, after, info.Length - after).Trim();
            return;
        }

        // uncompressed: "DDMM.mmN/DDDMM.mmW$" (19 chars) then comment
        if (info.Length < i + 19)
            return;
        string lat = Encoding.Latin1.GetString(info, i, 8);
        char latHem = (char)info[i + 7];
        char table = (char)info[i + 8];
        string lon = Encoding.Latin1.GetString(info, i + 9, 9);
        char lonHem = (char)info[i + 17];
        char code = (char)info[i + 18];

        if (!TryDecodeLat(lat, latHem, out double latVal) ||
            !TryDecodeLon(lon, lonHem, out double lonVal))
            return;

        p.Latitude = latVal;
        p.Longitude = lonVal;
        p.SymbolTable = table;
        p.SymbolCode = code;
        p.Kind = AprsPacketKind.Position;

        string rest = info.Length > i + 19
            ? Encoding.Latin1.GetString(info, i + 19, info.Length - i - 19)
            : string.Empty;
        ParseCourseSpeedAltComment(rest, p);
    }

    private static bool TryParseCompressed(byte[] info, int i, AprsPacket p)
    {
        // /YYYYXXXX$cs T  -> table, 4 lat, 4 lon, symbol, 2 course/speed, type
        char table = (char)info[i];
        int Y = B91(info, i + 1, 4);
        int X = B91(info, i + 5, 4);
        char code = (char)info[i + 9];
        if (Y < 0 || X < 0)
            return false;

        p.Latitude = Math.Round(90.0 - Y / 380926.0, 6);
        p.Longitude = Math.Round(-180.0 + X / 190463.0, 6);
        p.SymbolTable = table;
        p.SymbolCode = code;

        int c = info[i + 10] - 33;
        int s = info[i + 11] - 33;
        if (c is >= 0 and <= 89)
        {
            p.CourseDeg = c * 4;
            p.SpeedKnots = Math.Round(Math.Pow(1.08, s) - 1.0, 1);
        }
        return true;
    }

    private static int B91(byte[] b, int off, int len)
    {
        int v = 0;
        for (int k = 0; k < len; k++)
        {
            int d = b[off + k] - 33;
            if (d is < 0 or > 90) return -1;
            v = v * 91 + d;
        }
        return v;
    }

    private static bool TryDecodeLat(string s, char hem, out double value)
    {
        value = 0;
        // DDMM.mm
        if (s.Length < 7) return false;
        s = s.Replace(' ', '0'); // position ambiguity
        if (!int.TryParse(s.AsSpan(0, 2), out int deg)) return false;
        if (!double.TryParse(s.AsSpan(2, 5), NumberStyles.Float, CultureInfo.InvariantCulture, out double min))
            return false;
        value = deg + min / 60.0;
        if (char.ToUpperInvariant(hem) == 'S') value = -value;
        value = Math.Round(value, 6);
        return Math.Abs(value) <= 90;
    }

    private static bool TryDecodeLon(string s, char hem, out double value)
    {
        value = 0;
        if (s.Length < 8) return false;
        s = s.Replace(' ', '0');
        if (!int.TryParse(s.AsSpan(0, 3), out int deg)) return false;
        if (!double.TryParse(s.AsSpan(3, 5), NumberStyles.Float, CultureInfo.InvariantCulture, out double min))
            return false;
        value = deg + min / 60.0;
        if (char.ToUpperInvariant(hem) == 'W') value = -value;
        value = Math.Round(value, 6);
        return Math.Abs(value) <= 180;
    }

    private static void ParseCourseSpeedAltComment(string rest, AprsPacket p)
    {
        // "CSE/SPD" -> "ddd/sss" at the start
        if (rest.Length >= 7 && rest[3] == '/' &&
            int.TryParse(rest.AsSpan(0, 3), out int cse) &&
            int.TryParse(rest.AsSpan(4, 3), out int spd))
        {
            p.CourseDeg = cse;
            p.SpeedKnots = spd;
            rest = rest[7..];
        }

        int alt = rest.IndexOf("/A=", StringComparison.Ordinal);
        if (alt >= 0 && rest.Length >= alt + 9 &&
            int.TryParse(rest.AsSpan(alt + 3, 6), out int feet))
        {
            p.AltitudeM = Math.Round(feet * 0.3048, 1);
            rest = rest.Remove(alt, 9);
        }

        p.Comment = rest.Trim();
    }

    // -------- object / item --------

    private static void ParseObject(Ax25Frame frame, AprsPacket p)
    {
        // ;NAME     *DDHHMMz<lat>...
        string s = frame.InfoText;
        if (s.Length < 18) return;
        p.ObjectName = s.Substring(1, 9).Trim();
        p.ObjectLive = s[10] == '*';
        p.Kind = AprsPacketKind.Object;
        // reuse position parsing on the tail (after name + flag + 7-char time)
        var tail = Encoding.Latin1.GetBytes(s[18..]);
        var pseudo = new Ax25Frame
        {
            Destination = frame.Destination, Source = frame.Source, Path = frame.Path,
            Control = frame.Control, Pid = frame.Pid,
            Info = Prepend((byte)'!', tail)
        };
        var pos = new AprsPacket { Frame = pseudo };
        ParsePosition(pseudo, pos, hasTimestamp: false);
        CopyPosition(pos, p);
    }

    private static void ParseItem(Ax25Frame frame, AprsPacket p)
    {
        // )NAME!<lat>...  (name is 3-9 chars, ends with ! or _)
        string s = frame.InfoText;
        int bang = s.IndexOfAny(new[] { '!', '_' }, 1);
        if (bang < 3) return;
        p.ObjectName = s[1..bang].Trim();
        p.ObjectLive = s[bang] == '!';
        p.Kind = AprsPacketKind.Item;
        var tail = Encoding.Latin1.GetBytes(s[(bang + 1)..]);
        var pseudo = new Ax25Frame
        {
            Destination = frame.Destination, Source = frame.Source, Path = frame.Path,
            Control = frame.Control, Pid = frame.Pid, Info = Prepend((byte)'!', tail)
        };
        var pos = new AprsPacket { Frame = pseudo };
        ParsePosition(pseudo, pos, hasTimestamp: false);
        CopyPosition(pos, p);
    }

    private static void CopyPosition(AprsPacket from, AprsPacket to)
    {
        to.Latitude = from.Latitude;
        to.Longitude = from.Longitude;
        to.SymbolTable = from.SymbolTable;
        to.SymbolCode = from.SymbolCode;
        to.CourseDeg = from.CourseDeg;
        to.SpeedKnots = from.SpeedKnots;
        to.AltitudeM = from.AltitudeM;
        to.Comment = from.Comment;
    }

    private static byte[] Prepend(byte b, byte[] rest)
    {
        var r = new byte[rest.Length + 1];
        r[0] = b;
        Array.Copy(rest, 0, r, 1, rest.Length);
        return r;
    }

    // -------- message --------

    private static void ParseMessage(AprsPacket p, string info)
    {
        // :ADDRESSEE:message text{nn   (ADDRESSEE is exactly 9 chars, space padded)
        if (info.Length < 11 || info[10] != ':')
            return;
        p.Kind = AprsPacketKind.Message;
        p.MessageAddressee = info.Substring(1, 9).Trim();
        string body = info[11..];

        if (body.StartsWith("ack", StringComparison.OrdinalIgnoreCase))
        {
            p.IsAck = true;
            p.MessageNumber = body[3..].Trim();
            p.MessageText = body;
            return;
        }
        if (body.StartsWith("rej", StringComparison.OrdinalIgnoreCase))
        {
            p.IsRej = true;
            p.MessageNumber = body[3..].Trim();
            p.MessageText = body;
            return;
        }

        int brace = body.LastIndexOf('{');
        if (brace >= 0)
        {
            p.MessageNumber = body[(brace + 1)..].Trim();
            body = body[..brace];
        }
        p.MessageText = body;
    }
}
