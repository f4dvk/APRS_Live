using AprsLive.Audio;
using AprsLive.Audio.Ptt;
using AprsLive.Core.Links;
using AprsLive.Modem;
using AprsLive.Modem.Afsk;

namespace AprsLive.Core.Tnc;

/// <summary>
/// An <see cref="IFrameLink"/> backed by the built-in AFSK1200 software
/// modem over a sound card instead of a hardware KISS TNC. Stitches
/// together <c>AprsLive.Audio</c> (capture/playback/PTT) and
/// <c>AprsLive.Modem</c> (the actual modulator/demodulator) — nothing above
/// this class needs to know audio is involved at all.
/// </summary>
public sealed class AudioModemLink : IFrameLink
{
    private readonly string? _inputDeviceId;
    private readonly string? _outputDeviceId;
    private readonly IPttKeyer _ptt;
    private readonly ModemSettings _settings;
    private readonly Afsk1200Demodulator _demodulator;
    private readonly Afsk1200Modulator _modulator;

    private IAudioInput? _input;
    private IAudioOutput? _output;

    public AudioModemLink(string? inputDeviceId, string? outputDeviceId, IPttKeyer ptt, ModemSettings? settings = null)
    {
        _inputDeviceId = inputDeviceId;
        _outputDeviceId = outputDeviceId;
        _ptt = ptt;
        _settings = settings ?? new ModemSettings();
        _demodulator = new Afsk1200Demodulator(_settings);
        _modulator = new Afsk1200Modulator(_settings);
        _demodulator.FrameDecoded += f => FrameReceived?.Invoke(f);
        _demodulator.CrcError += () =>
        {
            Log?.Invoke("Trame audio invalide (CRC).");
            FrameCrcError?.Invoke();
        };
    }

    public FrameLinkState State { get; private set; } = FrameLinkState.Disconnected;

    public event Action<byte[]>? FrameReceived;
    public event Action<FrameLinkState>? StateChanged;
    public event Action<string>? Log;

    /// <summary>Raised with each raw captured chunk — for a live spectrum display, not fed back into the demodulator (that already happens internally).</summary>
    public event Action<float[]>? SamplesAvailable;

    /// <summary>Raised when a frame-shaped block was found between two flags but failed destuffing/FCS — a DCD indicator should still pulse on this (a frame was detected, just a corrupt one), not only on <see cref="FrameReceived"/>.</summary>
    public event Action? FrameCrcError;

    public int SampleRate => _settings.SampleRate;

    public Task ConnectAsync(CancellationToken ct = default)
    {
        if (State is FrameLinkState.Connected or FrameLinkState.Connecting)
            return Task.CompletedTask;

        SetState(FrameLinkState.Connecting);
        Log?.Invoke("Démarrage du modem audio AFSK1200…");
        try
        {
            _input = AudioBackend.CreateInput();
            _output = AudioBackend.CreateOutput();
            _input.SamplesAvailable += OnSamplesAvailable;

            _input.Start(_inputDeviceId, _settings.SampleRate);
            _output.Start(_outputDeviceId, _settings.SampleRate);

            SetState(FrameLinkState.Connected);
            Log?.Invoke("Modem audio démarré.");
        }
        catch (Exception ex)
        {
            SetState(FrameLinkState.Faulted);
            Log?.Invoke($"Échec du démarrage audio : {ex.Message}");
            throw;
        }
        return Task.CompletedTask;
    }

    private void OnSamplesAvailable(float[] samples)
    {
        SamplesAvailable?.Invoke(samples);
        _demodulator.ProcessSamples(samples);
    }

    public async Task SendFrameAsync(ReadOnlyMemory<byte> ax25, CancellationToken ct = default)
    {
        if (_output is null || State != FrameLinkState.Connected)
            throw new InvalidOperationException("Modem audio non connecté.");

        var samples = _modulator.Modulate(ax25.ToArray());

        // Keyed for the whole burst; the watchdog inside the keyer force-unkeys
        // regardless if anything downstream goes wrong.
        using var tx = _ptt.Key();
        await Task.Delay(200, ct); // let the radio's PTT relay/VOX settle before audio starts
        await _output.PlayAsync(samples);
    }

    private void SetState(FrameLinkState s)
    {
        if (State == s) return;
        State = s;
        StateChanged?.Invoke(s);
    }

    public ValueTask DisposeAsync()
    {
        if (_input is not null)
        {
            _input.SamplesAvailable -= OnSamplesAvailable;
            _input.Dispose();
            _input = null;
        }
        _output?.Dispose();
        _output = null;
        SetState(FrameLinkState.Disconnected);
        return ValueTask.CompletedTask;
    }
}
