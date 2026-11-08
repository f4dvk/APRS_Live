namespace AprsLive.Core.Kiss;

/// <summary>
/// KISS framing over SLIP (RFC 1055). A KISS frame is
/// <c>FEND, &lt;type/port byte&gt;, &lt;SLIP-escaped payload&gt;, FEND</c>.
/// The low nibble of the type byte is the command (0 = data), the high nibble
/// is the TNC port. Only data frames carry an AX.25 frame.
/// </summary>
public static class SlipCodec
{
    public const byte Fend = 0xC0;
    public const byte Fesc = 0xDB;
    public const byte TFend = 0xDC;
    public const byte TFesc = 0xDD;

    public const byte CmdData = 0x00;
    public const byte CmdTxDelay = 0x01;
    public const byte CmdP = 0x02;
    public const byte CmdSlotTime = 0x03;
    public const byte CmdTxTail = 0x04;
    public const byte CmdFullDuplex = 0x05;
    public const byte CmdSetHardware = 0x06;
    public const byte CmdReturn = 0xFF;

    /// <summary>Wrap a raw AX.25 frame as a KISS data frame on the given port.</summary>
    public static byte[] EncodeData(ReadOnlySpan<byte> ax25, int port = 0)
    {
        var outBuf = new List<byte>(ax25.Length + 4) { Fend, (byte)((port << 4) | CmdData) };
        foreach (var b in ax25)
        {
            switch (b)
            {
                case Fend: outBuf.Add(Fesc); outBuf.Add(TFend); break;
                case Fesc: outBuf.Add(Fesc); outBuf.Add(TFesc); break;
                default: outBuf.Add(b); break;
            }
        }
        outBuf.Add(Fend);
        return outBuf.ToArray();
    }

    /// <summary>Encode a simple 1-byte KISS command (TXDELAY, P, ... ).</summary>
    public static byte[] EncodeCommand(byte command, byte value, int port = 0) =>
        new[] { Fend, (byte)((port << 4) | (command & 0x0F)), value, Fend };
}

/// <summary>
/// Incremental SLIP/KISS de-framer. Feed bytes as they arrive; it raises
/// <see cref="DataFrame"/> for every complete KISS <b>data</b> frame
/// (command nibble 0), with the AX.25 bytes only (type byte stripped,
/// escapes resolved). Non-data frames and empty frames are dropped.
/// </summary>
public sealed class KissDeframer
{
    private readonly List<byte> _buffer = new(512);
    private bool _inFrame;
    private bool _escape;
    private bool _haveType;
    private bool _isData;

    /// <summary>Raised with the decoded AX.25 payload of each KISS data frame.</summary>
    public event Action<byte[]>? DataFrame;

    public void Feed(ReadOnlySpan<byte> bytes)
    {
        foreach (var b in bytes)
            Feed(b);
    }

    public void Feed(byte b)
    {
        if (b == SlipCodec.Fend)
        {
            if (_inFrame && _haveType && _isData && _buffer.Count > 0)
                DataFrame?.Invoke(_buffer.ToArray());
            Reset();
            _inFrame = true;
            return;
        }

        if (!_inFrame)
            return;

        if (_escape)
        {
            _escape = false;
            b = b switch
            {
                SlipCodec.TFend => SlipCodec.Fend,
                SlipCodec.TFesc => SlipCodec.Fesc,
                _ => b // lenient: keep an unknown escaped byte verbatim
            };
        }
        else if (b == SlipCodec.Fesc)
        {
            _escape = true;
            return;
        }

        if (!_haveType)
        {
            _haveType = true;
            _isData = (b & 0x0F) == SlipCodec.CmdData;
            return;
        }

        _buffer.Add(b);
    }

    private void Reset()
    {
        _buffer.Clear();
        _inFrame = false;
        _escape = false;
        _haveType = false;
        _isData = false;
    }
}
