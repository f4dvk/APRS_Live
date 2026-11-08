namespace AprsLive.Modem.Afsk;

/// <summary>
/// Non-coherent dual correlator FSK discriminator: for each sample, a
/// sliding one-bit-period correlation against the mark and space tones;
/// soft output = |mark correlation| - |space correlation| (positive leans
/// mark, negative leans space). No carrier phase lock needed. The window is
/// only 11 samples (at 13200 Hz/1200 baud) so a brute-force recompute each
/// sample is cheap and avoids any incremental-sliding-DFT bookkeeping.
/// </summary>
public sealed class Fsk1200Discriminator
{
    private readonly int _window;
    private readonly double[] _markCos, _markSin, _spaceCos, _spaceSin;
    private readonly double[] _buffer;
    private int _writePos;

    public Fsk1200Discriminator(ModemSettings settings)
    {
        _window = settings.SamplesPerBit;
        _markCos = new double[_window];
        _markSin = new double[_window];
        _spaceCos = new double[_window];
        _spaceSin = new double[_window];
        _buffer = new double[_window];

        for (int k = 0; k < _window; k++)
        {
            _markCos[k] = Math.Cos(2 * Math.PI * settings.MarkHz * k / settings.SampleRate);
            _markSin[k] = Math.Sin(2 * Math.PI * settings.MarkHz * k / settings.SampleRate);
            _spaceCos[k] = Math.Cos(2 * Math.PI * settings.SpaceHz * k / settings.SampleRate);
            _spaceSin[k] = Math.Sin(2 * Math.PI * settings.SpaceHz * k / settings.SampleRate);
        }
    }

    public double Process(double sample)
    {
        _buffer[_writePos] = sample;
        _writePos = (_writePos + 1) % _window;

        double markI = 0, markQ = 0, spaceI = 0, spaceQ = 0;
        int idx = _writePos; // oldest sample in the buffer right now
        for (int k = 0; k < _window; k++)
        {
            double s = _buffer[idx];
            markI += s * _markCos[k];
            markQ += s * _markSin[k];
            spaceI += s * _spaceCos[k];
            spaceQ += s * _spaceSin[k];
            idx++;
            if (idx == _window) idx = 0;
        }

        double markMag = Math.Sqrt(markI * markI + markQ * markQ);
        double spaceMag = Math.Sqrt(spaceI * spaceI + spaceQ * spaceQ);
        return markMag - spaceMag;
    }
}
