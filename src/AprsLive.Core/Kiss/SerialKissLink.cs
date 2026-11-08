using System.IO.Ports;

namespace AprsLive.Core.Kiss;

/// <summary>
/// KISS over a serial port (hardware TNC, any baud, 8N1). A TNC that starts
/// in its text command mode — a TNC2, a Kantronics, a Kenwood with a built-in
/// TNC — is put into KISS by <c>initCommands</c> once the port is open, and
/// back out by <c>exitCommands</c> on disconnect (see <see cref="TncScript"/>).
/// </summary>
public sealed class SerialKissLink : KissLinkBase
{
    private readonly string _portName;
    private readonly int _baud;
    private readonly TncScript _initCommands;
    private readonly TncScript _exitCommands;
    private SerialPort? _port;

    public SerialKissLink(string portName, int baud = 9600, TncScript? initCommands = null, TncScript? exitCommands = null)
    {
        _portName = portName;
        _baud = baud;
        _initCommands = initCommands ?? TncScript.Empty;
        _exitCommands = exitCommands ?? TncScript.Empty;
    }

    /// <summary>Time to wait after each command; replaceable for tests.</summary>
    public TimeSpan CommandDelay { get; init; } = TncScript.DefaultCommandDelay;

    protected override string Describe() => $"KISS série {_portName} @ {_baud}";

    protected override async Task<Stream> OpenAsync(CancellationToken ct)
    {
        var port = _port = new SerialPort(_portName, _baud, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None,
            ReadTimeout = SerialPort.InfiniteTimeout,
            WriteTimeout = 2000,
        };
        port.Open();
        // DTR/RTS up once open, not as part of the open: a virtual port (pty
        // from socat/ser2net to a remote TNC) has no modem lines, and asking
        // for them in the open made it fail.
        try
        {
            port.DtrEnable = true;
            port.RtsEnable = true;
        }
        catch (IOException)
        {
            RaiseLog("Lignes DTR/RTS absentes sur ce port (port virtuel ?) : on continue sans.");
        }
        port.DiscardInBuffer();

        if (!_initCommands.IsEmpty)
        {
            RaiseLog("Passage du TNC en mode KISS…");
            await _initCommands.RunAsync(
                (bytes, token) => port.BaseStream.WriteAsync(bytes, token).AsTask(),
                () => ReadAnswer(port),
                RaiseLog, CommandDelay, ct).ConfigureAwait(false);
            // The TNC's last answers are text, not KISS: start the pump on a clean buffer.
            port.DiscardInBuffer();
        }
        return port.BaseStream;
    }

    private static string? ReadAnswer(SerialPort port)
    {
        int count = port.BytesToRead;
        if (count <= 0)
            return null;
        var buffer = new byte[count];
        int read = port.Read(buffer, 0, count);
        return TncScript.Printable(buffer.AsSpan(0, read));
    }

    protected override async Task BeforeCloseAsync(CancellationToken ct)
    {
        if (_exitCommands.IsEmpty || Transport is not { } transport)
            return;
        RaiseLog("Retour du TNC au mode normal…");
        // The read pump still owns the input: the TNC's answers aren't logged here.
        await _exitCommands.RunAsync(
            (bytes, token) => transport.WriteAsync(bytes, token).AsTask(),
            () => null,
            RaiseLog, CommandDelay, ct).ConfigureAwait(false);
    }

    protected override void ReleaseTransport()
    {
        var port = _port;
        _port = null;
        port?.Dispose();
    }

    /// <summary>
    /// Hardware Carrier Detect (CD) line state, same RS-232 control line
    /// family as the RTS/DTR already used for PTT — some hardware TNCs/radio
    /// interfaces assert it when they detect a signal, not all do. False
    /// when not connected or the platform/driver doesn't expose it.
    /// </summary>
    public bool CdHolding
    {
        get
        {
            try { return _port is { IsOpen: true } && _port.CDHolding; }
            catch { return false; }
        }
    }

    /// <summary>Serial ports visible to the OS right now.</summary>
    public static IReadOnlyList<string> AvailablePorts() => SerialPort.GetPortNames();
}
