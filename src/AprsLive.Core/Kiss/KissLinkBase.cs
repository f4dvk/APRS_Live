using AprsLive.Core.Links;

namespace AprsLive.Core.Kiss;

/// <summary>
/// Shared plumbing for the KISS transports: the de-framer, the state machine,
/// a background read pump, and re-raising events. Subclasses only open/close the
/// underlying stream and provide its <see cref="Stream"/>.
/// </summary>
public abstract class KissLinkBase : IFrameLink
{
    private readonly KissDeframer _deframer = new();
    private CancellationTokenSource? _pumpCts;
    private Task? _pumpTask;

    protected KissLinkBase()
    {
        _deframer.DataFrame += f => FrameReceived?.Invoke(f);
    }

    public FrameLinkState State { get; private set; } = FrameLinkState.Disconnected;

    public event Action<byte[]>? FrameReceived;
    public event Action<FrameLinkState>? StateChanged;
    public event Action<string>? Log;

    protected void RaiseLog(string line) => Log?.Invoke(line);

    protected void SetState(FrameLinkState s)
    {
        if (State == s) return;
        State = s;
        StateChanged?.Invoke(s);
    }

    /// <summary>Open the underlying transport and return its duplex stream.</summary>
    protected abstract Task<Stream> OpenAsync(CancellationToken ct);

    protected abstract string Describe();

    protected Stream? Transport { get; private set; }

    /// <summary>Frees what <see cref="OpenAsync"/> opened besides the stream (the serial port, the TCP client).</summary>
    protected virtual void ReleaseTransport() { }

    private void CloseTransport()
    {
        var transport = Transport;
        Transport = null;
        try { transport?.Dispose(); } catch { /* best effort, the device may be gone */ }
        try { ReleaseTransport(); } catch { /* best effort */ }
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (State is FrameLinkState.Connected or FrameLinkState.Connecting)
            return;

        SetState(FrameLinkState.Connecting);
        RaiseLog($"Connexion à {Describe()}…");
        try
        {
            Transport = await OpenAsync(ct).ConfigureAwait(false);
            // Connected before the pump starts: a peer closing at once must end in Disconnected, not the reverse.
            SetState(FrameLinkState.Connected);
            RaiseLog($"Connecté à {Describe()}.");
            _pumpCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _pumpTask = Task.Run(() => ReadPumpAsync(_pumpCts.Token));
        }
        catch (Exception ex)
        {
            CloseTransport();
            SetState(FrameLinkState.Faulted);
            RaiseLog($"Échec de connexion : {ex.Message}");
            throw;
        }
    }

    private async Task ReadPumpAsync(CancellationToken ct)
    {
        var buf = new byte[4096];
        try
        {
            while (!ct.IsCancellationRequested && Transport is not null)
            {
                int n = await Transport.ReadAsync(buf.AsMemory(), ct).ConfigureAwait(false);
                if (n <= 0)
                {
                    RaiseLog("Flux fermé par le pair.");
                    break;
                }
                _deframer.Feed(buf.AsSpan(0, n));
            }
        }
        catch (OperationCanceledException) { /* normal on disconnect */ }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            // Disconnect closed the port under a pending read: normal too.
        }
        catch (Exception ex)
        {
            RaiseLog($"Erreur de lecture : {ex.Message}");
        }
        finally
        {
            // Lost on its own (TNC unplugged, TCP peer gone): close now, so a
            // USB TNC plugged back in gets its old name (/dev/ttyACM0) again.
            if (!ct.IsCancellationRequested)
                CloseTransport();
            SetState(FrameLinkState.Disconnected);
        }
    }

    public async Task SendFrameAsync(ReadOnlyMemory<byte> ax25, CancellationToken ct = default)
    {
        var transport = Transport;   // the read pump may close it at any time
        if (transport is null || State != FrameLinkState.Connected)
            throw new InvalidOperationException("Lien KISS non connecté.");
        var kiss = SlipCodec.EncodeData(ax25.Span);
        await transport.WriteAsync(kiss, ct).ConfigureAwait(false);
        await transport.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>How long <see cref="DisposeAsync"/> waits for the read pump once the port is closed.</summary>
    private static readonly TimeSpan PumpStopTimeout = TimeSpan.FromSeconds(2);

    public async ValueTask DisposeAsync()
    {
        try { _pumpCts?.Cancel(); } catch { /* ignore */ }
        // Close first: on Windows a pending serial read ignores the
        // cancellation and would only end with the TNC's next byte, which
        // left Disconnect hanging. Closing the port aborts the read.
        CloseTransport();
        bool pumpStopped = true;
        if (_pumpTask is not null)
        {
            try { await _pumpTask.WaitAsync(PumpStopTimeout).ConfigureAwait(false); }
            catch (TimeoutException) { pumpStopped = false; }
            catch { /* ended with an error: stopped all the same */ }
        }
        if (pumpStopped)
            _pumpCts?.Dispose();   // a pump still running keeps using its token
        SetState(FrameLinkState.Disconnected);
        GC.SuppressFinalize(this);
    }
}
