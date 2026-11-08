using AprsLive.Modem.Afsk;

namespace AprsLive.Modem.Tests;

public class Afsk1200RoundTripTests
{
    private static byte[] Ax25Frame(string tnc2Info, string source = "F4DVK-9", string dest = "APLIVE") =>
        AprsLive.Core.Ax25.Ax25Builder.BuildUi(source, dest, Array.Empty<string>(), tnc2Info);

    [Theory]
    [InlineData("!4830.00N/00230.00E>Test APRS_Live")]
    [InlineData(":F4DVK-9  :Hello{01")]
    [InlineData("!0000.00N/00000.00E>")]
    public void Clean_signal_round_trips_byte_exact(string info)
    {
        var original = Ax25Frame(info);
        var settings = new ModemSettings();

        var samples = new Afsk1200Modulator(settings).Modulate(original);

        byte[]? decoded = null;
        var demod = new Afsk1200Demodulator(settings);
        demod.FrameDecoded += f => decoded = f;
        demod.ProcessSamples(samples);

        Assert.NotNull(decoded);
        Assert.Equal(original, decoded);
    }

    [Fact]
    public void Survives_moderate_additive_noise()
    {
        var original = Ax25Frame("!4830.00N/00230.00E>Test avec un peu de bruit");
        var settings = new ModemSettings();
        var samples = new Afsk1200Modulator(settings).Modulate(original);

        var noisy = AddNoise(samples, snrDb: 15, seed: 1);

        byte[]? decoded = null;
        var demod = new Afsk1200Demodulator(settings);
        demod.FrameDecoded += f => decoded = f;
        demod.ProcessSamples(noisy);

        Assert.NotNull(decoded);
        Assert.Equal(original, decoded);
    }

    [Fact]
    public void Decode_rate_stays_high_across_an_SNR_sweep()
    {
        var settings = new ModemSettings();
        var original = Ax25Frame("!4830.00N/00230.00E>SNR sweep");
        var samples = new Afsk1200Modulator(settings).Modulate(original);

        foreach (double snrDb in new[] { 20.0, 15.0, 10.0 })
        {
            int decoded = 0;
            const int trials = 10;
            for (int seed = 0; seed < trials; seed++)
            {
                var noisy = AddNoise(samples, snrDb, seed);
                byte[]? got = null;
                var demod = new Afsk1200Demodulator(settings);
                demod.FrameDecoded += f => got = f;
                demod.ProcessSamples(noisy);
                if (got is not null && got.AsSpan().SequenceEqual(original)) decoded++;
            }

            double rate = decoded / (double)trials;
            // Loose thresholds for a v1 single-slicer demod — this is a
            // regression net (catches "it stopped decoding anything"), not a
            // performance claim.
            double minRate = snrDb >= 15 ? 0.9 : 0.5;
            Assert.True(rate >= minRate, $"decode rate at {snrDb} dB was {rate:P0}, expected >= {minRate:P0}");
        }
    }

    [Fact]
    public void Tolerates_a_couple_percent_of_sample_clock_error()
    {
        var settings = new ModemSettings();
        var original = Ax25Frame("!4830.00N/00230.00E>Clock drift");
        var samples = new Afsk1200Modulator(settings).Modulate(original);

        foreach (double drift in new[] { -0.02, 0.02 })
        {
            var resampled = ResampleLinear(samples, 1.0 + drift);

            byte[]? decoded = null;
            var demod = new Afsk1200Demodulator(settings);
            demod.FrameDecoded += f => decoded = f;
            demod.ProcessSamples(resampled);

            Assert.NotNull(decoded);
            Assert.Equal(original, decoded);
        }
    }

    private static float[] AddNoise(float[] samples, double snrDb, int seed)
    {
        double signalPower = 0;
        foreach (float s in samples) signalPower += s * s;
        signalPower /= samples.Length;

        double noisePower = signalPower / Math.Pow(10, snrDb / 10);
        double noiseStdDev = Math.Sqrt(noisePower);

        var rng = new Random(seed);
        var result = new float[samples.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            // Box-Muller for approximately Gaussian noise
            double u1 = 1.0 - rng.NextDouble();
            double u2 = rng.NextDouble();
            double gaussian = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            result[i] = (float)(samples[i] + gaussian * noiseStdDev);
        }
        return result;
    }

    /// <summary>Simulates a sender/receiver sample-clock mismatch: stretches or compresses the sample sequence by <paramref name="rateFactor"/> via linear interpolation.</summary>
    private static float[] ResampleLinear(float[] samples, double rateFactor)
    {
        int outLength = (int)(samples.Length / rateFactor);
        var result = new float[outLength];
        for (int i = 0; i < outLength; i++)
        {
            double srcPos = i * rateFactor;
            int i0 = (int)srcPos;
            int i1 = Math.Min(i0 + 1, samples.Length - 1);
            double frac = srcPos - i0;
            result[i] = (float)(samples[i0] * (1 - frac) + samples[i1] * frac);
        }
        return result;
    }
}
