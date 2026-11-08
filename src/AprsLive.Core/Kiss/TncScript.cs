using System.Globalization;
using System.Text;

namespace AprsLive.Core.Kiss;

/// <summary>
/// Commands sent to a TNC (or a radio with a built-in TNC) on the serial
/// port, in its text command mode: to put it into KISS mode on connect, and
/// back to normal on disconnect. One command per line, sent with a carriage
/// return. A line <c>{pause 2}</c> waits 2 s, <c>{hex C0 FF C0}</c> sends raw
/// bytes (here the KISS "leave KISS mode" frame), and a line starting with
/// <c>#</c> is a comment. Blank lines are skipped.
/// </summary>
public sealed class TncScript
{
    /// <summary>Waited after every command, so the TNC has taken it before the next one.</summary>
    public static readonly TimeSpan DefaultCommandDelay = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan MaxPause = TimeSpan.FromSeconds(30);

    private TncScript(IReadOnlyList<TncStep> steps, IReadOnlyList<string> errors)
    {
        Steps = steps;
        Errors = errors;
    }

    public IReadOnlyList<TncStep> Steps { get; }

    /// <summary>Lines that couldn't be read, e.g. <c>ligne 3 : octet « G1 » invalide</c>; those lines are left out.</summary>
    public IReadOnlyList<string> Errors { get; }

    public bool IsEmpty => Steps.Count == 0;

    public static TncScript Empty { get; } = new([], []);

    public static TncScript Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Empty;

        var steps = new List<TncStep>();
        var errors = new List<string>();
        string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            if (!(line.StartsWith('{') && line.EndsWith('}')))
            {
                steps.Add(new TncStep.Command(line));
                continue;
            }

            string inner = line[1..^1].Trim();
            int space = inner.IndexOf(' ');
            string word = (space < 0 ? inner : inner[..space]).ToLowerInvariant();
            string args = space < 0 ? "" : inner[(space + 1)..].Trim();
            switch (word)
            {
                case "pause":
                    if (double.TryParse(args.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double s)
                        && s > 0 && TimeSpan.FromSeconds(s) <= MaxPause)
                        steps.Add(new TncStep.Pause(TimeSpan.FromSeconds(s)));
                    else
                        errors.Add($"ligne {i + 1} : durée de pause invalide « {args} » (en secondes, 30 au plus)");
                    break;
                case "hex":
                    if (ParseHex(args) is { } bytes)
                        steps.Add(new TncStep.Bytes(bytes));
                    else
                        errors.Add($"ligne {i + 1} : octets invalides « {args} » (ex. C0 FF C0)");
                    break;
                default:
                    errors.Add($"ligne {i + 1} : « {{{word}}} » inconnu (pause ou hex)");
                    break;
            }
        }
        return new TncScript(steps, errors);
    }

    // "C0 FF C0", "C0FFC0", "0xC0 0xFF 0xC0".
    private static byte[]? ParseHex(string text)
    {
        var digits = new StringBuilder();
        foreach (string part in text.Split([' ', ',', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            string p = part.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? part[2..] : part;
            if (p.Length % 2 != 0)
                return null;
            digits.Append(p);
        }
        if (digits.Length == 0)
            return null;
        try
        {
            return Convert.FromHexString(digits.ToString());
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Sends the steps. <paramref name="readAvailable"/> returns what the TNC
    /// answered since the last call (or null), logged after each command;
    /// <paramref name="delay"/> is replaceable for tests.
    /// </summary>
    public async Task RunAsync(
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> write,
        Func<string?> readAvailable,
        Action<string> log,
        TimeSpan commandDelay,
        CancellationToken ct = default,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        delay ??= Task.Delay;
        foreach (var step in Steps)
        {
            ct.ThrowIfCancellationRequested();
            switch (step)
            {
                case TncStep.Pause pause:
                    await delay(pause.Duration, ct).ConfigureAwait(false);
                    continue;
                case TncStep.Command command:
                    log($"→ {command.Text}");
                    await write(Encoding.ASCII.GetBytes(command.Text + "\r"), ct).ConfigureAwait(false);
                    break;
                case TncStep.Bytes bytes:
                    log($"→ {{hex {BitConverter.ToString(bytes.Data).Replace('-', ' ')}}}");
                    await write(bytes.Data, ct).ConfigureAwait(false);
                    break;
            }
            await delay(commandDelay, ct).ConfigureAwait(false);
            LogAnswer(readAvailable(), log);
        }
    }

    /// <summary>
    /// What a TNC sent, as text for the log: printable ASCII kept, line ends
    /// kept, anything else (an echoed KISS byte, noise) shown as a dot.
    /// </summary>
    public static string Printable(ReadOnlySpan<byte> bytes)
    {
        var sb = new StringBuilder(bytes.Length);
        foreach (byte b in bytes)
            sb.Append(b is 0x0D or 0x0A || (b >= 0x20 && b < 0x7F) ? (char)b : '·');
        return sb.ToString();
    }

    // The TNC's echo and prompts ("cmd:KISS ON"), one log line each, control characters left out.
    private static void LogAnswer(string? answer, Action<string> log)
    {
        if (string.IsNullOrEmpty(answer))
            return;
        foreach (string raw in answer.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = new string(raw.Where(c => !char.IsControl(c)).ToArray()).Trim();
            if (line.Length > 0)
                log($"← {line}");
        }
    }
}

/// <summary>One step of a <see cref="TncScript"/>.</summary>
public abstract record TncStep
{
    private TncStep() { }

    /// <summary>A text command, sent followed by a carriage return.</summary>
    public sealed record Command(string Text) : TncStep;

    /// <summary>Raw bytes, sent as they are.</summary>
    public sealed record Bytes(byte[] Data) : TncStep;

    public sealed record Pause(TimeSpan Duration) : TncStep;
}
