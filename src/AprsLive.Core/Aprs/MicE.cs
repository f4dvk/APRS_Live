using System.Text;
using System.Text.RegularExpressions;
using AprsLive.Core.Ax25;

namespace AprsLive.Core.Aprs;

/// <summary>
/// MIC-E position decoding (APRS 1.01 chapter 10, with the APRS 1.2 additions).
/// The 6-character AX.25 destination carries the latitude digits (with
/// position ambiguity), the 3 message bits, N/S, the longitude offset and E/W;
/// the information field the longitude, speed, course and symbol, then a free
/// text: type byte, optional <c>aaa}</c> altitude, text (frequency, <c>!DAO!</c>
/// extra precision...) and, for most makes, a device code at the end — see
/// <see cref="MicEDevice"/>.
/// </summary>
public static partial class MicE
{
    // Standard messages by their A/B/C bits (index = A*4 + B*2 + C); 0 is the emergency.
    private static readonly string[] StandardMessages =
        ["Urgence", "Priorité", "Spécial", "Engagé", "Retour", "En service", "En route", "Hors service"];

    public const string Emergency = "Urgence";

    public static bool TryDecode(Ax25Frame frame, AprsPacket packet)
    {
        string dest = frame.Destination.Callsign;
        var info = frame.Info;
        if (dest.Length != 6 || info.Length < 8)
            return false;

        // --- destination: one latitude digit and one bit per character ---
        Span<int> digits = stackalloc int[6];
        Span<bool> bits = stackalloc bool[6];
        bool custom = false, standard = false;
        int ambiguity = 0;
        for (int i = 0; i < 6; i++)
        {
            char c = dest[i];
            int digit;
            bool one;
            if (c is >= '0' and <= '9') { digit = c - '0'; one = false; }
            else if (c == 'L') { digit = -1; one = false; }
            else if (c is >= 'P' and <= 'Y') { digit = c - 'P'; one = true; standard |= i < 3; }
            else if (c == 'Z') { digit = -1; one = true; standard |= i < 3; }
            // A-K only in the message characters, as custom message bits
            else if (i < 3 && c is >= 'A' and <= 'J') { digit = c - 'A'; one = true; custom = true; }
            else if (i < 3 && c == 'K') { digit = -1; one = true; custom = true; }
            else return false;

            // K, L and Z stand for a space: position ambiguity, from the end only.
            if (digit < 0) ambiguity++;
            else if (ambiguity > 0) return false;
            digits[i] = digit;
            bits[i] = one;
        }
        if (ambiguity > 4)
            return false;

        // Ambiguity: the middle of the box the remaining digits leave.
        if (ambiguity == 4)
        {
            digits[2] = 3;
            digits[3] = digits[4] = digits[5] = 0;
        }
        else if (ambiguity > 0)
        {
            digits[6 - ambiguity] = 5;
            for (int i = 7 - ambiguity; i < 6; i++)
                digits[i] = 0;
        }

        int latDeg = digits[0] * 10 + digits[1];
        double latMin = digits[2] * 10 + digits[3] + (digits[4] * 10 + digits[5]) / 100.0;
        if (latMin >= 60 || latDeg > 90 || (latDeg == 90 && latMin > 0))
            return false;
        bool north = bits[3], lonOffset = bits[4], west = bits[5];

        // --- information field: longitude, speed, course, symbol ---
        int lonDeg = info[1] - 28 + (lonOffset ? 100 : 0);
        if (lonDeg is >= 180 and <= 189) lonDeg -= 80;
        else if (lonDeg is >= 190 and <= 199) lonDeg -= 190;
        int lonMinInt = info[2] - 28;
        if (lonMinInt >= 60) lonMinInt -= 60;
        int lonHund = info[3] - 28;
        if (lonDeg is < 0 or > 179 || lonMinInt is < 0 or > 59 || lonHund is < 0 or > 99)
            return false;

        // The same ambiguity applies to the longitude minutes (in hundredths here).
        int lonHundredths = lonMinInt * 100 + lonHund;
        lonHundredths = ambiguity switch
        {
            1 => lonHundredths / 10 * 10 + 5,
            2 => lonHundredths / 100 * 100 + 50,
            3 => lonHundredths / 1000 * 1000 + 500,
            4 => 3000,
            _ => lonHundredths,
        };
        double lonMin = lonHundredths / 100.0;

        int sp = info[4] - 28, dc = info[5] - 28, se = info[6] - 28;
        if (sp >= 0 && dc >= 0 && se >= 0)
        {
            int speed = sp * 10 + dc / 10;
            int course = dc % 10 * 100 + se;
            if (speed >= 800) speed -= 800;
            if (course >= 400) course -= 400;
            packet.SpeedKnots = speed;
            packet.CourseDeg = course;
        }

        packet.SymbolCode = (char)info[7];
        packet.SymbolTable = info.Length > 8 ? (char)info[8] : '/';

        // --- message bits ---
        int message = (bits[0] ? 4 : 0) + (bits[1] ? 2 : 0) + (bits[2] ? 1 : 0);
        packet.IsEmergency = message == 0;
        packet.MicEStatus = message == 0 ? Emergency
            : custom && standard ? "Inconnu"
            : custom ? $"Perso {7 - message}"
            : StandardMessages[message];

        // --- free text ---
        string text = info.Length > 9 ? Encoding.Latin1.GetString(info, 9, info.Length - 9) : "";
        // The TM-D710 now and then puts NULs in it; a CR may end it. Not the
        // trailing spaces though: the VX-8's device code ends with one.
        text = text.Replace("\0", "").TrimEnd('\r', '\n');
        packet.Device = MicEDevice.Identify(text, out string body);

        // "aaa}": altitude in metres + 10000, base 91, first after the type byte.
        if (body.Length >= 4 && body[3] == '}' && IsBase91(body[0]) && IsBase91(body[1]) && IsBase91(body[2]))
        {
            int alt = (body[0] - 33) * 91 * 91 + (body[1] - 33) * 91 + (body[2] - 33) - 10000;
            packet.AltitudeM = alt;
            body = body[4..];
        }
        else if (AltitudeFeet().Match(body) is { Success: true } a)
        {
            packet.AltitudeM = Math.Round(int.Parse(a.Groups[1].Value) * 0.3048, 1);
            body = body.Remove(a.Index, a.Length);
        }

        // !DAO!: one more digit of latitude and longitude minutes (precision only when unambiguous).
        double latExtra = 0, lonExtra = 0;
        if (Dao().Matches(body) is { Count: > 0 } daos)
        {
            var dao = daos[^1];
            if (ambiguity == 0)
                (latExtra, lonExtra) = DaoMinutes(dao.Value[1], dao.Value[2], dao.Value[3]);
            body = body.Remove(dao.Index, dao.Length);
        }

        double lat = latDeg + (latMin + latExtra) / 60.0;
        double lon = lonDeg + (lonMin + lonExtra) / 60.0;
        packet.Latitude = Math.Round(north ? lat : -lat, 6);
        packet.Longitude = Math.Round(west ? -lon : lon, 6);
        packet.Kind = AprsPacketKind.Position;

        body = body.Trim();
        packet.Comment = body.Length > 0 ? body : null;
        return true;
    }

    private static bool IsBase91(char c) => c is >= '!' and <= '{';

    /// <summary>
    /// Extra minutes from a <c>!DAO!</c>: an upper-case datum takes one more
    /// decimal digit each (thousandths of a minute), a lower-case one a
    /// base-91 byte (about ten-thousandths). A space adds nothing.
    /// </summary>
    private static (double Lat, double Lon) DaoMinutes(char datum, char lat, char lon)
    {
        if (char.IsUpper(datum))
            return (lat == ' ' ? 0 : (lat - '0') / 1000.0, lon == ' ' ? 0 : (lon - '0') / 1000.0);
        return (lat == ' ' ? 0 : Math.Round((lat - 33) * 1.1) / 10000.0,
                lon == ' ' ? 0 : Math.Round((lon - 33) * 1.1) / 10000.0);
    }

    [GeneratedRegex(@"!(?:[A-Z][0-9 ]{2}|[a-z][!-{ ]{2})!")]
    private static partial Regex Dao();

    [GeneratedRegex(@"/A=(-\d{5}|\d{6})")]
    private static partial Regex AltitudeFeet();
}
