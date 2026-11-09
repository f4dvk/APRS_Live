using AprsLive.Core.Aprs;
using AprsLive.Core.Ax25;
using AprsLive.Core.Stations;

namespace AprsLive.Core.Tests;

public class StationRegistryTests
{
    private static AprsPacket ParsePosition(string source, string info, IEnumerable<string>? path = null)
    {
        var frame = Ax25Builder.BuildUi(source, "APLIVE", path ?? Array.Empty<string>(), info);
        Assert.True(Ax25Frame.TryParse(frame, out var f));
        return AprsParser.Parse(f!);
    }

    [Fact]
    public void Adds_a_new_station_with_position()
    {
        var registry = new StationRegistry();
        HeardStation? updated = null;
        registry.StationUpdated += s => updated = s;

        var packet = ParsePosition("F4ABC-7", "!4903.50N/00230.00E>Test", new[] { "WIDE1-1" });
        registry.Apply(packet);

        Assert.NotNull(updated);
        Assert.Equal("F4ABC-7", updated!.Callsign);
        Assert.Equal("Test", updated.Comment);
        Assert.Equal("APLIVE via WIDE1-1", updated.PathText);
        Assert.Single(registry.Stations);
    }

    [Fact]
    public void Updates_an_existing_station_in_place_rather_than_duplicating()
    {
        var registry = new StationRegistry();
        registry.Apply(ParsePosition("F4ABC-7", "!4903.50N/00230.00E>First"));
        registry.Apply(ParsePosition("F4ABC-7", "!4904.50N/00231.00E>Second"));

        Assert.Single(registry.Stations);
        var station = registry.Stations.Single();
        Assert.Equal("Second", station.Comment);
    }

    [Fact]
    public void Captures_course_speed_and_altitude_when_present()
    {
        var registry = new StationRegistry();
        registry.Apply(ParsePosition("F4ABC-7", "!4903.50N/00230.00E>088/036/A=001234Test"));

        var station = registry.Stations.Single();
        Assert.Equal(88, station.CourseDeg);
        Assert.Equal(36, station.SpeedKnots);
        Assert.NotNull(station.AltitudeM);
    }

    [Fact]
    public void Ignores_packets_without_a_position()
    {
        var registry = new StationRegistry();
        registry.Apply(ParsePosition("F4ABC-7", ">Status texte seulement"));

        Assert.Empty(registry.Stations);
    }

    [Fact]
    public void HeardDirect_is_true_when_no_path_hop_has_been_repeated()
    {
        var registry = new StationRegistry();
        registry.Apply(ParsePosition("F4ABC-7", "!4903.50N/00230.00E>Direct", new[] { "WIDE1-1" }));

        Assert.True(registry.Stations.Single().HeardDirect);
    }

    [Fact]
    public void HeardDirect_is_false_when_a_digipeater_has_repeated_it()
    {
        var registry = new StationRegistry();
        registry.Apply(ParsePosition("F4ABC-7", "!4903.50N/00230.00E>ViaDigi", new[] { "F4DIGI-1*", "WIDE2-1" }));

        Assert.False(registry.Stations.Single().HeardDirect);
    }

    [Fact]
    public void A_digi_beaconing_through_its_own_digipeater_is_heard_direct()
    {
        var registry = new StationRegistry();
        registry.Apply(ParsePosition("F1CNH-3", "!4903.50N/00230.00E#Digi", new[] { "F1CNH-3*", "WIDE2-1" }));

        var station = registry.Stations.Single();
        Assert.True(station.HeardDirect);
        Assert.Equal(StationRoute.Direct, station.LastRoute);
    }

