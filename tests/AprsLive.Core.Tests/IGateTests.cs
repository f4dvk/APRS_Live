using AprsLive.Core.Ax25;
using AprsLive.Core.Tnc;

namespace AprsLive.Core.Tests;

public class IGateTests
{
    private const string Me = "F4DVK-9";

    private DateTime _now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    private IGate NewIGate() => new(() => _now);

    private static Ax25Frame Frame(string source, string info, params string[] path)
    {
        var bytes = Ax25Builder.BuildUi(source, "APRS", path, info);
        Assert.True(Ax25Frame.TryParse(bytes, out var f));
        return f!;
    }

    [Fact]
    public void Gates_a_plain_rf_frame_with_qAR_and_our_callsign()
    {
        string? line = NewIGate().TryGate(Frame("F6KTN", "!5012.15N/00348.88E>Hi", "WIDE1-1"), Me, out _);

        Assert.Equal("F6KTN>APRS,WIDE1-1,qAR,F4DVK-9:!5012.15N/00348.88E>Hi", line);
    }

    [Fact]
    public void Only_the_last_repeated_digipeater_carries_the_star()
    {
        string? line = NewIGate().TryGate(Frame("F6KTN", ">st", "F4DIGI-1*", "WIDE1*", "WIDE2-1"), Me, out _);

        Assert.Equal("F6KTN>APRS,F4DIGI-1,WIDE1*,WIDE2-1,qAR,F4DVK-9:>st", line);
    }

    [Theory]
    [InlineData("NOGATE")]
    [InlineData("RFONLY")]
    [InlineData("TCPIP*")]
    [InlineData("TCPXX*")]
    [InlineData("qAR")]
    public void Never_gates_frames_whose_path_forbids_it(string alias)
    {
        Assert.Null(NewIGate().TryGate(Frame("F6KTN", ">st", "WIDE1-1", alias), Me, out var reason));
        Assert.NotNull(reason);
    }

    [Theory]
    [InlineData("}F5ABC>APRS,TCPIP,F4IGT*:>third party")]
    [InlineData("?APRS?")]
    public void Never_gates_third_party_packets_or_general_queries(string info)
    {
        Assert.Null(NewIGate().TryGate(Frame("F6KTN", info, "WIDE1-1"), Me, out _));
    }

    [Fact]
    public void Never_gates_our_own_frames()
    {
        Assert.Null(NewIGate().TryGate(Frame(Me, "!4851.40N/00221.13E>Moi", "F4DIGI-1*"), Me, out _));
    }

    [Fact]
    public void A_digipeated_copy_of_a_just_gated_packet_is_a_duplicate()
    {
        var igate = NewIGate();
        Assert.NotNull(igate.TryGate(Frame("F6KTN", ">st", "WIDE1-1"), Me, out _));

        _now = _now.AddSeconds(3);
        Assert.Null(igate.TryGate(Frame("F6KTN", ">st", "F4DIGI-1*", "WIDE1*"), Me, out var reason));
        Assert.Equal(IGate.DuplicateReason, reason);
    }

    [Fact]
    public void The_same_packet_is_gated_again_after_the_dupe_window()
    {
        var igate = NewIGate();
        Assert.NotNull(igate.TryGate(Frame("F6KTN", ">st", "WIDE1-1"), Me, out _));

        _now += IGate.DupeWindow;
        Assert.NotNull(igate.TryGate(Frame("F6KTN", ">st", "WIDE1-1"), Me, out _));
    }

    [Fact]
    public void Keeps_only_the_first_line_of_the_payload()
    {
        string? line = NewIGate().TryGate(Frame("F6KTN", ">ligne1\r\nligne2"), Me, out _);

        Assert.Equal("F6KTN>APRS,qAR,F4DVK-9:>ligne1", line);
    }
}
