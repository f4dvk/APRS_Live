using AprsLive.Core.Ax25;
using AprsLive.Core.Settings;

namespace AprsLive.Core.Tests;

public class DigiRouterTests
{
    private const string Own = "F4DVK-9";
    private static readonly DateTime T0 = new(2026, 11, 7, 10, 0, 0, DateTimeKind.Utc);

    private static byte[] Beacon(params string[] path) =>
        Ax25Builder.BuildUi("F4ABC-7", "APLIVE", path, "!4830.00N/00230.00E>test");

    /// <summary>UI-View "1=2  2=1,2".</summary>
    private static DigiSettings UiViewExample() => new()
    {
        Route1To1 = false, Route1To2 = true,
        Route2To1 = true, Route2To2 = true,
    };

    [Fact]
    public void Follows_the_routes_of_the_channel_it_was_heard_on()
    {
        var from1 = new DigiRouter().Route(Beacon("WIDE1-1"), 1, UiViewExample(), Own, true, true, T0);
        var from2 = new DigiRouter().Route(Beacon("WIDE2-1"), 2, UiViewExample(), Own, true, true, T0);

        Assert.Equal(new[] { 2 }, from1!.SendOn);
        Assert.Equal(new[] { 1, 2 }, from2!.SendOn);
    }

    [Fact]
    public void Hears_on_both_channels_and_repeats_on_one()
    {
        var settings = new DigiSettings { Route1To1 = false, Route1To2 = true, Route2To1 = false, Route2To2 = true };
        Assert.Equal(new[] { 2 }, new DigiRouter().Route(Beacon("WIDE1-1"), 1, settings, Own, true, true, T0)!.SendOn);
        Assert.Equal(new[] { 2 }, new DigiRouter().Route(Beacon("WIDE2-1"), 2, settings, Own, true, true, T0)!.SendOn);
    }

    [Fact]
    public void Listens_on_a_channel_whose_tx_is_off()
    {
        var decision = new DigiRouter().Route(Beacon("WIDE1-1"), 1, UiViewExample(), Own, txOn1: false, txOn2: true, T0);

        Assert.Equal(new[] { 2 }, decision!.SendOn);
    }

    [Fact]
    public void Skips_a_destination_channel_whose_tx_is_off()
    {
        var from2 = new DigiRouter().Route(Beacon("WIDE1-1"), 2, UiViewExample(), Own, txOn1: true, txOn2: false, T0);
        var from1 = new DigiRouter().Route(Beacon("WIDE2-1"), 1, UiViewExample(), Own, txOn1: true, txOn2: false, T0);

        Assert.Equal(new[] { 1 }, from2!.SendOn);
        Assert.Null(from1);
    }

    [Fact]
    public void Leaves_a_frame_with_nothing_to_digipeat_alone()
    {
        Assert.Null(new DigiRouter().Route(Beacon("F5XYZ-1*"), 1, UiViewExample(), Own, true, true, T0));
    }

    [Fact]
    public void Does_not_send_a_packet_again_on_a_channel_when_it_comes_back_through_another_digi()
    {
        var router = new DigiRouter();

        // Heard direct on 1, repeated on 2...
        var first = router.Route(Beacon("WIDE1-1", "WIDE2-2"), 1, UiViewExample(), Own, true, true, T0);
        // ...where another digi repeats it, and we hear it back on 2 with a hop left.
        var echo = router.Route(Beacon("F4DVK-9*", "F5DIG-1*", "WIDE2-1"), 2, UiViewExample(), Own, true, true, T0.AddSeconds(2));

        // Already sent on 2, and already heard on 1: it goes nowhere.
        Assert.Equal(new[] { 2 }, first!.SendOn);
        Assert.Empty(echo!.SendOn);
        Assert.Equal(new[] { 1, 2 }, echo.AlreadyOnAir);
    }

    [Fact]
    public void A_packet_heard_on_a_channel_is_not_sent_back_there_later()
    {
        // TNC1 only listened to (no route out of it), TNC2 repeats onto TNC1.
        var settings = new DigiSettings { Route1To1 = false, Route1To2 = false, Route2To1 = true, Route2To2 = false };
        var router = new DigiRouter();

        var heardOn1 = router.Route(Beacon("WIDE1-1", "WIDE2-1"), 1, settings, Own, true, true, T0);
        var laterOn2 = router.Route(Beacon("F5DIG-1*", "WIDE2-1"), 2, settings, Own, true, true, T0.AddSeconds(3));

        Assert.Null(heardOn1);
        Assert.Empty(laterOn2!.SendOn);
        Assert.Equal(new[] { 1 }, laterOn2.AlreadyOnAir);
    }

