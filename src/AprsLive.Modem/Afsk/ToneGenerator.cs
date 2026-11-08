namespace AprsLive.Modem.Afsk;

/// <summary>Generates continuous-phase FSK samples for a sequence of NRZI line levels (true=mark, false=space), one bit period each — phase carries across bit boundaries, so there are no clicks at tone changes.</summary>
public sealed class ToneGenerator
{
    private readonly ModemSettings _settings;
    private double _phase;

    public ToneGenerator(ModemSettings settings) => _settings = settings;

    public float[] Generate(IReadOnlyList<bool> levels)
    {
        int samplesPerBit = _settings.SamplesPerBit;
        var samples = new float[levels.Count * samplesPerBit];
        int idx = 0;
        foreach (bool level in levels)
        {
            double freq = level ? _settings.MarkHz : _settings.SpaceHz;
            double increment = 2 * Math.PI * freq / _settings.SampleRate;
            for (int i = 0; i < samplesPerBit; i++)
            {
                samples[idx++] = (float)(_settings.TxAmplitude * Math.Sin(_phase));
                _phase += increment;
                if (_phase > 2 * Math.PI) _phase -= 2 * Math.PI;
            }
        }
        return samples;
    }
}
