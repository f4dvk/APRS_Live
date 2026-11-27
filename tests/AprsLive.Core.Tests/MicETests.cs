using AprsLive.Core.Aprs;
using AprsLive.Core.Ax25;
using AprsLive.Core.Stations;

namespace AprsLive.Core.Tests;

// Frames built with a Mic-E encoder written from APRS 1.01 chapter 10, and
// cross-checked with Dire Wolf 1.7's decode_aprs (positions, speed, course,
// altitude, message, DAO, and the device whenever its older table knows it).
public class MicETests
{
    private static AprsPacket Parse(string dest, string info, string src = "F4ABC-9", DateTimeOffset? at = null)
    {
        var frame = Ax25Builder.BuildUi(src, dest, ["WIDE1-1"], info);
        Assert.True(Ax25Frame.TryParse(frame, out var f));
        return AprsParser.Parse(f!, at);
    }

    [Theory]
    // Kenwood: type byte, optional version byte at the end
    [InlineData("TX5QT0", "`x1\u001cq\"b>/]\"4:}Bonjour=", "Kenwood TM-D710", "Bonjour")]
    [InlineData("5P3WW5", "`y[Hr!l>/]Mobile D700", "Kenwood TM-D700", "Mobile D700")]
    [InlineData("47QSQP", "`w=2o>\u007f>/]\"4!}&", "Kenwood TM-D750", null)]
    [InlineData("TUTUP0", "`zO\u001cl \u001c[/>TH-D7 test", "Kenwood TH-D7", "TH-D7 test")]
    [InlineData("T3SVR8", "`w6]l>I[/>\"54}=", "Kenwood TH-D72", null)]
    [InlineData("S352Q3", "`O(Tm53[/>Portable^", "Kenwood TH-D74", "Portable")]
    [InlineData("3YTTSU", "`pWFnU&>/>\"6[}D75&", "Kenwood TH-D75", "D75")]
    // Yaesu
    [InlineData("TXPWPT", "`wD_l \u001c[/`\"5T}_0", "Yaesu FT3D", null)]
    [InlineData("TT5PRW", "`v>gpT5>/`\"4g}145.500MHz QRV_%", "Yaesu FTM-400DR", "145.500MHz QRV")]
    [InlineData("TUUUT2", "`|P,l4+[/`\"DC}_3", "Yaesu FT5D", null)]
    [InlineData("TSQWW9", "`{2/n!D>/`Test 73_4", "Yaesu FTM-500D", "Test 73")]
    // AnyTone, Byonics, SCS and others
    [InlineData("TYRVU9", "`w]\u007flT\u001c[/'\"4S}(8", "AnyTone AT-D878UV", null)]
    [InlineData("TWQYS2", "`{ZMp &>/`D578 mobile(5", "AnyTone AT-D578UV", "D578 mobile")]
    [InlineData("TQUR6Y", "`sAkqRvv/'TT3 tracker|3", "Byonics TinyTrak3", "TT3 tracker")]
    [InlineData("TXSTT0", "`}I)o X>/'\"5H}|4", "Byonics TinyTrak4", null)]
    [InlineData("USSSP7", "`\u007fWZl \u001c&/'P4dragon:4", "SCS P4dragon DR-7400", "P4dragon")]
    [InlineData("UQSPTT", "`v__m :>/`AP510 X", "SainSonic AP510", "AP510")]
    [InlineData("TXTQU3", "`|'\"mTN$/`\"63}APRSdroid[1", "APRSdroid", "APRSdroid")]
    [InlineData("TSTRV1", "`}+dlqb>/`\"4+}LoRa^L", "F4MLV ESP32 LoRa Tracker", "LoRa")]
    // type byte only: the unknown end code stays in the text
    [InlineData("TVQRR6", "`|`Wl \u001c[/`Hello", MicEDevice.GenericMessaging, "Hello")]
    [InlineData("TVQRR6", "`|`Wl \u001c[/'Tracker", MicEDevice.GenericTracker, "Tracker")]
    [InlineData("TXUQT0", "`x1\u001cl \u001c>/ Original", MicEDevice.Original, "Original")]
    public void Device_is_named_and_its_bytes_leave_the_comment(string dest, string info, string device, string? comment)
    {
        var p = Parse(dest, info);

        Assert.Equal(AprsPacketKind.Position, p.Kind);
        Assert.Equal(device, p.Device);
        Assert.Equal(comment, p.Comment);
    }

