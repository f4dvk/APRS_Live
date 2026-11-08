using System.Globalization;
using System.Net.Sockets;
using System.Text;
using AprsLive.Core.Ax25;
using AprsLive.Core.Links;

namespace AprsLive.Core.Tnc;

/// <summary>
/// An <see cref="IFrameLink"/> backed by an APRS-IS server instead of a KISS
/// TNC — a text line protocol over TCP (login line, then one TNC2-format
/// line per packet), not AX.25/KISS framing. Every text line is exactly
/// <c>SRC&gt;DST,PATH:info</c>, so each one converts to/from the same
/// AX.25 bytes the rest of the app already works with via the existing
/// <see cref="Ax25Builder.BuildUi"/> and <see cref="Ax25Frame.ToTnc2"/> —
/// no bespoke encoding needed here, just the login/failover/line-reading
/// mechanics specific to APRS-IS.
/// </summary>
public sealed class AprsIsClient : IFrameLink
{
    private readonly string _callsign;
    private readonly string _passcode;
    private readonly string _preferredServer;
    private readonly int _port;
    private readonly string? _filter;

    private TcpClient? _client;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private CancellationTokenSource? _readLoopCts;
    private Task? _readLoopTask;

    public AprsIsClient(string callsign, string passcode, string preferredServer, int port, string? filter)
    {
        _callsign = callsign;
        _passcode = passcode;
        _preferredServer = preferredServer;
        _port = port;
        _filter = filter;
    }

    public FrameLinkState State { get; private set; } = FrameLinkState.Disconnected;

    /// <summary>
    /// Servers send a <c>#</c> keepalive line about every 20 s, so this long
    /// without any line means the link died silently (router reboot, lost
    /// Wi-Fi: no FIN/RST ever arrives) — the read loop then gives up.
    /// </summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds(90);

    public event Action<byte[]>? FrameReceived;
    public event Action<FrameLinkState>? StateChanged;
    public event Action<string>? Log;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (State is FrameLinkState.Connected or FrameLinkState.Connecting)
            return;

        SetState(FrameLinkState.Connecting);

        foreach (var server in ServerAttemptOrder())
        {
            try
            {
                await ConnectToAsync(server, ct);
                SetState(FrameLinkState.Connected);
                _readLoopCts = new CancellationTokenSource();
                _readLoopTask = Task.Run(() => ReadLoopAsync(_readLoopCts.Token));
                return;
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Échec sur {server} : {ex.Message} — essai du serveur suivant…");
                CleanupSocket();
            }
        }

