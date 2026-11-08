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

    private bool Hear(string source, string info, DateTimeOffset at, params string[] path)
    {
        var bytes = Ax25Builder.BuildUi(source, "APRS", path, info);
        Assert.True(Ax25Frame.TryParse(bytes, out var f));
        return _coverage.Record(AprsParser.Parse(f!, at));
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
}
