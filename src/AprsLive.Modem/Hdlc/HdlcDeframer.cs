namespace AprsLive.Modem.Hdlc;

/// <summary>
/// Bit-at-a-time HDLC receiver: hunts for 0x7E flags, collects the raw
/// (still-stuffed) bits between consecutive flags, and on each closing flag
/// destuffs, assembles bytes and checks the FCS.
///
/// The flag pattern is only recognizable once its 8th bit has arrived, but
/// its first 7 bits look like ordinary data up to that point and get
/// appended to the in-progress span before the match is seen — so on every
/// flag, the last 7 bits of the span are stripped before it's treated as
/// frame data. This is the standard way to resolve that ambiguity: a
/// correctly bit-stuffed sender can never produce 6 consecutive raw 1-bits
/// outside a flag/abort, so those trailing bits can only ever belong to the
/// flag just recognized, never to real data.
/// </summary>
public sealed class HdlcDeframer
{
    private const byte Flag = 0x7E;

    private byte _shiftReg;
    private readonly List<bool> _rawSinceFlag = new();
    private bool _sawFlag;
    private bool _aborted;
    private int _onesInARow;

    /// <summary>Raised with the AX.25 frame bytes (FCS stripped, already verified) of each valid frame.</summary>
    public event Action<byte[]>? FrameDecoded;

    /// <summary>Raised once per frame that failed to destuff or check FCS (diagnostics only).</summary>
    public event Action? CrcError;

    public void ProcessBit(bool bit)
    {
        _shiftReg = (byte)((_shiftReg >> 1) | (bit ? 0x80 : 0));

        if (_shiftReg == Flag)
        {
            int strip = Math.Min(7, _rawSinceFlag.Count);
            if (strip > 0)
                _rawSinceFlag.RemoveRange(_rawSinceFlag.Count - strip, strip);

            if (_sawFlag && !_aborted)
                TryEmitFrame();

            _rawSinceFlag.Clear();
            _sawFlag = true;
            _aborted = false;
            _onesInARow = 0;
            return;
        }

        if (!_sawFlag || _aborted)
            return;

        if (bit)
        {
            _onesInARow++;
            if (_onesInARow >= 7)
            {
                _aborted = true;
                _rawSinceFlag.Clear();
                return;
            }
        }
        else
        {
            _onesInARow = 0;
        }

        _rawSinceFlag.Add(bit);
    }

    private void TryEmitFrame()
    {
        if (_rawSinceFlag.Count == 0)
            return;

        var destuffed = BitStuffer.Unstuff(_rawSinceFlag);
        if (destuffed is null || destuffed.Count == 0 || destuffed.Count % 8 != 0)
        {
            CrcError?.Invoke();
            return;
        }

        int byteCount = destuffed.Count / 8;
        if (byteCount < 2) // need at least the 2-byte FCS
        {
            CrcError?.Invoke();
            return;
        }

        var bytes = new byte[byteCount];
        for (int i = 0; i < byteCount; i++)
        {
            byte b = 0;
            for (int bitIdx = 0; bitIdx < 8; bitIdx++)
                if (destuffed[i * 8 + bitIdx]) b |= (byte)(1 << bitIdx);
            bytes[i] = b;
        }

        if (!Crc16X25.Check(bytes))
        {
            CrcError?.Invoke();
            return;
        }

        FrameDecoded?.Invoke(bytes[..^2]);
    }
}
