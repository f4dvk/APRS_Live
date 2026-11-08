using AprsLive.Core.Ax25;

namespace AprsLive.Core.Aprs;

/// <summary>
/// MIC-E position decoding (APRS 1.01, chapter 10). Latitude and message bits
/// are carried in the 6-character AX.25 destination address; longitude, speed
/// and course are in the first bytes of the information field.
/// </summary>
public static class MicE
{
    public static bool TryDecode(Ax25Frame frame, AprsPacket packet)
    {
        string dest = frame.Destination.Callsign;
        if (dest.Length < 6)
            return false;

        Span<int> digits = stackalloc int[6];
        int lonOffset = 0;
        bool north = false, west = false;
        for (int i = 0; i < 6; i++)
        {
            char c = dest[i];
            int d;
            if (c is >= '0' and <= '9') d = c - '0';
            else if (c is >= 'A' and <= 'J') d = c - 'A';
            else if (c is >= 'P' and <= 'Y') d = c - 'P';
            else if (c is 'K' or 'L' or 'Z') d = 0; // ambiguity -> treat as 0
            else return false;
            digits[i] = d;

            // message/N/W/offset bits live in the "custom/standard" encoding of
            // the first three, and in char 4/5/6 for N, longitude offset, W.
            if (i == 3 && (c is >= '0' and <= '9' or 'L')) north = false;
            if (i == 3 && (c is >= 'P' and <= 'Z')) north = true;
            if (i == 4 && (c is >= 'P' and <= 'Z')) lonOffset = 100;
            if (i == 5 && (c is >= 'P' and <= 'Z')) west = true;
        }

        double latDeg = digits[0] * 10 + digits[1];
        double latMin = digits[2] * 10 + digits[3] + (digits[4] * 10 + digits[5]) / 100.0;
        double lat = latDeg + latMin / 60.0;
        if (!north) lat = -lat;

        var info = frame.Info;
        if (info.Length < 8)
            return false;

        int dOff = info[1] - 28;
        int lonDeg = dOff + lonOffset;
        if (lonDeg is >= 180 and <= 189) lonDeg -= 80;
        else if (lonDeg is >= 190 and <= 199) lonDeg -= 190;

        int m = info[2] - 28;
        if (m >= 60) m -= 60;
        int h = info[3] - 28;
        double lonMin = m + h / 100.0;
        double lon = lonDeg + lonMin / 60.0;
        if (west) lon = -lon;

        int sp = info[4] - 28;
        int dc = info[5] - 28;
        int se = info[6] - 28;
        double speed = sp * 10 + dc / 10;
        double course = (dc % 10) * 100 + se;
        if (speed >= 800) speed -= 800;
        if (course >= 400) course -= 400;

        packet.Latitude = Math.Round(lat, 6);
        packet.Longitude = Math.Round(lon, 6);
        packet.SpeedKnots = speed;
        packet.CourseDeg = course;
        packet.SymbolCode = info.Length > 7 ? (char)info[7] : '>';
        packet.SymbolTable = info.Length > 8 ? (char)info[8] : '/';
        packet.Kind = AprsPacketKind.Position;

        // Anything after the symbol table byte is free-text / telemetry.
        if (info.Length > 9)
            packet.Comment = System.Text.Encoding.Latin1.GetString(info, 9, info.Length - 9).Trim();
        return true;
    }
}
