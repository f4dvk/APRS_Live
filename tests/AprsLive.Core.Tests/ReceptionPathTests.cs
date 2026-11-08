using AprsLive.Core.Aprs;
using AprsLive.Core.Ax25;
using AprsLive.Core.Stations;

namespace AprsLive.Core.Tests;

public class ReceptionPathTests
{
    private static readonly (double Lat, double Lon) Me = (48.85, 2.35);

    private readonly StationRegistry _registry = new();

    private void Hear(string source, string info, PacketOrigin origin, params string[] path)
    {
        var bytes = Ax25Builder.BuildUi(source, "APRS", path, info);
        Assert.True(Ax25Frame.TryParse(bytes, out var f));
        _registry.Apply(AprsParser.Parse(f!), origin);
    }

    private ReceptionPath.Result? PathOf(string callsign) =>
        ReceptionPath.Compute(_registry.Stations.Single(s => s.Callsign == callsign),
            call => _registry.Stations.FirstOrDefault(s => string.Equals(s.Callsign, call, StringComparison.OrdinalIgnoreCase)),
            Me);

    [Fact]
    public void A_direct_packet_draws_a_single_line_to_our_station()
    {
        Hear("F6KTN-9", "!4900.00N/00300.00E>x", PacketOrigin.Rf, "WIDE1-1");

        var r = PathOf("F6KTN-9")!;
        Assert.False(r.ViaInternet);
        Assert.Equal([(49.0, 3.0), Me], r.Points);
    }

    [Fact]
    public void Goes_through_each_repeating_station_the_alias_it_used_being_covered()
    {
        Hear("F4DIGI-1", "!4930.00N/00230.00E#Digi", PacketOrigin.Rf);
        Hear("F6KTN-9", "!4900.00N/00300.00E>x", PacketOrigin.Rf, "F4DIGI-1*", "WIDE1*", "WIDE2-1");

        var r = PathOf("F6KTN-9")!;
        Assert.Equal([(49.0, 3.0), (49.5, 2.5), Me], r.Points);
    }

    [Fact]
    public void Follows_a_traced_multi_hop_path()
    {
        Hear("F4AAA-1", "!4930.00N/00230.00E#A", PacketOrigin.Rf);
        Hear("F4BBB-1", "!4915.00N/00245.00E#B", PacketOrigin.Rf);
        Hear("F6KTN-9", "!4900.00N/00300.00E>x", PacketOrigin.Rf, "F4AAA-1*", "F4BBB-1*", "WIDE2*");

        Assert.Equal([(49.0, 3.0), (49.5, 2.5), (49.25, 2.75), Me], PathOf("F6KTN-9")!.Points);
    }

    [Fact]
    public void No_line_when_a_relay_is_not_on_the_map()
    {
        Hear("F4DIGI-1", "!4930.00N/00230.00E#Digi", PacketOrigin.Rf);
        Hear("F6KTN-9", "!4900.00N/00300.00E>x", PacketOrigin.Rf, "F4DIGI-1*", "WIDE1*", "F4UNK-2*", "WIDE2*");

        Assert.Null(PathOf("F6KTN-9"));
    }

    [Fact]
    public void No_line_when_a_digi_repeated_without_signing()
    {
        Hear("F6KTN-9", "!4900.00N/00300.00E>x", PacketOrigin.Rf, "WIDE2*");

        Assert.Null(PathOf("F6KTN-9"));
    }

    [Fact]
    public void No_line_for_an_internet_packet_whose_igate_is_not_on_the_map()
    {
        Hear("F6KTN-9", "!4900.00N/00300.00E>x", PacketOrigin.Internet, "WIDE1-1", "qAR", "F4IGT");

        Assert.Null(PathOf("F6KTN-9"));
    }

    [Fact]
    public void No_line_for_a_station_connected_straight_to_aprs_is()
    {
        Hear("F6KTN-9", "!4900.00N/00300.00E>x", PacketOrigin.Internet, "TCPIP*", "qAC", "T2FRA");

        Assert.Null(PathOf("F6KTN-9"));
    }

    [Fact]
    public void Internet_only_packets_end_at_the_igate_not_at_our_station()
    {
        Hear("F4IGT", "!4915.00N/00200.00E&IGate", PacketOrigin.Internet, "TCPIP*", "qAC", "T2FRA");
        Hear("F6KTN-9", "!4900.00N/00300.00E>x", PacketOrigin.Internet, "WIDE1-1", "qAR", "F4IGT");

        var r = PathOf("F6KTN-9")!;
        Assert.True(r.ViaInternet);
        Assert.Equal([(49.0, 3.0), (49.25, 2.0)], r.Points);
    }

    [Fact]
    public void The_rf_path_wins_over_the_aprs_is_copy_that_follows_it()
    {
        Hear("F6KTN-9", "!4900.00N/00300.00E>x", PacketOrigin.Rf, "WIDE1-1");
        Hear("F6KTN-9", "!4900.00N/00300.00E>x", PacketOrigin.Internet, "WIDE1-1", "qAR", "F4IGT");

        var r = PathOf("F6KTN-9")!;
        Assert.False(r.ViaInternet);
        Assert.Equal([(49.0, 3.0), Me], r.Points);
    }

    [Fact]
    public void Nothing_to_draw_for_our_own_transmissions()
    {
        Hear("F4DVK-9", "!4851.00N/00221.00E>Moi", PacketOrigin.Local, "WIDE1-1");

        Assert.Null(PathOf("F4DVK-9"));
    }
}
