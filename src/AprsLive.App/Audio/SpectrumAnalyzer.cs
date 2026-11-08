namespace AprsLive.App.Audio;

/// <summary>
/// Turns a chunk of raw audio samples into a small bar-graph spectrum for
/// display — just enough to see signal/noise and eyeball tuning against the
/// AX.25/AFSK1200 mark and space tones, not a general-purpose analyzer.
/// Bar/pixel layout is fixed so the view can position the mark/space
/// frequency markers once, without re-measuring anything at bind time.
/// </summary>
public static class SpectrumAnalyzer
{
    /// <summary>FFT window size — 512 samples at 13200 Hz ≈ 38.8 ms, ~26 Hz/bin.</summary>
    public const int FftSize = 512;

    public const int BarCount = 24;

    /// <summary>Top of the displayed range — comfortably past the 2200 Hz space tone, keeping the AFSK1200 band readable.</summary>
    public const double MaxDisplayHz = 3000;

    public const double BarWidth = 5;

    public const double BarSpacing = 1;

    public const double BarMaxHeight = 28;

    private const double MinDb = -60;
    private const double MaxDb = 0;

    /// <summary>Pixel X of the marker for a given frequency, centred on the bar it falls into.</summary>
    private static double MarkerX(double hz)
    {
        int bar = (int)Math.Clamp(Math.Round(hz / MaxDisplayHz * BarCount), 0, BarCount - 1);
        return bar * (BarWidth + BarSpacing) + BarWidth / 2;
    }

    public static readonly double MarkMarkerX = MarkerX(1200);

    public static readonly double SpaceMarkerX = MarkerX(2200);

    public static readonly double TotalWidth = BarCount * (BarWidth + BarSpacing) - BarSpacing;

    /// <summary>
    /// Computes <see cref="BarCount"/> bar heights (0..<see cref="BarMaxHeight"/> px)
    /// from the most recent <see cref="FftSize"/> samples (zero-padded if
    /// fewer are available yet).
    /// </summary>
    public static double[] ComputeBars(ReadOnlySpan<float> samples, int sampleRate)
    {
        var re = new double[FftSize];
        var im = new double[FftSize];

        int n = Math.Min(samples.Length, FftSize);
        int offset = samples.Length - n;
        for (int i = 0; i < n; i++)
        {
            // Hann window — tames spectral leakage from the block edges.
            double w = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (n - 1 <= 0 ? 1 : n - 1));
            re[i] = samples[offset + i] * w;
        }

        Fft(re, im);

        double hzPerBin = (double)sampleRate / FftSize;
        int usableBins = Math.Min(FftSize / 2, (int)(MaxDisplayHz / hzPerBin) + 1);
        var bars = new double[BarCount];
        var counts = new int[BarCount];

        for (int bin = 1; bin < usableBins; bin++) // skip DC (bin 0)
        {
            double hz = bin * hzPerBin;
            int bar = (int)(hz / MaxDisplayHz * BarCount);
            if (bar < 0 || bar >= BarCount)
                continue;

            double mag = Math.Sqrt(re[bin] * re[bin] + im[bin] * im[bin]);
            bars[bar] = Math.Max(bars[bar], mag); // peak per bar reads AFSK tones more clearly than an average would
            counts[bar]++;
        }

        for (int b = 0; b < BarCount; b++)
        {
            if (counts[b] == 0)
            {
                bars[b] = 0;
                continue;
            }
            double db = 20 * Math.Log10(bars[b] + 1e-9);
            double level = (db - MinDb) / (MaxDb - MinDb);
            bars[b] = Math.Clamp(level, 0, 1) * BarMaxHeight;
        }

        return bars;
    }

    /// <summary>In-place iterative radix-2 Cooley-Tukey FFT. <paramref name="re"/>/<paramref name="im"/> length must be a power of two.</summary>
    private static void Fft(double[] re, double[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
                j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2 * Math.PI / len;
            double wRe = Math.Cos(ang), wIm = Math.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                double curRe = 1, curIm = 0;
                for (int k = 0; k < len / 2; k++)
                {
                    double uRe = re[i + k], uIm = im[i + k];
                    double vRe = re[i + k + len / 2] * curRe - im[i + k + len / 2] * curIm;
                    double vIm = re[i + k + len / 2] * curIm + im[i + k + len / 2] * curRe;

                    re[i + k] = uRe + vRe;
                    im[i + k] = uIm + vIm;
                    re[i + k + len / 2] = uRe - vRe;
                    im[i + k + len / 2] = uIm - vIm;

                    double nextRe = curRe * wRe - curIm * wIm;
                    double nextIm = curRe * wIm + curIm * wRe;
                    curRe = nextRe;
                    curIm = nextIm;
                }
            }
        }
    }
}
