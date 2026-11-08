namespace AprsLive.Core;

/// <summary>Small great-circle helpers (WGS-84 sphere approximation).</summary>
public static class GeoMath
{
    private const double EarthRadiusM = 6_371_008.8;

    /// <summary>
    /// Point reached from (lat,lon) after travelling <paramref name="distanceM"/>
    /// metres along the initial bearing <paramref name="bearingDeg"/> (0 = north,
    /// 90 = east). Returns degrees.
    /// </summary>
    public static (double Lat, double Lon) Destination(double lat, double lon, double bearingDeg, double distanceM)
    {
        double d = distanceM / EarthRadiusM;
        double brng = DegToRad(bearingDeg);
        double lat1 = DegToRad(lat);
        double lon1 = DegToRad(lon);

        double lat2 = Math.Asin(Math.Sin(lat1) * Math.Cos(d) +
                                Math.Cos(lat1) * Math.Sin(d) * Math.Cos(brng));
        double lon2 = lon1 + Math.Atan2(Math.Sin(brng) * Math.Sin(d) * Math.Cos(lat1),
                                        Math.Cos(d) - Math.Sin(lat1) * Math.Sin(lat2));

        return (RadToDeg(lat2), NormalizeLon(RadToDeg(lon2)));
    }

    public static double DistanceM(double lat1, double lon1, double lat2, double lon2)
    {
        double p1 = DegToRad(lat1), p2 = DegToRad(lat2);
        double dp = DegToRad(lat2 - lat1), dl = DegToRad(lon2 - lon1);
        double a = Math.Sin(dp / 2) * Math.Sin(dp / 2) +
                   Math.Cos(p1) * Math.Cos(p2) * Math.Sin(dl / 2) * Math.Sin(dl / 2);
        return EarthRadiusM * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    /// <summary>Initial great-circle bearing from (lat1,lon1) to (lat2,lon2), in degrees (0 = north, 90 = east, range [0,360)).</summary>
    public static double BearingDeg(double lat1, double lon1, double lat2, double lon2)
    {
        double p1 = DegToRad(lat1), p2 = DegToRad(lat2);
        double dl = DegToRad(lon2 - lon1);
        double y = Math.Sin(dl) * Math.Cos(p2);
        double x = Math.Cos(p1) * Math.Sin(p2) - Math.Sin(p1) * Math.Cos(p2) * Math.Cos(dl);
        return (RadToDeg(Math.Atan2(y, x)) + 360) % 360;
    }

    private static double DegToRad(double d) => d * Math.PI / 180.0;
    private static double RadToDeg(double r) => r * 180.0 / Math.PI;
    private static double NormalizeLon(double lon) => ((lon + 540) % 360) - 180;
}
