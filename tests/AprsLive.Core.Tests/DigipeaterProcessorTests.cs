using AprsLive.Core.Ax25;

namespace AprsLive.Core.Tests;

public class DigipeaterProcessorTests
{
    [Theory]
    [InlineData("qAR")]
    [InlineData("TCPIP*")]
    public void Never_repeats_aprs_is_traffic_bridged_onto_a_radio_port(string marker)
    {
        // What an iGate's "APRS-IS bridge" hands its KISS clients: an
        // unused WIDE1-1 from the original sender, then the q construct.
        var frame = Ax25Builder.BuildUi("F4ABC-7", "APLIVE", new[] { "WIDE1-1", marker, "F4IGT" }, "!4830.00N/00230.00E>test");

        Assert.Null(DigipeaterProcessor.TryDigipeat(frame, "F4DVK-9", wide1: true, wide2: true, wide3: false));
    }

    [Fact]
    public void Decrements_a_multi_hop_alias_without_inserting_own_call()
    {
        var frame = Ax25Builder.BuildUi("F4ABC-7", "APLIVE", new[] { "WIDE2-2" }, "!4830.00N/00230.00E>test");

        var result = DigipeaterProcessor.TryDigipeat(frame, "F4DVK-9", wide1: true, wide2: true, wide3: false);

        Assert.NotNull(result);
        Assert.True(Ax25Frame.TryParse(result!, out var f));
        Assert.Equal("F4ABC-7>APLIVE,WIDE2-1:!4830.00N/00230.00E>test", f!.ToTnc2());
        Assert.False(f.Path[0].HasBeenRepeated);
    }

    [Fact]
    public void Substitutes_own_callsign_on_the_last_hop()
    {
        var frame = Ax25Builder.BuildUi("F4ABC-7", "APLIVE", new[] { "WIDE1-1" }, "!4830.00N/00230.00E>test");

        var result = DigipeaterProcessor.TryDigipeat(frame, "F4DVK-9", wide1: true, wide2: true, wide3: false);

        Assert.NotNull(result);
        Assert.True(Ax25Frame.TryParse(result!, out var f));
        Assert.Equal("F4ABC-7>APLIVE,F4DVK-9*:!4830.00N/00230.00E>test", f!.ToTnc2());
        Assert.True(f.Path[0].HasBeenRepeated);
    }

    [Fact]
    public void Preserves_a_digipeater_already_marked_repeated_before_the_active_hop()
    {
        var frame = Ax25Builder.BuildUi("F4ABC-7", "APLIVE", new[] { "F1XYZ*", "WIDE1-1" }, "test");

        var result = DigipeaterProcessor.TryDigipeat(frame, "F4DVK-9", wide1: true, wide2: true, wide3: false);

        Assert.NotNull(result);
        Assert.True(Ax25Frame.TryParse(result!, out var f));
        Assert.Equal("F4ABC-7>APLIVE,F1XYZ*,F4DVK-9*:test", f!.ToTnc2());
    }

    [Fact]
    public void Ignores_an_alias_family_that_is_disabled()
    {
        var frame = Ax25Builder.BuildUi("F4ABC-7", "APLIVE", new[] { "WIDE3-3" }, "test");

        var result = DigipeaterProcessor.TryDigipeat(frame, "F4DVK-9", wide1: true, wide2: true, wide3: false);

        Assert.Null(result);
    }

    [Fact]
    public void Does_not_act_when_the_next_hop_is_not_a_generic_alias()
    {
        // direct-addressed digipeat (a real callsign, not WIDEn-N) is out of scope here
        var frame = Ax25Builder.BuildUi("F4ABC-7", "APLIVE", new[] { "F4DVK-9" }, "test");

        var result = DigipeaterProcessor.TryDigipeat(frame, "F4DVK-9", wide1: true, wide2: true, wide3: true);

        Assert.Null(result);
    }

    [Fact]
    public void Never_redigipeats_its_own_transmissions()
    {
        var frame = Ax25Builder.BuildUi("F4DVK-9", "APLIVE", new[] { "WIDE1-1" }, "test");

        var result = DigipeaterProcessor.TryDigipeat(frame, "F4DVK-9", wide1: true, wide2: true, wide3: true);

        Assert.Null(result);
    }

    [Fact]
    public void Does_nothing_once_every_path_entry_is_already_repeated()
    {
        var frame = Ax25Builder.BuildUi("F4ABC-7", "APLIVE", new[] { "F4DVK-9*" }, "test");

        var result = DigipeaterProcessor.TryDigipeat(frame, "F4DVK-9", wide1: true, wide2: true, wide3: true);

        Assert.Null(result);
    }
}