    [Fact]
    public void FindBestDx_picks_the_farthest_direct_station_ignoring_relayed_ones()
    {
        var registry = new StationRegistry();
        // ~11 km direct
        registry.Apply(ParsePosition("F4NEAR", "!4903.50N/00230.00E>Near", new[] { "WIDE1-1" }));
        // ~1100 km direct (the one FindBestDx should pick)
        registry.Apply(ParsePosition("F4FAR", "!5800.00N/00230.00E>Far", new[] { "WIDE1-1" }));
        // Farther still, but relayed through a digipeater — must be excluded
        registry.Apply(ParsePosition("F4RELAYED", "!6500.00N/00230.00E>Relayed", new[] { "F4DIGI-1*" }));

        var best = registry.FindBestDx(fromLatitude: 48.0, fromLongitude: 2.0);

        Assert.NotNull(best);
        Assert.Equal("F4FAR", best!.Callsign);
    }

    [Fact]
    public void Packets_from_aprs_is_are_never_direct_even_without_a_repeated_hop()
    {
        var registry = new StationRegistry();
        registry.Apply(ParsePosition("F4ABC-7", "!4903.50N/00230.00E>Igated", new[] { "WIDE1-1", "qAR", "F4IGT" }),
            PacketOrigin.Internet);

        var station = registry.Stations.Single();
        Assert.False(station.HeardDirect);
        Assert.Equal(StationRoute.Internet, station.LastRoute);
    }

    [Fact]
    public void Our_own_transmissions_are_local_not_direct()
    {
        var registry = new StationRegistry();
        registry.Apply(ParsePosition("F4DVK-9", "!4851.00N/00222.00E>Moi", new[] { "WIDE1-1" }), PacketOrigin.Local);

        var station = registry.Stations.Single();
        Assert.False(station.HeardDirect);
        Assert.Equal(StationRoute.Local, station.LastRoute);
    }

    private static void ApplyCopy(StationRegistry registry, string info, string[] path, DateTimeOffset at,
        PacketOrigin origin = PacketOrigin.Rf, string source = "F4ABC-7", int channel = 0)
    {
        var bytes = Ax25Builder.BuildUi(source, "APLIVE", path, info);
        Assert.True(Ax25Frame.TryParse(bytes, out var f));
        registry.Apply(AprsParser.Parse(f!, at), origin, channel);
    }

    private static readonly DateTimeOffset T0 = new(2026, 11, 7, 10, 0, 0, TimeSpan.FromHours(1));

    [Fact]
    public void Notes_each_channel_a_station_is_heard_on_including_a_repeat_on_the_other_one()
    {
        var registry = new StationRegistry();
        int events = 0;
        registry.StationUpdated += _ => events++;

        ApplyCopy(registry, "!4903.50N/00230.00E>Balise", ["WIDE1-1", "WIDE2-1"], T0, channel: 1);
        ApplyCopy(registry, "!4903.50N/00230.00E>Balise", ["F4DIGI-1*", "WIDE1*", "WIDE2-1"], T0.AddSeconds(2), channel: 2);

        var station = registry.Stations.Single();
        Assert.Equal(StationRoute.Direct, station.LastRoute);   // the first copy still wins
        Assert.Equal(T0, station.Tnc1!.LastHeard);
        Assert.Equal(T0, station.Tnc1.LastDirect);
        Assert.Equal(T0.AddSeconds(2), station.Tnc2!.LastHeard);
        Assert.Null(station.Tnc2.LastDirect);
        Assert.Equal(1, station.LastRfChannel);
        Assert.Equal(2, events);
    }

    [Fact]
    public void A_better_copy_on_the_other_channel_takes_over_the_shown_channel()
    {
        var registry = new StationRegistry();

        ApplyCopy(registry, "!4903.50N/00230.00E>Balise", ["F4DIGI-1*", "WIDE1*"], T0, channel: 1);
        ApplyCopy(registry, "!4903.50N/00230.00E>Balise", ["WIDE1-1"], T0.AddSeconds(1), channel: 2);

        var station = registry.Stations.Single();
        Assert.Equal(StationRoute.Direct, station.LastRoute);
        Assert.Equal(2, station.LastRfChannel);
    }

