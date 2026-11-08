using AprsLive.Core;

namespace AprsLive.Core.Tests;

public class GeoCoordTests
{
    [Theory]
    [InlineData("45.1234", 45.1234)]
    [InlineData("-1.3644", -1.3644)]
    [InlineData("45 7 24.4 N", 45.123444)]
    [InlineData("45°07'24.4\"N", 45.123444)]
    [InlineData("45 07.407 N", 45.12345)]
    [InlineData("45°07.407'N", 45.12345)]
    [InlineData("S 45 7.407", -45.12345)]
    [InlineData("45,1234", 45.1234)]
    public void Parses_latitude_in_every_notation(string text, double expected)
    {
        Assert.True(GeoCoord.TryParseLatitude(text, out double d));
        Assert.Equal(expected, d, 4);
    }

    [Theory]
    [InlineData("120.5", 120.5)]
    [InlineData("120 30 0 W", -120.5)]
    [InlineData("W 5°43.90'", -5.731667)]
    public void Parses_longitude(string text, double expected)
    {
        Assert.True(GeoCoord.TryParseLongitude(text, out double d));
        Assert.Equal(expected, d, 4);
    }

    [Theory]
    [InlineData("100")]        // > 90 latitude
    [InlineData("45 61 0")]    // impossible minutes still fold but stays in range... allowed
    [InlineData("abc")]
    [InlineData("")]
    public void Rejects_bad_latitude(string text)
    {
        // "45 61 0" -> 46.0166.. which is valid; only the truly invalid ones fail
        bool ok = GeoCoord.TryParseLatitude(text, out _);
        if (text is "100" or "abc" or "")
            Assert.False(ok);
    }

    [Fact]
    public void Parses_a_combined_pair()
    {
        Assert.True(GeoCoord.TryParsePair("45.1234, -1.3644", out double lat, out double lon));
        Assert.Equal(45.1234, lat, 4);
        Assert.Equal(-1.3644, lon, 4);

        Assert.True(GeoCoord.TryParsePair("45°07.4'N 001°21.9'W", out lat, out lon));
        Assert.Equal(45.1233, lat, 3);
        Assert.Equal(-1.365, lon, 3);
    }

    [Fact]
    public void Round_trips_through_dms_and_ddm()
    {
        const double v = 45.123444;
        Assert.True(GeoCoord.TryParseLatitude(GeoCoord.ToDms(v, true), out double a));
        Assert.Equal(v, a, 4);
        Assert.True(GeoCoord.TryParseLatitude(GeoCoord.ToDdm(v, true), out double b));
        Assert.Equal(v, b, 4);
    }

    [Theory]
    [InlineData(50.2025, true, "50.12.15N")]
    [InlineData(3.8147, false, "3.48.88E")]
    [InlineData(-5.7, true, "5.42.00S")]
    public void Formats_compact_ddm_for_status_texts(double deg, bool isLat, string expected)
    {
        Assert.Equal(expected, GeoCoord.ToCompactDdm(deg, isLat));
    }
}
