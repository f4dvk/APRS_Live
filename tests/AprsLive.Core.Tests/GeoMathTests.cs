using AprsLive.Core;

namespace AprsLive.Core.Tests;

public class GeoMathTests
{
    [Fact]
    public void Bearing_due_east_is_90_degrees()
    {
        double b = GeoMath.BearingDeg(48.0, 2.0, 48.0, 3.0);
        Assert.Equal(90, b, 0);
    }

    [Fact]
    public void Bearing_due_north_is_0_degrees()
    {
        double b = GeoMath.BearingDeg(48.0, 2.0, 49.0, 2.0);
        Assert.Equal(0, b, 0);
    }

    [Fact]
    public void Bearing_due_south_is_180_degrees()
    {
        double b = GeoMath.BearingDeg(48.0, 2.0, 47.0, 2.0);
        Assert.Equal(180, b, 0);
    }

    [Fact]
    public void Bearing_stays_within_0_to_360()
    {
        double b = GeoMath.BearingDeg(48.0, 2.0, 48.5, 1.0);
        Assert.InRange(b, 0, 360);
    }
}
