using AprsLive.Core.Ax25;
using AprsLive.Core.Tnc;

namespace AprsLive.Core.Tests;

public class AprsIsPasscodeTests
{
    [Theory]
    [InlineData("N0CALL", 13023)] // the standard published reference vector
    [InlineData("F4DVK", 14976)]
    [InlineData("F4DVK-9", 14976)] // SSID is ignored, same as the base callsign
    public void Matches_the_standard_algorithm(string callsign, int expected) =>
        Assert.Equal(expected, AprsIsPasscode.Compute(callsign));
}

public class AprsIsServersTests
{
    [Fact]
    public void Lists_the_documented_rotation_hostnames()
    {
        Assert.Contains("rotate.aprs2.net", AprsIsServers.Known);
        Assert.True(AprsIsServers.Known.Count >= 4);
    }
}

public class AprsIsTnc2Tests
{
    [Fact]
    public void A_tnc2_line_round_trips_through_the_existing_Ax25_builder_and_parser()
    {
        // This is exactly what AprsIsClient does with each line it reads:
        // split "SRC>DST,PATH:info" into strings and hand them to the
        // existing Ax25Builder — no bespoke AX.25 encoding needed for APRS-IS.
        const string line = "F4ABC-7>APLIVE,WIDE1-1,qAR,F6DDS-3:!4903.50N/00230.00E>Test APRS-IS";

        int gt = line.IndexOf('>');
        int colon = line.IndexOf(':', gt);
        string source = line[..gt];
        var rest = line[(gt + 1)..colon].Split(',');
        string destination = rest[0];
        var path = rest[1..];
        string info = line[(colon + 1)..];

        var frame = Ax25Builder.BuildUi(source, destination, path, info);

        Assert.True(Ax25Frame.TryParse(frame, out var parsed));
        Assert.Equal("F4ABC", parsed!.Source.Base);
        Assert.Equal(7, parsed.Source.Ssid);
        Assert.Equal("APLIVE", parsed.Destination.Callsign);
        Assert.Equal(3, parsed.Path.Count);
        Assert.Equal("F6DDS", parsed.Path[2].Base);
        Assert.Equal(3, parsed.Path[2].Ssid);
        Assert.Equal(info, parsed.InfoText);
    }
}
