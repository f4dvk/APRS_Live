using AprsLive.Core.Aprs;
using AprsLive.Core.Ax25;

namespace AprsLive.Core.Tests;

public class Ax25Tests
{
    [Theory]
    [InlineData(false, "WIDE1-1")]
    [InlineData(false, "F4DIGI-1*", "WIDE2-1")]
    [InlineData(true, "TCPIP*")]
    [InlineData(true, "TCPXX*")]
    [InlineData(true, "WIDE1-1", "qAR", "F4IGT")]
    [InlineData(true, "qAC", "T2FRA")]
    public void HasInternetPath_spots_aprs_is_markers(bool expected, params string[] path)
    {
        var bytes = Ax25Builder.BuildUi("F6KTN", "APRS", path, ">st");
        Assert.True(Ax25Frame.TryParse(bytes, out var frame));

        Assert.Equal(expected, frame!.HasInternetPath);
    }

    [Theory]
    [InlineData(false, 0, "WIDE1-1", "WIDE2-1")]
    [InlineData(true, 0, "F1CNH-3*", "WIDE2-1")]
    // its own call first: a digi beaconing through its own digipeater, heard direct
    [InlineData(false, 1, "F5ZBC-3*", "WIDE2-1")]
    [InlineData(false, 2, "F5ZBC-3*", "WIDE1*", "WIDE2-1")]
    [InlineData(false, 2, "F5ZBC-3", "WIDE2*", "qAR", "F4IGT")]
    // then repeated by another digi
    [InlineData(true, 1, "F5ZBC-3*", "F1CNH-3*", "WIDE2*")]
    [InlineData(true, 2, "F5ZBC-3*", "WIDE1*", "F1CNH-3*")]
    // same call, other SSID: another station
    [InlineData(true, 0, "F5ZBC-2*", "WIDE2-1")]
    public void Only_a_hop_repeated_by_someone_else_than_the_sender_means_via_digi(bool expected, int senderHops, params string[] path)
    {
        var bytes = Ax25Builder.BuildUi("F5ZBC-3", "APRS", path, ">st");
        Assert.True(Ax25Frame.TryParse(bytes, out var frame));

        Assert.Equal(senderHops, frame!.SenderHops);
        Assert.Equal(expected, frame.HasBeenDigipeated);
    }

    [Fact]
    public void BuildAck_is_a_valid_APRS_message_ack()
    {
        var frame = Ax25Builder.BuildAck("PCT-1", "F4DVK-14", "05",
            destination: "APLIVE", path: new[] { "WIDE1-1" });

        Assert.True(Ax25Frame.TryParse(frame, out var f));
        var p = AprsParser.Parse(f!);
        Assert.Equal(AprsPacketKind.Message, p.Kind);
        Assert.True(p.IsAck);
        Assert.Equal("F4DVK-14", p.MessageAddressee);   // trimmed from the 9-char field
        Assert.Equal("05", p.MessageNumber);
        Assert.Equal("PCT", f!.Source.Base);
    }

    [Fact]
    public void Build_then_parse_round_trips_with_digipeaters()
    {
        var frame = Ax25Builder.BuildUi(
            source: "F4DVK-14",
            destination: "APLIVE",
            path: new[] { "F6ABC-2*", "WIDE2-1" },
            info: "!4830.00N/00230.00E>test");

        Assert.True(Ax25Frame.TryParse(frame, out var f));
        Assert.NotNull(f);
        Assert.Equal("F4DVK", f!.Source.Base);
        Assert.Equal(14, f.Source.Ssid);
        Assert.Equal("APLIVE", f.Destination.Callsign);
        Assert.Equal(2, f.Path.Count);
        Assert.True(f.Path[0].HasBeenRepeated);
        Assert.False(f.Path[1].HasBeenRepeated);
        Assert.Equal("F4DVK-14>APLIVE,F6ABC-2*,WIDE2-1:!4830.00N/00230.00E>test", f.ToTnc2());
    }

    [Fact]
    public void Truncated_frame_is_rejected()
    {
        Assert.False(Ax25Frame.TryParse(new byte[] { 1, 2, 3 }, out _));
    }