    [Fact]
    public void A_route_back_onto_the_same_channel_repeats_the_first_copy_only()
    {
        var single = new DigiSettings { Route1To1 = true, Route1To2 = false, Route2To1 = false, Route2To2 = false };
        var router = new DigiRouter();

        var direct = router.Route(Beacon("WIDE1-1", "WIDE2-1"), 1, single, Own, true, false, T0);
        var viaOtherDigi = router.Route(Beacon("F5DIG-1*", "WIDE2-1"), 1, single, Own, true, false, T0.AddSeconds(1));

        Assert.Equal(new[] { 1 }, direct!.SendOn);
        Assert.Empty(viaOtherDigi!.SendOn);
    }

    [Fact]
    public void Same_packet_heard_on_both_channels_goes_out_once_per_channel()
    {
        var all = new DigiSettings { Route1To1 = true, Route1To2 = true, Route2To1 = true, Route2To2 = true };
        var router = new DigiRouter();

        var onA = router.Route(Beacon("WIDE1-1"), 1, all, Own, true, true, T0);
        var onB = router.Route(Beacon("WIDE1-1"), 2, all, Own, true, true, T0.AddMilliseconds(300));

        Assert.Equal(new[] { 1, 2 }, onA!.SendOn);
        Assert.Empty(onB!.SendOn);
        Assert.Equal(new[] { 1, 2 }, onB.AlreadyOnAir);
    }

    [Fact]
    public void Repeats_the_same_packet_again_once_the_window_is_over()
    {
        var router = new DigiRouter();

        router.Route(Beacon("WIDE1-1"), 1, UiViewExample(), Own, true, true, T0);
        var later = router.Route(Beacon("WIDE1-1"), 1, UiViewExample(), Own, true, true, T0 + DigiRouter.DuplicateWindow);

        Assert.Equal(new[] { 2 }, later!.SendOn);
    }

    [Fact]
    public void A_different_packet_from_the_same_station_is_not_a_duplicate()
    {
        var router = new DigiRouter();
        var other = Ax25Builder.BuildUi("F4ABC-7", "APLIVE", new[] { "WIDE1-1" }, ">status");

        router.Route(Beacon("WIDE1-1"), 1, UiViewExample(), Own, true, true, T0);
        var decision = router.Route(other, 1, UiViewExample(), Own, true, true, T0.AddSeconds(1));

        Assert.Equal(new[] { 2 }, decision!.SendOn);
    }

    [Fact]
    public void Describes_the_routes_in_ui_view_notation()
    {
        Assert.Equal("1=2  2=1,2", DigiRouter.Describe(UiViewExample()));
        Assert.Equal("2=2", DigiRouter.Describe(new DigiSettings { Route1To1 = false, Route2To2 = true }));
        Assert.Equal("", DigiRouter.Describe(new DigiSettings { Route1To1 = false, Route2To2 = false }));
    }
}

public class DigiSettingsUpgradeTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"aprslive-digi-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path))
            File.Delete(_path);
    }

    private DigiSettings LoadDigi(string digiJson)
    {
        File.WriteAllText(_path, $"{{ \"Digi\": {digiJson} }}");
        return AppSettings.Load(_path).Digi;
    }

    [Fact]
    public void One_ticked_channel_becomes_a_route_onto_itself()
    {
        var digi = LoadDigi("""{ "Enabled": true, "RepeatOnTnc1": true, "RepeatOnTnc2": false }""");

        Assert.True(digi.Enabled);
        Assert.Equal((true, false, false, false), (digi.Route1To1, digi.Route1To2, digi.Route2To1, digi.Route2To2));
    }

    [Fact]
    public void Both_ticked_channels_become_every_route()
    {
        var digi = LoadDigi("""{ "RepeatOnTnc1": true, "RepeatOnTnc2": true }""");

        Assert.Equal((true, true, true, true), (digi.Route1To1, digi.Route1To2, digi.Route2To1, digi.Route2To2));
    }

    [Fact]
    public void Saved_routes_are_kept_and_the_old_flags_are_no_longer_written()
    {
        var settings = new AppSettings { Digi = { Route1To1 = false, Route1To2 = true, Route2To1 = true, Route2To2 = true } };
        settings.Save(_path);

        var json = File.ReadAllText(_path);
        var digi = AppSettings.Load(_path).Digi;

        Assert.DoesNotContain("RepeatOnTnc", json);
        Assert.Equal("1=2  2=1,2", DigiRouter.Describe(digi));
    }
}
