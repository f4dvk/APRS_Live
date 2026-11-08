using AprsLive.Modem.Dsp;
using AprsLive.Modem.Hdlc;

namespace AprsLive.Modem.Afsk;

/// <summary>
/// AFSK1200 demodulator pipeline: band-pass filter -> dual-correlator FSK
/// discriminator -> low-pass smoothing -> zero-threshold slicer -> PLL bit
/// clock -> NRZI decode -> HDLC deframe/CRC. Expects samples already at
/// <see cref="ModemSettings.SampleRate"/> (13200 Hz by default) —
/// resampling from whatever rate a sound card actually delivers is the
/// audio I/O layer's job, not this one's.
/// </summary>
/// <remarks>
/// The slicer threshold is fixed at zero rather than adaptively tracked.
/// An earlier attack/decay peak tracker seemed like the more robust choice
/// (real captured audio can carry a genuine DC/gain bias), but it actively
/// broke decoding: HDLC flags are extremely duty-cycle-skewed under NRZI
/// (one mark bit per seven space bits), and any tracker that adapts within
/// a timescale of a few bits gets pulled off-centre by that skew rather
/// than by real hardware bias, corrupting exactly the bit stream a receiver
/// most depends on for frame sync. Since the discriminator's mark/space
/// correlators are symmetric, zero is the correct decision boundary as long
/// as the front end doesn't introduce its own DC offset — real-world DC
/// compensation (tuned against actual captured audio, not synthetic
/// signals) is deferred to the hardening pass once that audio exists.
/// </remarks>
public sealed class Afsk1200Demodulator
{
    private readonly Biquad _bandPass;
    private readonly Biquad _lowPass;
    private readonly Fsk1200Discriminator _discriminator;
    private readonly BitTimingPll _pll;
    private readonly HdlcDeframer _deframer;

    private bool _nrziPrevLevel;
    private bool _nrziHasPrev;

    public Afsk1200Demodulator(ModemSettings? settings = null)
    {
        settings ??= new ModemSettings();
        _bandPass = Biquad.BandPass(settings.SampleRate, (settings.MarkHz + settings.SpaceHz) / 2, q: 0.7);
        _lowPass = Biquad.LowPass(settings.SampleRate, settings.BaudRate * 0.6, q: 0.7);
        _discriminator = new Fsk1200Discriminator(settings);
        _pll = new BitTimingPll(settings);
        _deframer = new HdlcDeframer();
        _deframer.FrameDecoded += f => FrameDecoded?.Invoke(f);
        _deframer.CrcError += () => CrcError?.Invoke();
    }

    /// <summary>Raised with the AX.25 frame bytes (FCS stripped, already CRC-verified) of each valid frame decoded.</summary>
    public event Action<byte[]>? FrameDecoded;

    /// <summary>Raised once per frame candidate that failed destuffing or the FCS check.</summary>
    public event Action? CrcError;

    public void ProcessSamples(ReadOnlySpan<float> samples)
    {
        foreach (float sample in samples)
            ProcessSample(sample);
    }

    private void ProcessSample(float sample)
    {
        double bandPassed = _bandPass.Process(sample);
        double discriminated = _discriminator.Process(bandPassed);
        double filtered = _lowPass.Process(discriminated);

        bool level = filtered > 0; // true = mark, matching Fsk1200Discriminator's sign convention

        _pll.Process(level, slicedLevel =>
        {
            bool bit = NrziCodec.DecodeNext(slicedLevel, ref _nrziPrevLevel, ref _nrziHasPrev);
            _deframer.ProcessBit(bit);
        });
    }
}