    [Fact]
    public void No_type_byte_means_no_device()
    {
        // LoRa tracker: the text starts right away with the altitude.
        var p = Parse("TXUQT0", "`x1\u001co v>/\"4:}");

        Assert.Null(p.Device);
        Assert.Equal(35, p.AltitudeM);
        Assert.Null(p.Comment);
    }

    [Theory]
    [InlineData("TUTUP0", "`zO\u001cl \u001c[/>TH-D7 test", "Hors service")]
    [InlineData("TX5QT0", "`x1\u001cq\"b>/]\"4:}Bonjour=", "En route")]
    [InlineData("T3SVR8", "`w6]l>I[/>\"54}=", "En service")]
    [InlineData("S352Q3", "`O(Tm53[/>Portable^", "Retour")]
    [InlineData("3YTTSU", "`pWFnU&>/>\"6[}D75&", "Engagé")]
    [InlineData("5P3WW5", "`y[Hr!l>/]Mobile D700", "Spécial")]
    [InlineData("47QSQP", "`w=2o>\u007f>/]\"4!}&", "Priorité")]
    [InlineData("E5BQS1", "`{GKl \u001c[/`_0", "Perso 2")]
    public void Message_bits_give_the_status(string dest, string info, string status)
    {
        var p = Parse(dest, info);

        Assert.Equal(status, p.MicEStatus);
        Assert.False(p.IsEmergency);
    }

    [Fact]
    public void Emergency()
    {
        var p = Parse("451QS1", "`{GKl \u001c[/`\"Ip}SOS_0");

        Assert.True(p.IsEmergency);
        Assert.Equal(MicE.Emergency, p.MicEStatus);
        Assert.Equal("Yaesu FT3D", p.Device);
        Assert.Equal(2000, p.AltitudeM);
        Assert.Equal("SOS", p.Comment);
        Assert.Equal(45.1885, p.Latitude!.Value, 3);
    }

    [Fact]
    public void Custom_and_standard_bits_together_are_an_unknown_message()
    {
        // A (custom) + P (standard) + 1: not a valid combination.
        var p = Parse("AP1QS1", "`{GKl \u001c[/`_0");

        Assert.Equal("Inconnu", p.MicEStatus);
        Assert.False(p.IsEmergency);
    }

    [Theory]
    // dest, info, lat, lon, knots, course, altitude
    [InlineData("TX5QT0", "`x1\u001cq\"b>/]\"4:}Bonjour=", 48.856667, 2.35, 50, 270, 35)]
    [InlineData("S352Q3", "`O(Tm53[/>Portable^", -33.868833, 151.209333, 12, 123, null)]       // S, E > 110
    [InlineData("3YTTSU", "`pWFnU&>/>\"6[}D75&", 39.739167, -104.990333, 25, 310, 250)]       // W 100-109
    [InlineData("TQUR6Y", "`sAkqRvv/'TT3 tracker|3", 41.878167, -87.629833, 55, 90, null)]   // W 10-99
    [InlineData("TUUUT2", "`|P,l4+[/`\"DC}_3", 45.923667, 6.869333, 2, 15, 1500)]           // E 0-9
    [InlineData("UQSPTT", "`v__m :>/`AP510 X", 51.507333, -0.127833, 10, 30, null)]          // W 0-9
    public void Position_speed_course_and_altitude(string dest, string info,
        double lat, double lon, double knots, double course, int? altitude)
    {
        var p = Parse(dest, info);

        Assert.Equal(lat, p.Latitude!.Value, 5);
        Assert.Equal(lon, p.Longitude!.Value, 5);
        Assert.Equal(knots, p.SpeedKnots);
        Assert.Equal(course, p.CourseDeg);
        Assert.Equal(altitude, p.AltitudeM is { } a ? (int)a : null);
    }