    [Fact]
    public void Keeps_the_last_direct_time_per_channel_when_later_heard_via_digi()
    {
        var registry = new StationRegistry();

        ApplyCopy(registry, "!4903.50N/00230.00E>Un", ["WIDE1-1"], T0, channel: 1);
        ApplyCopy(registry, "!4903.50N/00230.00E>Deux", ["F4DIGI-1*", "WIDE1*"], T0.AddMinutes(10), channel: 1);

        var tnc1 = registry.Stations.Single().Tnc1!;
        Assert.Equal(T0.AddMinutes(10), tnc1.LastHeard);
        Assert.Equal(T0, tnc1.LastDirect);
        Assert.Null(registry.Stations.Single().Tnc2);
    }

    [Fact]
    public void Internet_and_own_packets_are_not_counted_on_a_channel()
    {
        var registry = new StationRegistry();

        ApplyCopy(registry, "!4903.50N/00230.00E>IS", ["WIDE1-1", "qAR", "F4IGT"], T0, PacketOrigin.Internet, channel: 1);
        ApplyCopy(registry, "!4851.00N/00222.00E>Moi", ["WIDE1-1"], T0, PacketOrigin.Local, source: "F4DVK-9", channel: 1);

        Assert.All(registry.Stations, s => Assert.Null(s.Tnc1));
    }

    [Fact]
    public void The_digis_repeat_of_a_packet_heard_direct_keeps_the_direct_route_and_path()
    {
        var registry = new StationRegistry();
        ApplyCopy(registry, "!4903.50N/00230.00E>Balise", ["WIDE1-1", "WIDE2-1"], T0);
        ApplyCopy(registry, "!4903.50N/00230.00E>Balise", ["F4DIGI-1*", "WIDE1*", "WIDE2-1"], T0.AddSeconds(2));
        ApplyCopy(registry, "!4903.50N/00230.00E>Balise", ["WIDE1-1", "WIDE2-1", "qAR", "F4IGT"], T0.AddSeconds(3),
            PacketOrigin.Internet);

        var station = registry.Stations.Single();
        Assert.Equal(StationRoute.Direct, station.LastRoute);
        Assert.Equal("APLIVE via WIDE1-1,WIDE2-1", station.PathText);
        Assert.DoesNotContain(station.LastRfPath!, a => a.HasBeenRepeated);
        Assert.Equal(T0, station.LastHeard);
        Assert.Equal(["via F4DIGI-1*,WIDE1*,WIDE2-1", "Internet (WIDE1-1,WIDE2-1,QAR,F4IGT)"], station.AlsoHeardVia);

        var path = ReceptionPath.Compute(station, _ => null, (48.0, 2.0));
        Assert.Equal(2, path!.Points.Count);   // straight from the station to us, no digi in between
    }

    [Fact]
    public void A_later_transmission_only_via_digi_shows_the_digi_but_remembers_the_direct_one()
    {
        var registry = new StationRegistry();
        ApplyCopy(registry, "!4903.50N/00230.00E>Balise", ["WIDE1-1"], T0);
        ApplyCopy(registry, "!4903.50N/00230.00E>Balise", ["F4DIGI-1*", "WIDE1*"], T0.AddMinutes(10));   // same text, new beacon

        var station = registry.Stations.Single();
        Assert.Equal(StationRoute.Digipeater, station.LastRoute);
        Assert.Equal(T0, station.LastHeardDirect);
        Assert.Empty(station.AlsoHeardVia);
    }