    [Fact]
    public void BuildPositionReport_round_trips_through_the_parser()
    {
        var frame = Ax25Builder.BuildPositionReport("F4DVK-9", "APLIVE", new[] { "WIDE1-1" },
            latitude: 48.8566, longitude: 2.3522, symbolTable: '/', symbolCode: '>', comment: "Via APRS_Live");

        Assert.True(Ax25Frame.TryParse(frame, out var f));
        var p = Aprs.AprsParser.Parse(f!);

        Assert.Equal(Aprs.AprsPacketKind.Position, p.Kind);
        Assert.Equal(48.8566, p.Latitude!.Value, 3);
        Assert.Equal(2.3522, p.Longitude!.Value, 3);
        Assert.Equal('/', p.SymbolTable);
        Assert.Equal('>', p.SymbolCode);
        Assert.Equal("Via APRS_Live", p.Comment);
    }

    [Fact]
    public void BuildPositionReport_encodes_southern_western_hemispheres()
    {
        var frame = Ax25Builder.BuildPositionReport("F4DVK-9", "APLIVE", Array.Empty<string>(),
            latitude: -33.865, longitude: -151.209, symbolTable: '/', symbolCode: '>', comment: "");

        Assert.True(Ax25Frame.TryParse(frame, out var f));
        var p = Aprs.AprsParser.Parse(f!);

        Assert.Equal(-33.865, p.Latitude!.Value, 3);
        Assert.Equal(-151.209, p.Longitude!.Value, 3);
    }

    [Fact]
    public void BuildObjectReport_round_trips_as_a_live_object()
    {
        var frame = Ax25Builder.BuildObjectReport("F4DVK-9", "APLIVE", new[] { "WIDE1-1" },
            objectName: "EVENT1", live: true, timestampUtc: new DateTime(2026, 10, 9, 14, 30, 0, DateTimeKind.Utc),
            latitude: 48.8566, longitude: 2.3522, symbolTable: '/', symbolCode: '>', comment: "Course cycliste");

        Assert.True(Ax25Frame.TryParse(frame, out var f));
        var p = Aprs.AprsParser.Parse(f!);

        Assert.Equal(Aprs.AprsPacketKind.Object, p.Kind);
        Assert.Equal("EVENT1", p.ObjectName);
        Assert.True(p.ObjectLive);
        Assert.Equal(48.8566, p.Latitude!.Value, 3);
        Assert.Equal(2.3522, p.Longitude!.Value, 3);
        Assert.Equal('/', p.SymbolTable);
        Assert.Equal('>', p.SymbolCode);
        Assert.Equal("Course cycliste", p.Comment);
        Assert.Equal("F4DVK", f!.Source.Base);
    }

    [Fact]
    public void BuildObjectReport_kill_sets_the_object_not_live()
    {
        var frame = Ax25Builder.BuildObjectReport("F4DVK-9", "APLIVE", Array.Empty<string>(),
            objectName: "RELAIS", live: false, timestampUtc: DateTime.UtcNow,
            latitude: 48.0, longitude: 2.0, symbolTable: '/', symbolCode: '>', comment: "");

        Assert.True(Ax25Frame.TryParse(frame, out var f));
        var p = Aprs.AprsParser.Parse(f!);

        Assert.Equal("RELAIS", p.ObjectName);
        Assert.False(p.ObjectLive);
    }

    [Fact]
    public void BuildStatusReport_round_trips_through_the_parser()
    {
        var frame = Ax25Builder.BuildStatusReport("F4DVK-9", "APLIVE", new[] { "WIDE1-1" }, "Test status text");

        Assert.True(Ax25Frame.TryParse(frame, out var f));
        var p = Aprs.AprsParser.Parse(f!);

        Assert.Equal(Aprs.AprsPacketKind.Status, p.Kind);
        Assert.Equal("Test status text", p.Comment);
    }

    [Fact]
    public void BuildStatusReport_truncates_to_the_APRS_recommended_length()
    {
        string longText = new string('A', 100);
        var frame = Ax25Builder.BuildStatusReport("F4DVK-9", "APLIVE", Array.Empty<string>(), longText);

        Assert.True(Ax25Frame.TryParse(frame, out var f));
        var p = Aprs.AprsParser.Parse(f!);

        Assert.Equal(Ax25Builder.MaxStatusTextLength, p.Comment!.Length);
    }
}
