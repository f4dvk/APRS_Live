using AprsLive.Core.Aprs;
using AprsLive.Core.Ax25;
using AprsLive.Core.Stations;

namespace AprsLive.Core.Tests;

public class StationStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "aprslive-store-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "stations.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private static void Apply(StationRegistry registry, string source, string info, IEnumerable<string> path,
        PacketOrigin origin = PacketOrigin.Rf, DateTimeOffset? at = null, int channel = 0)
    {
        var bytes = Ax25Builder.BuildUi(source, "APDW18", path, info);
        Assert.True(Ax25Frame.TryParse(bytes, out var f));
        registry.Apply(AprsParser.Parse(f!, at), origin, channel);
    }

    [Fact]
    public void Saved_stations_keep_the_channels_they_were_heard_on()
    {
        var heard = new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.FromHours(1));
        var registry = new StationRegistry();
        Apply(registry, "F4ABC-7", "!4903.50N/00230.00E>Direct", new[] { "WIDE1-1" }, at: heard, channel: 2);

        StationStore.Save(registry.Stations, FilePath);
        var restored = new StationRegistry();
        restored.Restore(StationStore.Load(FilePath));

        var station = restored.Stations.Single();
        Assert.Null(station.Tnc1);
        Assert.Equal(heard, station.Tnc2!.LastHeard);
        Assert.Equal(heard, station.Tnc2.LastDirect);
    }

    [Fact]
    public void Saved_stations_come_back_with_symbol_route_and_paths()
    {
        var heard = new DateTimeOffset(2026, 11, 6, 18, 25, 44, TimeSpan.FromHours(1));
        var registry = new StationRegistry();
        Apply(registry, "F5KOU-5", "!5000.33NS00241.45E#/A=000262Radio club", new[] { "F1PRY-14*", "WIDE1*", "WIDE2-1" },
            at: heard);
        Apply(registry, "F4ABC-7", "!4903.50N/00230.00E>Direct", new[] { "WIDE1-1" }, at: heard);
        Apply(registry, "F4DVK-9", "!4851.00N/00222.00E>Moi", new[] { "WIDE1-1" }, PacketOrigin.Local, heard);

        StationStore.Save(registry.Stations, FilePath);
        var restored = new StationRegistry();
        restored.Restore(StationStore.Load(FilePath));

        Assert.Equal(3, restored.Stations.Count);
        var kou = restored.Stations.Single(s => s.Callsign == "F5KOU-5");
        Assert.Equal('S', kou.SymbolTable);
        Assert.Equal('#', kou.SymbolCode);
        Assert.Equal(heard, kou.LastHeard);
        Assert.Equal(StationRoute.Digipeater, kou.LastRoute);
        Assert.Equal(["F1PRY-14*", "WIDE1*", "WIDE2-1"], kou.LastRfPath!.Select(a => a.ToStringWithFlag()));
        Assert.Equal(50.0055, kou.Latitude!.Value, 4);

        var direct = restored.Stations.Single(s => s.Callsign == "F4ABC-7");
        Assert.True(direct.HeardDirect);
        Assert.Equal(heard, direct.LastHeardDirect);
        Assert.True(restored.Stations.Single(s => s.Callsign == "F4DVK-9").IsLocal);
        Assert.Equal("F4ABC-7", restored.FindBestDx(48.0, 2.0, now: heard)!.Callsign);
    }

    [Fact]
    public void Restore_keeps_a_station_already_heard_since_the_start_and_raises_one_event()
    {
        var old = new StationRegistry();
        Apply(old, "F4ABC-7", "!4903.50N/00230.00E>Ancien", new[] { "WIDE1-1" });
        Apply(old, "F4XYZ", "!4900.00N/00200.00E>Autre", new[] { "WIDE1-1" });

        var registry = new StationRegistry();
        Apply(registry, "F4ABC-7", "!4904.00N/00231.00E>Nouveau", new[] { "WIDE1-1" });
        int events = 0;
        registry.StationsChanged += () => events++;
        registry.Restore(old.Stations);

        Assert.Equal(1, events);
        Assert.Equal(2, registry.Stations.Count);
        Assert.Equal("Nouveau", registry.Stations.Single(s => s.Callsign == "F4ABC-7").Comment);
    }

    [Fact]
    public void Expiry_applies_to_restored_stations()
    {
        var t0 = new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.FromHours(1));
        var old = new StationRegistry();
        Apply(old, "F4OLD", "!4900.00N/00200.00E>Vieille", new[] { "WIDE1-1" }, at: t0);
        Apply(old, "F4NEW", "!4900.00N/00300.00E>Recente", new[] { "WIDE1-1" }, at: t0.AddMinutes(50));
        StationStore.Save(old.Stations, FilePath);

        var registry = new StationRegistry();
        registry.Restore(StationStore.Load(FilePath));
        registry.RemoveNotHeardSince(TimeSpan.FromMinutes(30), now: t0.AddMinutes(60));

        Assert.Equal("F4NEW", registry.Stations.Single().Callsign);
    }

    [Fact]
    public void Missing_or_corrupt_file_gives_an_empty_list()
    {
        Assert.Empty(StationStore.Load(FilePath));
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ pas du json");
        Assert.Empty(StationStore.Load(FilePath));
    }
}
