using AprsLive.Modem.Hdlc;

namespace AprsLive.Modem.Afsk;

/// <summary>Encodes an AX.25 frame (no FCS) into AFSK1200 audio samples: HDLC framing + bit stuffing, NRZI, continuous-phase FSK.</summary>
public sealed class Afsk1200Modulator
{
    private readonly ModemSettings _settings;

    public Afsk1200Modulator(ModemSettings? settings = null) => _settings = settings ?? new ModemSettings();

    public float[] Modulate(byte[] ax25Frame)
    {
        var bits = HdlcFramer.BuildBitStream(ax25Frame, _settings.TxDelayFlags, _settings.TxTailFlags);
        var levels = NrziCodec.Encode(bits);
        return new ToneGenerator(_settings).Generate(levels);
    }
}
