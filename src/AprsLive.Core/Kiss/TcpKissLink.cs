using System.Net.Sockets;

namespace AprsLive.Core.Kiss;

/// <summary>
/// KISS over TCP — the way Direwolf (<c>KISSPORT 8001</c>), the AGW/KISS bridge,
/// aprx, and most SDR TNCs expose a network TNC.
/// </summary>
public sealed class TcpKissLink : KissLinkBase
{
    private readonly string _host;
    private readonly int _port;
    private TcpClient? _client;

    public TcpKissLink(string host, int port = 8001)
    {
        _host = host;
        _port = port;
    }

    protected override string Describe() => $"KISS TCP {_host}:{_port}";

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    protected override async Task<Stream> OpenAsync(CancellationToken ct)
    {
        _client = new TcpClient { NoDelay = true };
        EnableKeepAlive(_client.Client);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ConnectTimeout);
        try
        {
            await _client.ConnectAsync(_host, _port, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"pas de réponse de {_host}:{_port} en {ConnectTimeout.TotalSeconds:0} s");
        }
        return _client.GetStream();
    }

    protected override void ReleaseTransport()
    {
        var client = _client;
        _client = null;
        client?.Dispose();
    }

    /// <summary>
    /// KISS has no keepalive of its own and a TNC may stay quiet for hours, so
    /// let TCP probe the peer: a TNC that vanished without closing (power cut,
    /// Wi-Fi lost) fails the read after about a minute (30 s idle + 3 × 10 s).
    /// Each option is best effort, some older systems lack the fine-tuning ones.
    /// </summary>
    private static void EnableKeepAlive(Socket socket)
    {
        try { socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true); }
        catch { return; }
        TrySet(socket, SocketOptionName.TcpKeepAliveTime, 30);
        TrySet(socket, SocketOptionName.TcpKeepAliveInterval, 10);
        TrySet(socket, SocketOptionName.TcpKeepAliveRetryCount, 3);
    }

    private static void TrySet(Socket socket, SocketOptionName option, int value)
    {
        try { socket.SetSocketOption(SocketOptionLevel.Tcp, option, value); }
        catch { /* not supported here: OS defaults apply */ }
    }
}