    [Fact]
    public void An_rf_copy_takes_over_an_internet_copy_that_arrived_first()
    {
        var registry = new StationRegistry();
        ApplyCopy(registry, "!4903.50N/00230.00E>Balise", ["F4DIGI-1*", "WIDE1*", "qAR", "F4IGT"], T0, PacketOrigin.Internet);
        ApplyCopy(registry, "!4903.50N/00230.00E>Balise", ["F4DIGI-1*", "WIDE1*"], T0.AddSeconds(1));

        var station = registry.Stations.Single();
        Assert.Equal(StationRoute.Digipeater, station.LastRoute);
        Assert.Equal(["Internet (F4DIGI-1*,WIDE1*,QAR,F4IGT)"], station.AlsoHeardVia);
    }

    [Fact]
    public void Our_own_beacon_heard_back_through_a_digi_updates_the_route()
    {
        var registry = new StationRegistry();
        ApplyCopy(registry, "!4851.00N/00222.00E>Moi", ["WIDE1-1"], T0, PacketOrigin.Local, "F4DVK-9");
        ApplyCopy(registry, "!4851.00N/00222.00E>Moi", ["F4DIGI-1*", "WIDE1*"], T0.AddSeconds(2), source: "F4DVK-9");

        var station = registry.Stations.Single();
        Assert.Equal(StationRoute.Digipeater, station.LastRoute);
        Assert.True(station.IsLocal);
    }

    [Fact]
    public void Direct_reception_survives_a_later_digipeated_or_igated_copy()
    {
        var registry = new StationRegistry();
        registry.Apply(ParsePosition("F4ABC-7", "!4903.50N/00230.00E>Direct", new[] { "WIDE1-1" }));
        registry.Apply(ParsePosition("F4ABC-7", "!4903.50N/00230.00E>Direct", new[] { "F4DIGI-1*", "WIDE2-1" }));
        registry.Apply(ParsePosition("F4ABC-7", "!4903.50N/00230.00E>Direct", new[] { "WIDE1-1", "qAR", "F4IGT" }),
            PacketOrigin.Internet);

        var station = registry.Stations.Single();
        Assert.True(station.HeardDirect);
        Assert.Equal(StationRoute.Direct, station.LastRoute);
    }

    [Fact]
    public void FindBestDx_uses_the_position_the_station_had_when_heard_direct()
    {
        var registry = new StationRegistry();
        // Mobile heard direct near us, then only through a digi once far away.
        registry.Apply(ParsePosition("F4MOB-9", "!4810.00N/00200.00E>Pres", new[] { "WIDE1-1" }));
        registry.Apply(ParsePosition("F4MOB-9", "!5500.00N/00200.00E>Loin", new[] { "F4DIGI-1*" }));
        registry.Apply(ParsePosition("F4FIX", "!4900.00N/00200.00E>Fixe", new[] { "WIDE1-1" }));

        var best = registry.FindBestDx(fromLatitude: 48.0, fromLongitude: 2.0);

        Assert.Equal("F4FIX", best!.Callsign);
    }

    [Fact]
    public void FindBestDx_drops_stations_not_heard_direct_within_the_window_and_falls_back()
    {
        var t0 = new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.FromHours(1));
        var registry = new StationRegistry();
        ApplyAt(registry, "F4FAR", "!5800.00N/00200.00E>Loin", t0);                    // ~1110 km, heard at 12:00
        ApplyAt(registry, "F4NEAR", "!4900.00N/00200.00E>Pres", t0.AddMinutes(50));    // ~111 km, heard at 12:50

