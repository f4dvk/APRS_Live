using AprsLive.Modem.Hdlc;

namespace AprsLive.Modem.Tests;

public class HdlcRoundTripTests
{
    [Theory]
    [InlineData(new byte[] { 0x01, 0x02, 0x03 })]
    [InlineData(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF })] // forces bit stuffing throughout
    [InlineData(new byte[] { 0x00 })]
    [InlineData(new byte[] { 0x7E, 0x7E })] // payload byte happens to equal the flag byte
    public void Frame_then_deframe_recovers_the_original_bytes(byte[] payload)
    {
        var bits = HdlcFramer.BuildBitStream(payload, txDelayFlags: 2, txTailFlags: 2);

        byte[]? got = null;
        var deframer = new HdlcDeframer();
        deframer.FrameDecoded += f => got = f;
        deframer.CrcError += () => Assert.Fail("unexpected CRC error");

        foreach (bool bit in bits)
            deframer.ProcessBit(bit);

        Assert.NotNull(got);
        Assert.Equal(payload, got);
    }

    [Fact]
    public void Back_to_back_frames_sharing_flags_both_decode()
    {
        var bits1 = HdlcFramer.BuildBitStream(new byte[] { 0xAA, 0xBB }, txDelayFlags: 1, txTailFlags: 1);
        var bits2 = HdlcFramer.BuildBitStream(new byte[] { 0xCC, 0xDD, 0xEE }, txDelayFlags: 1, txTailFlags: 1);

        var frames = new List<byte[]>();
        var deframer = new HdlcDeframer();
        deframer.FrameDecoded += f => frames.Add(f);

        foreach (bool bit in bits1) deframer.ProcessBit(bit);
        foreach (bool bit in bits2) deframer.ProcessBit(bit);

        Assert.Equal(2, frames.Count);
        Assert.Equal(new byte[] { 0xAA, 0xBB }, frames[0]);
        Assert.Equal(new byte[] { 0xCC, 0xDD, 0xEE }, frames[1]);
    }

    [Fact]
    public void Corrupted_frame_data_raises_CrcError_not_FrameDecoded()
    {
        var bits = HdlcFramer.BuildBitStream(new byte[] { 0x01, 0x02, 0x03 }, txDelayFlags: 1, txTailFlags: 1);
        bits[10] = !bits[10]; // flip a bit well inside the stuffed frame data

        bool crcError = false;
        var deframer = new HdlcDeframer();
        deframer.FrameDecoded += _ => Assert.Fail("expected the corrupted frame to fail its CRC");
        deframer.CrcError += () => crcError = true;

        foreach (bool bit in bits)
            deframer.ProcessBit(bit);

        Assert.True(crcError);
    }

    [Fact]
    public void Stuffing_never_leaves_six_consecutive_ones()
    {
        var bits = new List<bool>();
        for (int i = 0; i < 40; i++) bits.Add(true); // pathological: all ones

        var stuffed = BitStuffer.Stuff(bits);

        int run = 0;
        foreach (bool bit in stuffed)
        {
            run = bit ? run + 1 : 0;
            Assert.True(run < 6);
        }
    }
}
