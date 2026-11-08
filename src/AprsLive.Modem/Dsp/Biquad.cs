namespace AprsLive.Modem.Dsp;

/// <summary>A single biquad IIR filter section (RBJ Audio EQ Cookbook formulas), Direct Form I.</summary>
public sealed class Biquad
{
    private double _b0, _b1, _b2, _a1, _a2;
    private double _x1, _x2, _y1, _y2;

    private Biquad() { }

    public static Biquad BandPass(double sampleRate, double centerHz, double q)
    {
        double w0 = 2 * Math.PI * centerHz / sampleRate;
        double alpha = Math.Sin(w0) / (2 * q);
        double cosw0 = Math.Cos(w0);

        return Normalize(
            b0: alpha, b1: 0, b2: -alpha,
            a0: 1 + alpha, a1: -2 * cosw0, a2: 1 - alpha);
    }

    public static Biquad LowPass(double sampleRate, double cutoffHz, double q)
    {
        double w0 = 2 * Math.PI * cutoffHz / sampleRate;
        double alpha = Math.Sin(w0) / (2 * q);
        double cosw0 = Math.Cos(w0);

        return Normalize(
            b0: (1 - cosw0) / 2, b1: 1 - cosw0, b2: (1 - cosw0) / 2,
            a0: 1 + alpha, a1: -2 * cosw0, a2: 1 - alpha);
    }

    private static Biquad Normalize(double b0, double b1, double b2, double a0, double a1, double a2) =>
        new()
        {
            _b0 = b0 / a0,
            _b1 = b1 / a0,
            _b2 = b2 / a0,
            _a1 = a1 / a0,
            _a2 = a2 / a0,
        };

    public double Process(double x)
    {
        double y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
        _x2 = _x1; _x1 = x;
        _y2 = _y1; _y1 = y;
        return y;
    }
}
