namespace AprsLive.Modem;

/// <summary>Tunable parameters for the AFSK1200 (Bell 202) modulator and demodulator.</summary>
public sealed class ModemSettings
{
    /// <summary>Internal processing rate: 13200 Hz = exactly 11 samples/bit at 1200 baud, chosen so every later stage works in whole samples.</summary>
    public int SampleRate { get; init; } = 13200;

    public int BaudRate { get; init; } = 1200;

    public double MarkHz { get; init; } = 1200;

    public double SpaceHz { get; init; } = 2200;

    /// <summary>Preamble length before the frame, in 0x7E flag bytes (~1 flag = 6.7 ms at 1200 baud).</summary>
    public int TxDelayFlags { get; init; } = 30;

    /// <summary>Trailing flags after the frame's closing flag.</summary>
    public int TxTailFlags { get; init; } = 3;

    public float TxAmplitude { get; init; } = 0.8f;

    public int SamplesPerBit => SampleRate / BaudRate;
}
