using System.Text;
using AprsLive.Core.Kiss;

namespace AprsLive.Core.Tests;

public class TncScriptTests
{
    [Fact]
    public void Reads_commands_pauses_raw_bytes_and_skips_comments()
    {
        var script = TncScript.Parse("""
            # a comment
            {hex C0 FF C0}

            TC 1
            {pause 1,5}
            {HEX 0x03 0d}
            KISS ON
            """);

        Assert.Empty(script.Errors);
        Assert.Collection(script.Steps,
            s => Assert.Equal(new byte[] { 0xC0, 0xFF, 0xC0 }, Assert.IsType<TncStep.Bytes>(s).Data),
            s => Assert.Equal("TC 1", Assert.IsType<TncStep.Command>(s).Text),
            s => Assert.Equal(TimeSpan.FromSeconds(1.5), Assert.IsType<TncStep.Pause>(s).Duration),
            s => Assert.Equal(new byte[] { 0x03, 0x0D }, Assert.IsType<TncStep.Bytes>(s).Data),
            s => Assert.Equal("KISS ON", Assert.IsType<TncStep.Command>(s).Text));
    }

    [Theory]
    [InlineData("{hex C0 G1}", "ligne 1")]
    [InlineData("{hex C}", "ligne 1")]
    [InlineData("KISS ON\n{pause}", "ligne 2")]
    [InlineData("{pause 90}", "30 au plus")]
    [InlineData("{wait 2}", "inconnu")]
    public void Reports_the_lines_it_cannot_read_and_leaves_them_out(string text, string expected)
    {
        var script = TncScript.Parse(text);
        Assert.Contains(expected, Assert.Single(script.Errors));
        Assert.DoesNotContain(script.Steps, s => s is not TncStep.Command);
    }

    [Fact]
    public void Nothing_to_send_for_an_empty_text()
    {
        Assert.True(TncScript.Parse("").IsEmpty);
        Assert.True(TncScript.Parse("# only a comment\n\n").IsEmpty);
    }

    [Fact]
    public async Task Sends_each_command_with_a_carriage_return_waits_and_logs_the_answers()
    {
        var script = TncScript.Parse("{hex C0 FF C0}\nKISS ON\n{pause 2}\nRESTART");
        var sent = new List<byte>();
        var waits = new List<TimeSpan>();
        var log = new List<string>();
        var answers = new Queue<string?>(["", "cmd:KISS ON\r\ncmd:", null]);

        await script.RunAsync(
            (bytes, _) => { sent.AddRange(bytes.ToArray()); return Task.CompletedTask; },
            () => answers.Count > 0 ? answers.Dequeue() : null,
            log.Add,
            TimeSpan.FromSeconds(1),
            delay: (d, _) => { waits.Add(d); return Task.CompletedTask; });

        Assert.Equal([0xC0, 0xFF, 0xC0, .. Encoding.ASCII.GetBytes("KISS ON\rRESTART\r")], sent);
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1)], waits);
        Assert.Equal(["→ {hex C0 FF C0}", "→ KISS ON", "← cmd:KISS ON", "← cmd:", "→ RESTART"], log);
    }

    [Fact]
    public void Shows_what_the_tnc_sent_with_unreadable_bytes_as_dots()
    {
        Assert.Equal("··· \r\ncmd:", TncScript.Printable([0xC0, 0xFF, 0x03, 0x20, 0x0D, 0x0A, .. "cmd:"u8]));
    }

    [Fact]
    public async Task Stops_when_cancelled()
    {
        var script = TncScript.Parse("TC 1\nTN 2,0");
        using var cts = new CancellationTokenSource();
        int writes = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => script.RunAsync(
            (_, _) => { writes++; cts.Cancel(); return Task.CompletedTask; },
            () => null, _ => { }, TimeSpan.Zero, cts.Token,
            (d, ct) => Task.Delay(d, ct)));
        Assert.Equal(1, writes);
    }

    [Fact]
    public void Every_model_s_commands_read_without_error()
    {
        foreach (var model in TncModels.All)
        {
            Assert.Empty(TncScript.Parse(model.InitCommands).Errors);
            Assert.Empty(TncScript.Parse(model.ExitCommands).Errors);
        }
    }

    [Fact]
    public void The_kenwood_d710_goes_to_packet_mode_on_band_a_then_kiss()
    {
        var commands = TncScript.Parse(TncModels.KenwoodD710.InitCommands).Steps
            .OfType<TncStep.Command>().Select(c => c.Text);
        Assert.Equal(["TC 1", "TN 2,0", "HBAUD 1200", "KISS ON", "RESTART"], commands);
        Assert.Equal(9600, TncModels.KenwoodD710.BaudRate);
    }

    [Fact]
    public void An_unknown_saved_model_falls_back_to_none()
    {
        Assert.Same(TncModels.None, TncModels.Find("tm-d9999"));
        Assert.Same(TncModels.KenwoodD700, TncModels.Find("tm-d700"));
    }
}