        var window = TimeSpan.FromMinutes(30);
        Assert.Equal("F4FAR", registry.FindBestDx(48.0, 2.0, maxAge: window, now: t0.AddMinutes(10))!.Callsign);
        Assert.Equal("F4NEAR", registry.FindBestDx(48.0, 2.0, maxAge: window, now: t0.AddMinutes(60))!.Callsign);
        Assert.Null(registry.FindBestDx(48.0, 2.0, maxAge: window, now: t0.AddMinutes(90)));
        Assert.Equal("F4FAR", registry.FindBestDx(48.0, 2.0, now: t0.AddMinutes(90))!.Callsign);
    }

    private static void ApplyAt(StationRegistry registry, string source, string info, DateTimeOffset at,
        PacketOrigin origin = PacketOrigin.Rf)
    {
        var bytes = Ax25Builder.BuildUi(source, "APLIVE", new[] { "WIDE1-1" }, info);
        Assert.True(Ax25Frame.TryParse(bytes, out var f));
        registry.Apply(AprsParser.Parse(f!, at), origin);
    }

    [Fact]
    public void Stations_not_heard_within_the_delay_are_removed_but_ours_stay()
    {
        var t0 = new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.FromHours(1));
        var registry = new StationRegistry();
        int removedEvents = 0;
        registry.StationsChanged += () => removedEvents++;

        ApplyAt(registry, "F4OLD", "!4900.00N/00200.00E>Ancienne", t0);
        ApplyAt(registry, "F4NEW", "!4900.00N/00300.00E>Recente", t0.AddMinutes(50));
        ApplyAt(registry, "F4DVK-9", "!4851.00N/00222.00E>Moi", t0, PacketOrigin.Local);
        ApplyAt(registry, "F4DVK-9", "!4851.00N/00222.00E>Moi", t0.AddMinutes(1));   // our beacon heard back on RF

        int removed = registry.RemoveNotHeardSince(TimeSpan.FromMinutes(30), now: t0.AddMinutes(60));

        Assert.Equal(1, removed);
        Assert.Equal(1, removedEvents);
        Assert.Equal(["F4DVK-9", "F4NEW"], registry.Stations.Select(s => s.Callsign).Order());
    }

    [Fact]
    public void Nothing_removed_means_no_event()
    {
        var registry = new StationRegistry();
        registry.StationsChanged += () => Assert.Fail("no station was due");
        registry.Apply(ParsePosition("F4ABC-7", "!4903.50N/00230.00E>Test"));

        Assert.Equal(0, registry.RemoveNotHeardSince(TimeSpan.FromMinutes(30)));
    }

    [Fact]
    public void FindBestDx_excludes_the_operators_own_callsign()
    {
        var registry = new StationRegistry();
        // A TNC echoing our own TX back as RX makes our beacon look like a
        // direct RF reception — not itself a valid "Best DX".
        registry.Apply(ParsePosition("F4DVK-9", "!4851.00N/00222.00E>Moi", new[] { "WIDE1-1" }));

        var best = registry.FindBestDx(fromLatitude: 48.85, fromLongitude: 2.37, excludeCallsign: "F4DVK-9");

        Assert.Null(best);
    }

    [Fact]
    public void FindBestDx_returns_null_when_nothing_was_heard_direct()
    {
        var registry = new StationRegistry();
        registry.Apply(ParsePosition("F4RELAYED", "!5800.00N/00230.00E>Relayed", new[] { "F4DIGI-1*" }));

        Assert.Null(registry.FindBestDx(fromLatitude: 48.0, fromLongitude: 2.0));
    }

    [Fact]
    public void Objects_sent_by_the_same_station_get_distinct_entries_keyed_by_name()
    {
        var registry = new StationRegistry();
        registry.Apply(ParsePosition("F4DVK-9", "!4903.50N/00230.00E>Ma station"));

        var objectFrame = Ax25Builder.BuildObjectReport("F4DVK-9", "APLIVE", Array.Empty<string>(),
            objectName: "EVENT1", live: true, timestampUtc: DateTime.UtcNow,
            latitude: 48.0, longitude: 2.0, symbolTable: '/', symbolCode: '>', comment: "");
        Assert.True(Ax25Frame.TryParse(objectFrame, out var f));
        registry.Apply(AprsParser.Parse(f!));

        Assert.Equal(2, registry.Stations.Count);
        Assert.Contains(registry.Stations, s => s.Callsign == "F4DVK-9");
        Assert.Contains(registry.Stations, s => s.Callsign == "EVENT1");
    }
}
