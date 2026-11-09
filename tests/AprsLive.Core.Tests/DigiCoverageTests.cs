using AprsLive.Core.Aprs;
using AprsLive.Core.Ax25;
using AprsLive.Core.Stations;

namespace AprsLive.Core.Tests;

public class DigiCoverageTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly (double Lat, double Lon) Digi = (49.5, 3.5);

    private readonly DigiCoverage _coverage = new();

    public DigiCoverageTests() => _coverage.Watch("F1ZXX-3");

    private bool Hear(string source, string info, DateTimeOffset at, params string[] path) =>
        _coverage.Record(Packet(source, info, at, path), PacketOrigin.Rf);

    private static AprsPacket Packet(string source, string info, DateTimeOffset at, params string[] path)
    {
        var bytes = Ax25Builder.BuildUi(source, "APRS", path, info);
        Assert.True(Ax25Frame.TryParse(bytes, out var f));
        return AprsParser.Parse(f!, at);
    }

    private static double Km(double lat, double lon) => GeoMath.DistanceM(Digi.Lat, Digi.Lon, lat, lon) / 1000.0;

    private CoverageSummary Summary(DateTimeOffset now) => _coverage.Summarize("F1ZXX-3", Digi, now)!;

    [Fact]
    public void Averages_the_distance_to_the_stations_it_repeated_first_and_keeps_the_farthest()
    {
        Assert.True(Hear("F4AAA-9", "!4900.00N/00300.00E>a", T0, "F1ZXX-3*", "WIDE1*", "WIDE2-1"));
        Assert.True(Hear("F4BBB", "!4930.00N/00400.00E-b", T0.AddMinutes(5), "F1ZXX-3*", "WIDE2-1"));

        var s = Summary(T0.AddMinutes(10));
        Assert.Equal(2, s.Frames);
        Assert.Equal(2, s.Stations);
        Assert.Equal((Km(49, 3) + Km(49.5, 4)) / 2, s.AverageKm!.Value, 6);
        Assert.Equal(Km(49, 3), s.MaxKm!.Value, 6);
        Assert.Equal("F4AAA-9", s.FarthestStation);
        Assert.Equal(T0, s.FarthestAt);
        Assert.Equal(T0.AddMinutes(5), s.LastAt);
    }

    [Fact]
    public void Each_frame_counts_so_a_frequent_sender_counts_several_times()
    {
        Hear("F4AAA-9", "!4900.00N/00300.00E>a", T0, "F1ZXX-3*", "WIDE2-1");
        Hear("F4AAA-9", "!4910.00N/00310.00E>a", T0.AddMinutes(1), "F1ZXX-3*", "WIDE2-1");

        var s = Summary(T0.AddMinutes(2));
        Assert.Equal(2, s.Frames);
        Assert.Equal(1, s.Stations);
    }

    [Fact]
    public void Not_repeated_yet_means_heard_direct_not_through_the_digi()
    {
        Assert.False(Hear("F4AAA-9", "!4900.00N/00300.00E>a", T0, "F1ZXX-3", "WIDE2-1"));
        Assert.Equal(0, Summary(T0).Frames);
    }

    [Fact]
    public void Only_the_first_hop_counts()
    {
        Assert.False(Hear("F4AAA-9", "!4900.00N/00300.00E>a", T0, "F4DIGI-1*", "F1ZXX-3*", "WIDE2*"));
        Assert.Equal(0, Summary(T0).Frames);
    }

    [Fact]
    public void A_later_hop_repeated_on_rf_still_credits_the_first()
    {
        Assert.True(Hear("F4AAA-9", "!4900.00N/00300.00E>a", T0, "F1ZXX-3*", "F4DIGI-1*", "WIDE2*"));
    }

    [Fact]
    public void On_aprs_is_only_the_last_repeated_hop_is_marked()
    {
        Assert.True(Hear("F4AAA-9", "!4900.00N/00300.00E>a", T0, "F1ZXX-3", "F4DIGI-1", "WIDE2*", "qAR", "F4IGT"));
    }

    [Fact]
    public void Heard_direct_by_an_igate_is_not_repeated()
    {
        Assert.False(Hear("F4AAA-9", "!4900.00N/00300.00E>a", T0, "F1ZXX-3", "WIDE2-1", "qAR", "F4IGT"));
    }

    [Theory]
    [InlineData("WIDE1*", "WIDE2-1")]
    [InlineData("TCPIP*", "qAC", "T2FRANCE")]
    [InlineData("RELAY*")]
    public void No_relay_when_the_first_hop_is_not_a_callsign(params string[] path)
    {
        var bytes = Ax25Builder.BuildUi("F4AAA-9", "APRS", path, "!4900.00N/00300.00E>a");
        Assert.True(Ax25Frame.TryParse(bytes, out var f));
        Assert.Null(DigiCoverage.FirstRepeater(f!));
    }

    [Fact]
    public void Copies_of_one_transmission_count_once()
    {
        Assert.True(Hear("F4AAA-9", "!4900.00N/00300.00E>a", T0, "F1ZXX-3*", "WIDE2-1"));
        Assert.False(Hear("F4AAA-9", "!4900.00N/00300.00E>a", T0.AddSeconds(2), "F1ZXX-3*", "F4DIGI-1*", "WIDE2*"));
        Assert.False(Hear("F4AAA-9", "!4900.00N/00300.00E>a", T0.AddSeconds(3), "F1ZXX-3", "F4DIGI-1", "WIDE2*", "qAR", "F4IGT"));
        // the same beacon again ten minutes later is a new transmission
        Assert.True(Hear("F4AAA-9", "!4900.00N/00300.00E>a", T0.AddMinutes(10), "F1ZXX-3*", "WIDE2-1"));

        Assert.Equal(2, Summary(T0.AddMinutes(11)).Frames);
    }

    [Fact]
    public void The_farthest_time_is_the_latest_frame_at_that_distance()
    {
        Hear("F4AAA-9", "!4900.00N/00300.00E>a", T0, "F1ZXX-3*", "WIDE2-1");
        Hear("F4BBB", "!4930.00N/00400.00E-b", T0.AddMinutes(5), "F1ZXX-3*", "WIDE2-1");
        // the same beacon from the same spot, later: still the farthest, now heard at the new time
        Hear("F4AAA-9", "!4900.00N/00300.00E>a", T0.AddMinutes(10), "F1ZXX-3*", "WIDE2-1");
        // nearer: changes nothing
        Hear("F4AAA-9", "!4920.00N/00320.00E>a", T0.AddMinutes(20), "F1ZXX-3*", "WIDE2-1");

        var s = Summary(T0.AddMinutes(30));
        Assert.Equal("F4AAA-9", s.FarthestStation);
        Assert.Equal(T0.AddMinutes(10), s.FarthestAt);
        Assert.Equal(Km(49, 3), s.MaxKm!.Value, 6);
    }

    [Fact]
    public void Lists_the_stations_heard_direct_latest_first_with_their_last_position()
    {
        Hear("F4AAA-9", "!4900.00N/00300.00E>a", T0, "F1ZXX-3*", "WIDE2-1");
        Hear("F4BBB", "!4930.00N/00400.00E-b", T0.AddMinutes(5), "F1ZXX-3*", "WIDE2-1");
        Hear("F4AAA-9", "!4920.00N/00320.00E>a", T0.AddMinutes(10), "F1ZXX-3*", "WIDE2-1");
        // older than 24 h by the time we look: gone
        Hear("F4CCC", "!4940.00N/00340.00E-c", T0.AddHours(-1), "F1ZXX-3*", "WIDE2-1");
        // heard direct by another digi: not in this list
        Hear("F4DDD", "!4940.00N/00340.00E-d", T0, "F5XYZ-2*", "WIDE2-1");

        var list = _coverage.HeardDirect("F1ZXX-3", Digi, T0.AddHours(23).AddMinutes(30));
        Assert.Equal(["F4AAA-9", "F4BBB"], list.Select(d => d.Station));
        Assert.Equal(T0.AddMinutes(10), list[0].LastAt);
        Assert.Equal(2, list[0].Frames);
        Assert.Equal(Km(49 + 20 / 60.0, 3 + 20 / 60.0), list[0].DistanceKm!.Value, 3);
        Assert.Equal(1, list[1].Frames);

        // not watched: nothing recorded for it
        Assert.Empty(_coverage.HeardDirect("F5XYZ-2", Digi, T0));
    }

    [Fact]
    public void Heard_direct_without_the_relay_position_has_no_distance()
    {
        var coverage = new DigiCoverage();
        coverage.Watch("F1ZXX-3");
        coverage.Record(Packet("F4AAA-9", "!4900.00N/00300.00E>a", T0, "F1ZXX-3*", "WIDE2-1"), PacketOrigin.Rf);

        var only = Assert.Single(coverage.HeardDirect("F1ZXX-3", null, T0));
        Assert.Null(only.DistanceKm);
        Assert.Equal(T0, only.LastAt);
    }

    [Fact]
    public void Our_own_station_counts_what_our_tncs_hear_direct()
    {
        var coverage = new DigiCoverage();
        coverage.SetOwnStation("F4DVK-10");
        const string beacon = "!4900.00N/00300.00E>a";

        Assert.True(coverage.Record(Packet("F4AAA-9", beacon, T0, "WIDE1-1", "WIDE2-1"), PacketOrigin.Rf));
        // the same transmission on the other TNC counts once
        Assert.False(coverage.Record(Packet("F4AAA-9", beacon, T0.AddSeconds(1), "WIDE1-1", "WIDE2-1"), PacketOrigin.Rf));
        // through a digi: not heard direct
        Assert.False(coverage.Record(Packet("F4BBB", "!4930.00N/00400.00E-b", T0.AddMinutes(1), "F1ZXX-3*", "WIDE2-1"), PacketOrigin.Rf));
        // APRS-IS, even with nothing repeated in the path: not heard by our TNCs
        Assert.False(coverage.Record(Packet("F4CCC", "!4940.00N/00340.00E-c", T0.AddMinutes(2), "WIDE2-1", "qAR", "F4IGT"), PacketOrigin.Internet));
        // our own beacon heard back
        Assert.False(coverage.Record(Packet("F4DVK-10", "!4930.00N/00330.00E-d", T0.AddMinutes(3), "WIDE2-1"), PacketOrigin.Rf));

        var s = coverage.Summarize("F4DVK-10", Digi, T0.AddMinutes(5))!;
        Assert.Equal(1, s.Frames);
        Assert.Equal(Km(49, 3), s.MaxKm!.Value, 6);
        Assert.Equal("F4AAA-9", Assert.Single(coverage.HeardDirect("F4DVK-10", Digi, T0.AddMinutes(5))).Station);
    }

    [Fact]
    public void Our_digi_repeat_echoed_by_aprs_is_is_the_same_transmission()
    {
        var coverage = new DigiCoverage();
        coverage.SetOwnStation("F4DVK-10");
        const string beacon = "!4900.00N/00300.00E>a";

        Assert.True(coverage.Record(Packet("F4AAA-9", beacon, T0, "WIDE1-1", "WIDE2-1"), PacketOrigin.Rf));
        Assert.False(coverage.Record(Packet("F4AAA-9", beacon, T0.AddSeconds(4), "F4DVK-10*", "WIDE2-1", "qAR", "F4IGT"), PacketOrigin.Internet));
        Assert.Equal(1, coverage.Summarize("F4DVK-10", Digi, T0.AddMinutes(1))!.Frames);
    }

    [Fact]
    public void Older_than_24_hours_drops_out()
    {
        Hear("F4AAA-9", "!4900.00N/00300.00E>a", T0, "F1ZXX-3*", "WIDE2-1");
        Hear("F4BBB", "!4930.00N/00400.00E-b", T0.AddHours(2), "F1ZXX-3*", "WIDE2-1");

        var s = Summary(T0.AddHours(24).AddMinutes(1));
        Assert.Equal(1, s.Frames);
        Assert.Equal("F4BBB", s.FarthestStation);

        Assert.True(_coverage.Prune(T0.AddHours(24).AddMinutes(1)));
        Assert.Single(_coverage.Logs.Single().Samples);
    }

    [Fact]
    public void Objects_and_non_position_frames_do_not_count()
    {
        // the object's position is not where its sender is
        Assert.False(Hear("F4AAA-9", ";EVENT    *081200z4800.00N/00200.00E-x", T0, "F1ZXX-3*", "WIDE2-1"));
        Assert.False(Hear("F4AAA-9", ">en route", T0, "F1ZXX-3*", "WIDE2-1"));
        Assert.Equal(0, Summary(T0).Frames);
    }

    [Fact]
    public void Stations_not_watched_are_only_listed_as_relays_seen()
    {
        Assert.False(Hear("F4AAA-9", "!4900.00N/00300.00E>a", T0, "F4DIGI-1*", "WIDE2-1"));
        Assert.Contains("F4DIGI-1", _coverage.RelaysSeen);
        Assert.Null(_coverage.Summarize("F4DIGI-1", Digi, T0));
    }

    [Fact]
    public void Without_the_relay_position_only_frames_are_counted()
    {
        Hear("F4AAA-9", "!4900.00N/00300.00E>a", T0, "F1ZXX-3*", "WIDE2-1");

        var s = _coverage.Summarize("F1ZXX-3", null, T0)!;
        Assert.Equal(1, s.Frames);
        Assert.False(s.RelayPositionKnown);
        Assert.Null(s.AverageKm);
    }

    [Fact]
    public void Remembers_the_relay_position_from_its_own_beacon()
    {
        Hear("F1ZXX-3", "!4930.00N/00330.00E#Digi", T0);
        Hear("F4AAA-9", "!4900.00N/00300.00E>a", T0.AddMinutes(1), "F1ZXX-3*", "WIDE2-1");

        var s = _coverage.Summarize("F1ZXX-3", null, T0.AddMinutes(2))!;
        Assert.Equal(Km(49, 3), s.MaxKm!.Value, 6);
    }

    [Fact]
    public void Saved_samples_come_back_for_watched_stations_only()
    {
        Hear("F1ZXX-3", "!4930.00N/00330.00E#Digi", T0);
        Hear("F4AAA-9", "!4900.00N/00300.00E>a", T0.AddMinutes(1), "F1ZXX-3*", "WIDE2-1");
        string path = Path.Combine(Path.GetTempPath(), $"coverage-{Guid.NewGuid():N}.json");
        try
        {
            CoverageStore.Save(_coverage.Logs, path);

            var restored = new DigiCoverage();
            restored.Watch("F1ZXX-3");
            restored.Restore(CoverageStore.Load(path), T0.AddHours(1));
            var s = restored.Summarize("F1ZXX-3", null, T0.AddHours(1))!;
            Assert.Equal(1, s.Frames);
            Assert.Equal(Km(49, 3), s.MaxKm!.Value, 6);

            var other = new DigiCoverage();
            other.Watch("F4DIGI-1");
            other.Restore(CoverageStore.Load(path), T0.AddHours(1));
            Assert.Empty(other.Logs.Single().Samples);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // Digis placed on the map: F1CNH-3 at 49.0 N 3.0 E, F5ZBC-3 at Digi.
    private static (double Lat, double Lon)? Digis(string call) => call switch
    {
        "F1CNH-3" => (49.0, 3.0),
        "F5ZBC-3" => Digi,
        _ => null,
    };

    private static DigiCoverage LinksCoverage(params string[] watched)
    {
        var coverage = new DigiCoverage(Digis) { IncludeDigiLinks = true };
        foreach (string call in watched)
            coverage.Watch(call);
        return coverage;
    }

    [Theory]
    [InlineData(new[] { "F1CNH-3*", "F5ZBC-3*", "WIDE2*" }, new[] { "F1CNH-3", "F5ZBC-3" })]
    // a used alias right after a callsign: that digi used it up, one hop
    [InlineData(new[] { "F1CNH-3*", "WIDE1*", "F5ZBC-3*", "WIDE2-1" }, new[] { "F1CNH-3", "F5ZBC-3" })]
    // the same, whichever hops carry a star (APRS-IS often marks only the last one)
    [InlineData(new[] { "F1CNH-3*", "WIDE1*", "F5ZBC-3*" }, new[] { "F1CNH-3", "F5ZBC-3" })]
    [InlineData(new[] { "F1CNH-3", "WIDE1*", "F5ZBC-3*" }, new[] { "F1CNH-3", "F5ZBC-3" })]
    [InlineData(new[] { "F1CNH-3", "WIDE1", "F5ZBC-3*" }, new[] { "F1CNH-3", "F5ZBC-3" })]
    // repeated by a digi that didn't sign: unknown, on either side of it
    [InlineData(new[] { "F1CNH-3*", "WIDE1*", "WIDE2*" }, new[] { "F1CNH-3", null })]
    [InlineData(new[] { "WIDE1*", "F5ZBC-3*" }, new[] { null, "F5ZBC-3" })]
    // APRS-IS: only the last repeated hop is marked, the IGate after the q construct is not a digi
    [InlineData(new[] { "F1CNH-3", "F5ZBC-3*", "qAR", "F4IGT" }, new[] { "F1CNH-3", "F5ZBC-3" })]
    [InlineData(new[] { "F1CNH-3", "WIDE2-1" }, new string[0])]
    public void The_relay_chain_lists_who_repeated_the_frame_in_order(string[] path, string?[] expected)
    {
        var bytes = Ax25Builder.BuildUi("F4AAA-9", "APRS", path, ">test");
        Assert.True(Ax25Frame.TryParse(bytes, out var f));
        Assert.Equal(expected, DigiCoverage.RelayChain(f!));
    }

    [Fact]
    public void A_digi_beaconing_through_its_own_digipeater_is_heard_direct()
    {
        var coverage = new DigiCoverage(Digis);   // without the links between digis
        coverage.SetOwnStation("F4DVK-10");
        coverage.Watch("F5ZBC-3");
        const string beacon = "!4900.00N/00300.00E#";

        Assert.Equal(new string[0], DigiCoverage.RelayChain(Packet("F1CNH-3", beacon, T0, "F1CNH-3*", "WIDE2-1").Frame));
        // our TNCs heard F1CNH-3 itself: a position heard direct, not a link between digis
        Assert.True(coverage.Record(Packet("F1CNH-3", beacon, T0, "F1CNH-3*", "WIDE2-1"), PacketOrigin.Rf));
        var heard = Assert.Single(coverage.HeardDirect("F4DVK-10", Digi, T0.AddMinutes(1)));
        Assert.Equal("F1CNH-3", heard.Station);
        Assert.False(heard.LastRelayed);

        // repeated by F5ZBC-3: it heard F1CNH-3 send its own position
        Assert.True(coverage.Record(Packet("F1CNH-3", beacon, T0.AddMinutes(10), "F1CNH-3*", "WIDE1*", "F5ZBC-3*"), PacketOrigin.Rf));
        heard = Assert.Single(coverage.HeardDirect("F5ZBC-3", Digi, T0.AddMinutes(11)));
        Assert.Equal("F1CNH-3", heard.Station);
        Assert.False(heard.LastRelayed);
    }

    [Fact]
    public void The_digi_mark_is_for_the_last_frame_heard()
    {
        var coverage = LinksCoverage();
        coverage.SetOwnStation("F4DVK-10");
        // F1CNH-3 relays F4AAA-9, then sends its own beacon, heard direct
        coverage.Record(Packet("F4AAA-9", ">a", T0, "F1CNH-3*", "WIDE2-1"), PacketOrigin.Rf);
        Assert.True(Assert.Single(coverage.HeardDirect("F4DVK-10", Digi, T0.AddMinutes(1))).LastRelayed);

        coverage.Record(Packet("F1CNH-3", "!4900.00N/00300.00E#", T0.AddMinutes(2), "WIDE2-1"), PacketOrigin.Rf);
        var heard = Assert.Single(coverage.HeardDirect("F4DVK-10", Digi, T0.AddMinutes(3)));
        Assert.False(heard.LastRelayed);
        Assert.Equal(2, heard.Frames);
        Assert.StartsWith("F1CNH-3>APRS,WIDE2-1:", heard.LastFrame);
    }

    [Fact]
    public void Digi_marks_only_a_digi_heard_relaying_and_is_worked_out_again_on_each_frame()
    {
        var coverage = LinksCoverage("F1CNH-3", "F8KCS-2");
        (double, double) cnh = (49.0, 3.0);
        const string zbcBeacon = "!4930.00N/00330.00E#";

        // F5ZBC-3>APRS,F1CNH-3,F8KCS-2,WIDE2*: F1CNH-3 heard F5ZBC-3 send its
        // own position, F8KCS-2 heard F1CNH-3 relaying it
        coverage.Record(Packet("F5ZBC-3", zbcBeacon, T0, "F1CNH-3", "F8KCS-2", "WIDE2*"), PacketOrigin.Rf);
        var byCnh = Assert.Single(coverage.HeardDirect("F1CNH-3", cnh, T0.AddMinutes(1)));
        Assert.Equal("F5ZBC-3", byCnh.Station);
        Assert.False(byCnh.LastRelayed);
        var byKcs = Assert.Single(coverage.HeardDirect("F8KCS-2", Digi, T0.AddMinutes(1)));
        Assert.Equal("F1CNH-3", byKcs.Station);
        Assert.True(byKcs.LastRelayed);

        // then F1CNH-3 hears F5ZBC-3 relaying someone else: digi, for this frame
        coverage.Record(Packet("F4AAA-9", ">a", T0.AddMinutes(5), "F5ZBC-3*", "F1CNH-3*", "WIDE2*"), PacketOrigin.Rf);
        Assert.True(Assert.Single(coverage.HeardDirect("F1CNH-3", cnh, T0.AddMinutes(6))).LastRelayed);

        // and F5ZBC-3's own beacon again: no longer digi
        coverage.Record(Packet("F5ZBC-3", zbcBeacon, T0.AddMinutes(10), "F1CNH-3*", "WIDE2-1"), PacketOrigin.Rf);
        Assert.False(Assert.Single(coverage.HeardDirect("F1CNH-3", cnh, T0.AddMinutes(11))).LastRelayed);
    }

    [Fact]
    public void A_digi_hears_the_digi_before_it_in_the_path()
    {
        var coverage = LinksCoverage("F5ZBC-3");
        // a status: no position in it, the digis' positions are what counts
        Assert.True(coverage.Record(Packet("F4AAA-9", ">en route", T0, "F1CNH-3*", "F5ZBC-3*", "WIDE2*"), PacketOrigin.Rf));
        // the APRS-IS copy is the same transmission
        Assert.False(coverage.Record(Packet("F4AAA-9", ">en route", T0.AddSeconds(3), "F1CNH-3", "F5ZBC-3*", "qAR", "F4IGT"), PacketOrigin.Internet));

        var s = coverage.Summarize("F5ZBC-3", Digi, T0.AddMinutes(1))!;
        Assert.Equal(1, s.Frames);
        Assert.Equal("F1CNH-3", s.FarthestStation);
        Assert.Equal(Km(49, 3), s.MaxKm!.Value, 6);
        var heard = Assert.Single(coverage.HeardDirect("F5ZBC-3", Digi, T0.AddMinutes(1)));
        Assert.True(heard.LastRelayed);
    }

    [Fact]
    public void Links_between_digis_are_kept_but_only_counted_with_the_option()
    {
        var coverage = LinksCoverage("F5ZBC-3");
        coverage.IncludeDigiLinks = false;
        coverage.Record(Packet("F4AAA-9", ">en route", T0, "F1CNH-3*", "F5ZBC-3*", "WIDE2*"), PacketOrigin.Rf);

        Assert.Equal(0, coverage.Summarize("F5ZBC-3", Digi, T0.AddMinutes(1))!.Frames);
        Assert.Empty(coverage.HeardDirect("F5ZBC-3", Digi, T0.AddMinutes(1)));

        coverage.IncludeDigiLinks = true;
        Assert.Equal(1, coverage.Summarize("F5ZBC-3", Digi, T0.AddMinutes(1))!.Frames);
    }

    [Fact]
    public void No_link_through_an_unsigned_hop_nor_to_a_digi_never_placed()
    {
        var coverage = LinksCoverage("F5ZBC-3", "F6XYZ-2");
        Assert.False(coverage.Record(Packet("F4AAA-9", ">a", T0, "F1CNH-3*", "WIDE1*", "WIDE2*", "F5ZBC-3*"), PacketOrigin.Rf));
        // F4NOWHERE-1 has no known position
        Assert.False(coverage.Record(Packet("F4AAA-9", ">b", T0, "F4NOWHERE-1*", "F6XYZ-2*"), PacketOrigin.Rf));
    }

    [Fact]
    public void Our_own_station_hears_the_last_digi_of_a_radio_frame()
    {
        var coverage = LinksCoverage();
        coverage.SetOwnStation("F4DVK-10");
        Assert.True(coverage.Record(Packet("F4AAA-9", ">a", T0, "F1CNH-3*", "WIDE2-1"), PacketOrigin.Rf));
        // heard through APRS-IS: not by our TNCs
        Assert.False(coverage.Record(Packet("F4AAA-9", ">b", T0.AddMinutes(1), "F5ZBC-3*", "qAR", "F4IGT"), PacketOrigin.Internet));

        var heard = Assert.Single(coverage.HeardDirect("F4DVK-10", Digi, T0.AddMinutes(2)));
        Assert.Equal("F1CNH-3", heard.Station);
        Assert.Equal(Km(49, 3), heard.DistanceKm!.Value, 6);
    }

    [Fact]
    public void Links_between_digis_survive_a_restart()
    {
        var coverage = LinksCoverage("F5ZBC-3");
        coverage.Record(Packet("F4AAA-9", ">en route", T0, "F1CNH-3*", "F5ZBC-3*"), PacketOrigin.Rf);
        string path = Path.Combine(Path.GetTempPath(), $"coverage-{Guid.NewGuid():N}.json");
        try
        {
            CoverageStore.Save(coverage.Logs, path);
            var restored = new DigiCoverage();
            restored.Watch("F5ZBC-3");
            restored.Restore(CoverageStore.Load(path), T0.AddHours(1));
            Assert.Equal(0, restored.Summarize("F5ZBC-3", Digi, T0.AddHours(1))!.Frames);
            restored.IncludeDigiLinks = true;
            Assert.Equal(1, restored.Summarize("F5ZBC-3", Digi, T0.AddHours(1))!.Frames);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void The_last_frame_heard_is_kept_raw_across_a_restart()
    {
        Hear("F4AAA-9", "!4900.00N/00300.00E>a", T0, "F1ZXX-3*", "WIDE2-1");
        Hear("F4AAA-9", "!4910.00N/00310.00E>b", T0.AddMinutes(5), "F1ZXX-3*", "WIDE2*");

        var heard = Assert.Single(_coverage.HeardDirect("F1ZXX-3", Digi, T0.AddMinutes(6)));
        Assert.Equal("F4AAA-9>APRS,F1ZXX-3*,WIDE2*:!4910.00N/00310.00E>b", heard.LastFrame);
        Assert.Equal(T0.AddMinutes(5), heard.LastFrameAt);

        string path = Path.Combine(Path.GetTempPath(), $"coverage-{Guid.NewGuid():N}.json");
        try
        {
            CoverageStore.Save(_coverage.Logs, path);
            var restored = new DigiCoverage();
            restored.Watch("F1ZXX-3");
            restored.Restore(CoverageStore.Load(path), T0.AddMinutes(6));
            Assert.Equal(heard.LastFrame, Assert.Single(restored.HeardDirect("F1ZXX-3", Digi, T0.AddMinutes(6))).LastFrame);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
