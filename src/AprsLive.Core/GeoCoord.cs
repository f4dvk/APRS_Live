using System.Globalization;
using System.Text.RegularExpressions;

namespace AprsLive.Core;

/// <summary>
/// Parse / format a single geographic coordinate (one axis) in the three
/// common notations:
///   - decimal degrees            <c>45.12345</c>   /  <c>-1.3644</c>
///   - degrees minutes seconds    <c>45°07'24.4"N</c>  /  <c>45 7 24.4 N</c>
///   - degrees decimal minutes    <c>45°07.407'N</c>   /  <c>N 45 7.407</c>
/// The parser is lenient: <c>° ' "</c> and letters are optional, order of the
/// hemisphere letter is free, a leading <c>-</c> or <c>+</c> also works.
/// </summary>
public static partial class GeoCoord
{
    public enum Notation { Decimal, Dms, Ddm }

    public static bool TryParseLatitude(string? s, out double deg) => TryParse(s, isLat: true, out deg);
    public static bool TryParseLongitude(string? s, out double deg) => TryParse(s, isLat: false, out deg);

    private static bool TryParse(string? input, bool isLat, out double deg)
    {
        deg = 0;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        string s = input.Trim().ToUpperInvariant();

        int sign = 1;
        // hemisphere letter, anywhere
        if (s.Contains('S') || s.Contains('W')) sign = -1;
        s = s.Replace("N", " ").Replace("S", " ").Replace("E", " ").Replace("W", " ");
        // symbols -> spaces
        s = s.Replace('°', ' ').Replace('\'', ' ').Replace('"', ' ')
             .Replace('’', ' ').Replace('′', ' ').Replace('″', ' ');
        s = s.Replace(',', '.');

        if (s.Contains('-')) { sign = -1; s = s.Replace("-", " "); }
        s = s.Replace("+", " ");

        var parts = s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 1 or > 3)
            return false;

        double value = 0, scale = 1;
        foreach (var p in parts)
        {
            if (!double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                return false;
            value += v / scale;
            scale *= 60;
        }

        value *= sign;
        double limit = isLat ? 90 : 180;
        if (double.IsNaN(value) || Math.Abs(value) > limit)
            return false;

        deg = Math.Round(value, 7);
        return true;
    }

    /// <summary>Parse a combined "lat, lon" / "lat lon" pair in any notation.</summary>
    public static bool TryParsePair(string? input, out double lat, out double lon)
    {
        lat = lon = 0;
        if (string.IsNullOrWhiteSpace(input)) return false;

        // split on a comma if present, else on the boundary between the two
        // hemisphere letters, else in half by whitespace groups
        string s = input.Trim();
        string[] halves;
        int comma = s.IndexOf(',');
        if (comma > 0)
            halves = new[] { s[..comma], s[(comma + 1)..] };
        else
        {
            var m = PairSplit().Match(s);
            if (!m.Success) return false;
            halves = new[] { m.Groups[1].Value, m.Groups[2].Value };
        }

        return TryParseLatitude(halves[0], out lat) && TryParseLongitude(halves[1], out lon);
    }

    [GeneratedRegex(@"^\s*([\-+]?\s*[\d.,°'"" NSEWnsew]+?)\s+([\-+]?\s*[\d.,°'"" NSEWnsew]+)\s*$")]
    private static partial Regex PairSplit();

    // ---- formatting ----

    public static string Format(double deg, Notation notation, bool isLat) => notation switch
    {
        Notation.Dms => ToDms(deg, isLat),
        Notation.Ddm => ToDdm(deg, isLat),
        _ => deg.ToString("0.######", CultureInfo.InvariantCulture)
    };

    public static string ToDms(double deg, bool isLat)
    {
        char hemi = Hemisphere(deg, isLat);
        double a = Math.Abs(deg);
        int d = (int)a;
        double minF = (a - d) * 60;
        int m = (int)minF;
        double sec = (minF - m) * 60;
        return $"{d}°{m:00}'{sec:00.0}\"{hemi}";
    }

    public static string ToDdm(double deg, bool isLat)
    {
        char hemi = Hemisphere(deg, isLat);
        double a = Math.Abs(deg);
        int d = (int)a;
        double min = (a - d) * 60;
        return $"{d}°{min:00.000}'{hemi}";
    }

    /// <summary>Compact degrees + decimal minutes, no ° / ' symbols (e.g. <c>50.12.15N</c> for 50°12.15'N) — used in short status texts sent over the air.</summary>
    public static string ToCompactDdm(double deg, bool isLat)
    {
        char hemi = Hemisphere(deg, isLat);
        double a = Math.Abs(deg);
        int d = (int)a;
        double min = (a - d) * 60;
        return $"{d}.{min:00.00}{hemi}";
    }

    private static char Hemisphere(double deg, bool isLat) =>
        isLat ? (deg < 0 ? 'S' : 'N') : (deg < 0 ? 'W' : 'E');
}