    [Fact]
    public void Yaesu_vx8_code_ends_with_a_space_and_a_cr()
    {
        // Example from the Dire Wolf documentation.
        var p = Parse("T2QT8Y", "`bSbl!Mv/`\"4%}_ \r", src: "N1EDF-9");

        Assert.Equal("Yaesu VX-8", p.Device);
        Assert.Equal("En service", p.MicEStatus);
        Assert.Equal(42 + 14.89 / 60, p.Latitude!.Value, 5);
        Assert.Equal(-(70 + 55.70 / 60), p.Longitude!.Value, 5);
        Assert.Equal(0, p.SpeedKnots);
        Assert.Equal(149, p.CourseDeg);
        Assert.Equal(14, p.AltitudeM);
        Assert.Equal('v', p.SymbolCode);
        Assert.Null(p.Comment);
    }

    [Fact]
    public void Nul_bytes_of_the_tm_d710_are_ignored()
    {
        var p = Parse("TX5QT0", "`x1\u001cq\"b>/]\"4:}Bonjour\0=");

        Assert.Equal("Kenwood TM-D710", p.Device);
        Assert.Equal("Bonjour", p.Comment);
    }

    [Fact]
    public void Ambiguous_position_is_centred_in_its_box()
    {
        // Last two latitude digits blanked (Z, L): 48 51.__ N, and so the
        // longitude hundredths too: the middle is 48 51.50 / 002 21.50.
        var p = Parse("TXUQZL", "`x1\u001cl \u001c-/>");

        Assert.Equal(48 + 51.5 / 60, p.Latitude!.Value, 5);
        Assert.Equal(2 + 21.5 / 60, p.Longitude!.Value, 5);
        Assert.Equal("Kenwood TH-D7", p.Device);
    }

    [Theory]
    [InlineData("TXUZT0")]   // a blank before a digit
    [InlineData("TXUQTM")]   // M: not a Mic-E character
    [InlineData("TXUAT0")]   // A-K only in the first three
    [InlineData("TLLLLL")]   // more than 4 blanks
    public void Invalid_destination_gives_no_position(string dest)
    {
        var p = Parse(dest, "`x1\u001cl \u001c>/");

        Assert.False(p.HasPosition);
    }

    [Theory]
    [InlineData("`x1\u001cl \u001c[/`\"4:}Precis !W57!_0", 48 + 51.405 / 60, 2 + 21.007 / 60)]   // human readable
    [InlineData("`x1\u001cl \u001c[/`Precis !w5d!_0", 48 + 51.4022 / 60, 2 + 21.0074 / 60)]       // base 91
    public void Dao_adds_precision_and_leaves_the_comment(string info, double lat, double lon)
    {
        var p = Parse("TXUQT0", info);

        Assert.Equal(lat, p.Latitude!.Value, 6);
        Assert.Equal(lon, p.Longitude!.Value, 6);
        Assert.Equal("Precis", p.Comment);
        Assert.Equal("Yaesu FT3D", p.Device);
    }

    [Fact]
    public void Old_altitude_format_is_read_too()
    {
        var p = Parse("TXUQT0", "`x1\u001cl \u001c>/`Haut /A=001000_0");

        Assert.Equal(304.8, p.AltitudeM);
        Assert.Equal("Haut", p.Comment);
    }

    [Fact]
    public void Every_end_code_is_two_bytes()
    {
        Assert.All(MicEDevice.Codes.Keys, code => Assert.Equal(2, code.Length));
    }

    [Fact]
    public void Station_keeps_its_device_through_a_packet_that_does_not_tell()
    {
        var registry = new StationRegistry();
        var t0 = DateTimeOffset.Now;
        registry.Apply(Parse("TX5QT0", "`x1\u001cq\"b>/]\"4:}Bonjour=", src: "F4DVK-9", at: t0));
        registry.Apply(Parse("APK102", "!4851.40N/00221.00E>Beacon", src: "F4DVK-9", at: t0.AddMinutes(1)));

        var s = registry.Find("F4DVK-9")!;
        Assert.Equal("Kenwood TM-D710", s.Device);
        Assert.Null(s.MicEStatus);
        Assert.Equal("Beacon", s.Comment);
    }

    [Fact]
    public void Station_shows_an_emergency()
    {
        var registry = new StationRegistry();
        registry.Apply(Parse("451QS1", "`{GKl \u001c[/`\"Ip}SOS_0", src: "F4DVK-7"));

        var s = registry.Find("F4DVK-7")!;
        Assert.True(s.IsEmergency);
        Assert.Equal(MicE.Emergency, s.MicEStatus);
    }
}
