namespace AprsLive.Modem.Afsk;

/// <summary>
/// 32-bit phase-accumulator bit clock: advances by one bit period's worth of
/// phase per sample; the current sliced value is sampled as the bit
/// whenever the accumulator wraps — that wrap point is defined as the
/// bit's <em>centre</em>, as far as possible from any transition on either
/// side. A true data transition can only happen at a bit <em>boundary</em>,
/// which is therefore exactly half a bit period away from the two
/// surrounding centre-samples — i.e. at phase ≈ <see cref="uint.MaxValue"/>/2.
/// On every observed transition of the sliced signal, the accumulator is
/// pulled toward that half-way point by a fixed fraction of its current
/// error — tolerant of a few percent of sample-clock drift without any
/// elaborate timing-error detector.
/// </summary>
public sealed class BitTimingPll
{
    private const double PullFraction = 0.6;
    private const uint EdgePhase = uint.MaxValue / 2;

    private readonly uint _increment;
    private uint _phase;
    private bool _lastLevel;
    private bool _hasLast;

    public BitTimingPll(ModemSettings settings)
    {
        _increment = (uint)((ulong)uint.MaxValue * (uint)settings.BaudRate / (uint)settings.SampleRate);
    }

    /// <summary>Feed one sample's sliced line level; invokes <paramref name="onBit"/> with the level sampled at each bit-centre crossing.</summary>
    public void Process(bool level, Action<bool> onBit)
    {
        if (_hasLast && level != _lastLevel)
        {
            long error = (long)_phase - EdgePhase;
            _phase = unchecked((uint)(_phase - (long)(error * PullFraction)));
        }
        _lastLevel = level;
        _hasLast = true;

        uint before = _phase;
        _phase = unchecked(_phase + _increment);
        if (_phase < before)
            onBit(level);
    }
}
