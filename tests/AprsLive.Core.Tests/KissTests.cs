using AprsLive.Core.Kiss;

namespace AprsLive.Core.Tests;

public class KissTests
{
    [Fact]
    public void Encode_then_deframe_round_trips()
    {
        byte[] ax25 = { 0x82, 0xA0, 0xB4, 0x72, 0x60, 0x60, 0x60, 0xC0, 0xDB, 0x11, 0x22 };
        var kiss = SlipCodec.EncodeData(ax25);

        Assert.Equal(SlipCodec.Fend, kiss[0]);
        Assert.Equal(SlipCodec.Fend, kiss[^1]);
        // no bare FEND/FESC inside the body
        Assert.DoesNotContain(kiss[1..^1], b => b == SlipCodec.Fend);

        byte[]? got = null;
        var d = new KissDeframer();
        d.DataFrame += f => got = f;
        d.Feed(kiss);

        Assert.NotNull(got);
        Assert.Equal(ax25, got);
    }

    [Fact]
    public void Deframer_resyncs_after_garbage_and_handles_shared_fend()
    {
        var d = new KissDeframer();
        var frames = new List<byte[]>();
        d.DataFrame += f => frames.Add(f);

        d.Feed(new byte[] { 0x01, 0x02, 0x03 });                 // garbage, no frame open
        d.Feed(SlipCodec.EncodeData(new byte[] { 0xAA, 0xBB }));  // frame 1
        // two frames sharing a single FEND
        var f2 = SlipCodec.EncodeData(new byte[] { 0xCC });
        var f3 = SlipCodec.EncodeData(new byte[] { 0xDD, 0xEE });
        d.Feed(f2[..^1]);          // frame 2 without closing FEND
        d.Feed(f3);                // its opening FEND closes frame 2

        Assert.Equal(3, frames.Count);
        Assert.Equal(new byte[] { 0xAA, 0xBB }, frames[0]);
        Assert.Equal(new byte[] { 0xCC }, frames[1]);
        Assert.Equal(new byte[] { 0xDD, 0xEE }, frames[2]);
    }

    [Fact]
    public void Non_data_kiss_frames_are_dropped()
    {
        var d = new KissDeframer();
        int n = 0;
        d.DataFrame += _ => n++;
        d.Feed(SlipCodec.EncodeCommand(SlipCodec.CmdTxDelay, 50));
        Assert.Equal(0, n);
    }
}
