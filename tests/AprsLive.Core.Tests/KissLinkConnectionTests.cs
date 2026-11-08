using System.Net;
using System.Net.Sockets;
using AprsLive.Core.Kiss;
using AprsLive.Core.Links;

namespace AprsLive.Core.Tests;

public class KissLinkConnectionTests
{
    private static async Task<bool> WaitFor(Func<bool> condition, TimeSpan timeout)
    {
        var end = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < end)
        {
            if (condition()) return true;
            await Task.Delay(50);
        }
        return condition();
    }

    /// <summary>A one-client fake KISS TCP TNC that keeps the connection for <paramref name="hold"/>, then closes it.</summary>
    private static int StartTnc(TimeSpan hold)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        _ = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            listener.Stop();
            await Task.Delay(hold);
        });
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    [Theory]
    [InlineData(0)]     // closes right after accepting: must not be left "connected"
    [InlineData(300)]
    public async Task A_tnc_closing_the_connection_ends_in_disconnected(int holdMs)
    {
        int port = StartTnc(TimeSpan.FromMilliseconds(holdMs));
        await using var link = new TcpKissLink("127.0.0.1", port);
        var states = new List<FrameLinkState>();
        link.StateChanged += s => { lock (states) states.Add(s); };

        await link.ConnectAsync();

        Assert.True(await WaitFor(() => link.State == FrameLinkState.Disconnected, TimeSpan.FromSeconds(5)));
        await Task.Delay(100);
        Assert.Equal(FrameLinkState.Disconnected, link.State);
        lock (states) Assert.Equal(FrameLinkState.Disconnected, states[^1]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => link.SendFrameAsync(new byte[] { 1, 2, 3 }));
    }

    /// <summary>
    /// Behaves like a serial port on Windows: a pending read ignores its
    /// cancellation token and only ends when data comes in or the port is closed.
    /// </summary>
    private sealed class DeafToCancellationStream : Stream
    {
        private readonly TaskCompletionSource<int> _pendingRead = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            new(_pendingRead.Task);

        protected override void Dispose(bool disposing)
        {
            _pendingRead.TrySetException(new ObjectDisposedException(nameof(DeafToCancellationStream)));
            base.Dispose(disposing);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) { }
    }

    private sealed class DeafLink : KissLinkBase
    {
        protected override Task<Stream> OpenAsync(CancellationToken ct) => Task.FromResult<Stream>(new DeafToCancellationStream());
        protected override string Describe() => "test";
    }

    [Fact]
    public async Task Disconnect_does_not_wait_for_a_read_that_ignores_cancellation()
    {
        var link = new DeafLink();
        var log = new List<string>();
        link.Log += line => { lock (log) log.Add(line); };
        await link.ConnectAsync();
        await Task.Delay(100);   // the pump is now blocked in its read

        var dispose = link.DisposeAsync().AsTask();
        bool finished = await Task.WhenAny(dispose, Task.Delay(TimeSpan.FromSeconds(1))) == dispose;

        Assert.True(finished, "DisposeAsync hung on the pending read");
        Assert.Equal(FrameLinkState.Disconnected, link.State);
        lock (log) Assert.DoesNotContain(log, l => l.StartsWith("Erreur de lecture"));
    }

    [Fact]
    public async Task Connecting_to_a_closed_port_faults()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();   // nothing listens there any more

        await using var link = new TcpKissLink("127.0.0.1", port);

        await Assert.ThrowsAnyAsync<Exception>(() => link.ConnectAsync());
        Assert.Equal(FrameLinkState.Faulted, link.State);
    }
}
