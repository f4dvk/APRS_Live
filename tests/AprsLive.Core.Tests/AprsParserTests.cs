using AprsLive.Core.Aprs;
using AprsLive.Core.Ax25;

namespace AprsLive.Core.Tests;

public class AprsParserTests
{
    private static AprsPacket ParseInfo(string info, string src = "F4DVK-7", string dst = "APLIVE")
    {
        var frame = Ax25Builder.BuildUi(src, dst, Array.Empty<string>(), info);
        Assert.True(Ax25Frame.TryParse(frame, out var f));
        return AprsParser.Parse(f!);
    }

    [Fact]
    public void Uncompressed_position_with_course_speed()
    {
        var p = ParseInfo("!4903.50N/00230.00E>088/036Comment here");
        Assert.Equal(AprsPacketKind.Position, p.Kind);
        Assert.Equal(49.058333, p.Latitude!.Value, 4);
        Assert.Equal(2.5, p.Longitude!.Value, 4);
        Assert.Equal('>', p.SymbolCode);
        Assert.Equal(88, p.CourseDeg);
        Assert.Equal(36, p.SpeedKnots);
        Assert.Equal("Comment here", p.Comment);
    }

    // Built with the formulas of richonguzman's APRSPacketLib 1.0.10 (LoRa APRS
    // iGate / tracker), for 48.8567 N 2.3500 E. In compressed form the first
    // latitude character is a digit ('5') for any latitude from ~40.5 to
    // ~60.3 N, all of France included.
    [Theory]
    [InlineData("=L5iVmO)%S& !G", 'L', '&')]                       // iGate beacon: overlay L, no course/speed
    [InlineData("=/5iVmO)%S>7MG LoRa Tracker", '/', '>')]          // tracker: course and speed
    public void Compressed_position_whose_latitude_starts_with_a_digit(string info, char table, char code)
    {
        var p = ParseInfo(info, dst: "APLRG1");

        Assert.Equal(AprsPacketKind.Position, p.Kind);
        Assert.Equal(48.8567, p.Latitude!.Value, 3);
        Assert.Equal(2.3500, p.Longitude!.Value, 3);
        Assert.Equal(table, p.SymbolTable);
        Assert.Equal(code, p.SymbolCode);
    }

    [Fact]
    public void Compressed_standing_beacon_has_no_course()
    {
        var p = ParseInfo("=L5iVmO)%S& !G");

        Assert.Null(p.CourseDeg);
        Assert.Null(p.SpeedKnots);
    }

    [Fact]
    public void Compressed_position_comment_follows_the_13_bytes()
    {
        var p = ParseInfo("=/5iVmO)%S>7MG LoRa Tracker");

        Assert.Equal("LoRa Tracker", p.Comment);
        Assert.NotNull(p.CourseDeg);
    }

    [Fact]
    public void MicE_from_a_lora_tracker()
    {
        // Same position, course 90, 30 kt, 35 m, message type 111.
        var p = ParseInfo("`x1\u001co v>/\"4:}", src: "F4ABC-7", dst: "TXUQT0");

        Assert.Equal(AprsPacketKind.Position, p.Kind);
        Assert.Equal(48.8567, p.Latitude!.Value, 3);
        Assert.Equal(2.3500, p.Longitude!.Value, 3);
        Assert.Equal('>', p.SymbolCode);
    }

    [Fact]
    public void Timestamped_position_skips_the_timestamp()
    {
        var p = ParseInfo("@092345z4903.50N/00230.00E-Home");
        Assert.Equal(AprsPacketKind.Position, p.Kind);
        Assert.Equal(49.058333, p.Latitude!.Value, 4);
        Assert.Equal('-', p.SymbolCode);
    }

    [Fact]
    public void Message_and_ack_are_recognised()
    {
        var msg = ParseInfo(":F4DVK-14 :Hello there{12");
        Assert.Equal(AprsPacketKind.Message, msg.Kind);
        Assert.Equal("F4DVK-14", msg.MessageAddressee);
        Assert.Equal("Hello there", msg.MessageText);
        Assert.Equal("12", msg.MessageNumber);

        var ack = ParseInfo(":F4DVK-14 :ack07");
        Assert.True(ack.IsAck);
        Assert.Equal("07", ack.MessageNumber);
    }

    [Fact]
    public void Status_packet()
    {
        var p = ParseInfo(">En route vers zone 4");
        Assert.Equal(AprsPacketKind.Status, p.Kind);
        Assert.Equal("En route vers zone 4", p.Comment);
    }

    [Fact]
    public void MicE_position_speed_and_course()
    {
        // dest "T7P3SY" + info "`(_fn\"Oj/" -> S47 03.39', W112 07.74',
        // speed 20 kt, course 251, symbol /j  (values checked by hand).
        var frame = Ax25Builder.BuildUi("K7XYZ-9", "T7P3SY", Array.Empty<string>(), "`(_fn\"Oj/");
        Assert.True(Ax25Frame.TryParse(frame, out var f));
        var p = AprsParser.Parse(f!);
        Assert.Equal(AprsPacketKind.Position, p.Kind);
        Assert.Equal(-47.0565, p.Latitude!.Value, 3);
        Assert.Equal(-112.129, p.Longitude!.Value, 2);
        Assert.Equal(20, p.SpeedKnots!.Value, 1);
        Assert.Equal(251, p.CourseDeg!.Value, 1);
        Assert.Equal('j', p.SymbolCode);
    }
}
