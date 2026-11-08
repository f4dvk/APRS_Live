using System.Net;
using System.Net.Sockets;
using System.Text;
using AprsLive.Core.Links;
using AprsLive.Core.Tnc;

namespace AprsLive.Core.Tests;

public class AprsIsConnectionTests
{
    /// <summary>A one-client fake APRS-IS server on a free local port: answers the login, then runs <paramref name="afterLogin"/>.</summary>
    private static (int Port, Task Server) StartServer(Func<StreamWriter, Task> afterLogin)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            listener.Stop();
            var stream = client.GetStream();
            var reader = new StreamReader(stream, Encoding.Latin1);
            var writer = new StreamWriter(stream, Encoding.Latin1) { NewLine = "\r\n", AutoFlush = true };
            await reader.ReadLineAsync();   // "user ... pass ..."
            await writer.WriteLineAsync("# logresp F4DVK-9 unverified, server TEST");
            await afterLogin(writer);
        });
        return (port, server);
    }

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

    [Fact]
    public async Task A_silent_server_is_detected_as_lost()
    {
        var (port, _) = StartServer(_ => Task.Delay(TimeSpan.FromSeconds(10)));   // keeps the socket open, sends nothing
        var logs = new List<string>();
        await using var client = new AprsIsClient("F4DVK-9", "-1", "127.0.0.1", port, null)
        {
            IdleTimeout = TimeSpan.FromSeconds(1),
        };
        client.Log += l => { lock (logs) logs.Add(l); };

        await client.ConnectAsync();
        Assert.Equal(FrameLinkState.Connected, client.State);

        Assert.True(await WaitFor(() => client.State == FrameLinkState.Disconnected, TimeSpan.FromSeconds(5)));
        lock (logs) Assert.Contains(logs, l => l.StartsWith("Aucune donnée du serveur"));
    }

    [Fact]
    public async Task Keepalive_lines_keep_the_connection_up()
    {
        var (port, _) = StartServer(async w =>
        {
            for (int i = 0; i < 12; i++)
            {
                await Task.Delay(250);
                await w.WriteLineAsync("# aprsc keepalive");
            }
        });
        await using var client = new AprsIsClient("F4DVK-9", "-1", "127.0.0.1", port, null)
        {
            IdleTimeout = TimeSpan.FromSeconds(1),
        };

        await client.ConnectAsync();
        await Task.Delay(TimeSpan.FromSeconds(2));

        Assert.Equal(FrameLinkState.Connected, client.State);
    }

    [Fact]
    public async Task A_server_closing_the_connection_ends_in_disconnected()
    {
        var (port, _) = StartServer(_ => Task.CompletedTask);   // closes right after the login reply
        await using var client = new AprsIsClient("F4DVK-9", "-1", "127.0.0.1", port, null);

        await client.ConnectAsync();

        Assert.True(await WaitFor(() => client.State == FrameLinkState.Disconnected, TimeSpan.FromSeconds(5)));
    }

    [Theory]
    [InlineData(1, 10)]
    [InlineData(2, 30)]
    [InlineData(3, 60)]
    [InlineData(4, 120)]
    [InlineData(25, 120)]
    public void Reconnect_delays_grow_then_stay_at_two_minutes(int attempt, int seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), ReconnectSchedule.DelayBefore(attempt));
}