        SetState(FrameLinkState.Faulted);
        throw new InvalidOperationException("Aucun serveur APRS-IS n'a répondu.");
    }

    private IEnumerable<string> ServerAttemptOrder()
    {
        yield return _preferredServer;
        foreach (var s in AprsIsServers.Known)
            if (!string.Equals(s, _preferredServer, StringComparison.OrdinalIgnoreCase))
                yield return s;
    }

    private async Task ConnectToAsync(string server, CancellationToken ct)
    {
        Log?.Invoke($"Connexion à {server}:{_port}…");
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

        _client = new TcpClient();
        await _client.ConnectAsync(server, _port, timeoutCts.Token);

        var stream = _client.GetStream();
        _reader = new StreamReader(stream, Encoding.Latin1);
        _writer = new StreamWriter(stream, Encoding.Latin1) { NewLine = "\r\n", AutoFlush = true };

        string filterClause = string.IsNullOrWhiteSpace(_filter) ? "" : $" filter {_filter}";
        await _writer.WriteLineAsync($"user {_callsign} pass {_passcode} vers APRS_Live 0.1{filterClause}");

        var readTask = _reader.ReadLineAsync(timeoutCts.Token).AsTask();
        var completed = await Task.WhenAny(readTask, Task.Delay(Timeout.Infinite, timeoutCts.Token))
            .ConfigureAwait(false);
        if (completed != readTask || readTask.Result is not { } response)
            throw new IOException("Pas de réponse du serveur.");

        Log?.Invoke(response);
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            while (!ct.IsCancellationRequested && _reader is not null)
            {
                idle.CancelAfter(IdleTimeout);   // restarts the countdown for each line
                string? line;
                try
                {
                    line = await _reader.ReadLineAsync(idle.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    Log?.Invoke($"Aucune donnée du serveur depuis {IdleTimeout.TotalSeconds:0} s : connexion considérée comme perdue.");
                    break;
                }
                if (line is null)
                {
                    Log?.Invoke("Connexion APRS-IS fermée par le serveur.");
                    break;
                }

                if (line.Length == 0) continue;

                if (line[0] == '#')
                {
                    Log?.Invoke(line);
                    continue;
                }

                if (TryConvertTnc2ToAx25(line, out var ax25))
                    FrameReceived?.Invoke(ax25);
            }
        }
        catch (OperationCanceledException) { /* normal on disconnect */ }
        catch (Exception ex)
        {
            Log?.Invoke($"Erreur de lecture APRS-IS : {ex.Message}");
        }
        finally
        {
            if (!ct.IsCancellationRequested)
                CleanupSocket();   // drop a half-open socket now; DisposeAsync does it otherwise
            SetState(FrameLinkState.Disconnected);
        }
    }

    private static bool TryConvertTnc2ToAx25(string line, out byte[] ax25)
    {
        ax25 = [];
        int gt = line.IndexOf('>');
        if (gt < 0) return false;
        int colon = line.IndexOf(':', gt);
        if (colon < 0) return false;

        string source = line[..gt];
        var rest = line[(gt + 1)..colon].Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (rest.Length == 0) return false;
        string destination = rest[0];
        var path = rest.Length > 1 ? rest[1..] : [];
        string info = line[(colon + 1)..];

        try
        {
            ax25 = Ax25Builder.BuildUi(source, destination, path, info);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Sends a frame as a TNC2 text line — only meaningful with a real (non "-1") passcode.</summary>
    public async Task SendFrameAsync(ReadOnlyMemory<byte> ax25, CancellationToken ct = default)
    {
        var writer = _writer;   // the read loop may drop the socket at any time
        if (writer is null || State != FrameLinkState.Connected)
            throw new InvalidOperationException("APRS-IS non connecté.");
        if (!Ax25Frame.TryParse(ax25.Span, out var frame) || frame is null)
            throw new ArgumentException("Trame AX.25 invalide.", nameof(ax25));

        await writer.WriteLineAsync(frame.ToTnc2().AsMemory(), ct);
    }

    /// <summary>Sends a ready-made TNC2 line as is — for gated RF traffic, whose q construct can't be carried in an AX.25 frame.</summary>
    public async Task SendLineAsync(string tnc2Line, CancellationToken ct = default)
    {
        var writer = _writer;
        if (writer is null || State != FrameLinkState.Connected)
            throw new InvalidOperationException("APRS-IS non connecté.");

        await writer.WriteLineAsync(tnc2Line.AsMemory(), ct);
    }

    /// <summary>Builds the APRS-IS range filter clause for a centre point and radius, e.g. <c>r/49.66/3.31/100</c>.</summary>
    public static string BuildRangeFilter(double lat, double lon, int radiusKm) =>
        string.Create(CultureInfo.InvariantCulture, $"r/{lat:0.###}/{lon:0.###}/{radiusKm}");

    private void SetState(FrameLinkState s)
    {
        if (State == s) return;
        State = s;
        StateChanged?.Invoke(s);
    }

    private void CleanupSocket()
    {
        try { _writer?.Dispose(); } catch { /* best effort */ }
        try { _reader?.Dispose(); } catch { /* best effort */ }
        try { _client?.Dispose(); } catch { /* best effort */ }
        _writer = null;
        _reader = null;
        _client = null;
    }

    public async ValueTask DisposeAsync()
    {
        try { _readLoopCts?.Cancel(); } catch { /* ignore */ }
        if (_readLoopTask is not null)
        {
            try { await _readLoopTask.ConfigureAwait(false); } catch { /* ignore */ }
        }
        CleanupSocket();
        _readLoopCts?.Dispose();
        SetState(FrameLinkState.Disconnected);
    }
}
