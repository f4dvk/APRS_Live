using System.Text;
using AprsLive.Modem.Hdlc;

namespace AprsLive.Modem.Tests;

public class Crc16X25Tests
{
    [Fact]
    public void Matches_the_reference_check_value()
    {
        // CRC-16/X-25 catalog check value for the ASCII string "123456789".
        var data = Encoding.ASCII.GetBytes("123456789");
        Assert.Equal(0x906E, Crc16X25.Compute(data));
    }

    [Fact]
    public void Append_then_check_round_trips()
    {
        byte[] data = { 0x01, 0x02, 0x03, 0x04, 0x05 };
        var withFcs = Crc16X25.Append(data);

        Assert.Equal(data.Length + 2, withFcs.Length);
        Assert.True(Crc16X25.Check(withFcs));
    }

    [Fact]
    public void Check_rejects_corrupted_data()
    {
        byte[] data = { 0xAA, 0xBB, 0xCC };
        var withFcs = Crc16X25.Append(data);
        withFcs[0] ^= 0xFF; // flip a data bit, FCS now stale

        Assert.False(Crc16X25.Check(withFcs));
    }
}
