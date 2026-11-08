using System.IO.Ports;

namespace AprsLive.Core.Kiss;

/// <summary>KISS over a serial port (hardware TNC, any baud, 8N1).</summary>
public sealed class SerialKissLink : KissLinkBase
{
    private readonly string _portName;
    private readonly int _baud;
    private SerialPort? _port;

    public SerialKissLink(string portName, int baud = 9600)
    {
        _portName = portName;
        _baud = baud;
    }

    protected override string Describe() => $"KISS série {_portName} @ {_baud}";

    protected override Task<Stream> OpenAsync(CancellationToken ct)
    {
        _port = new SerialPort(_portName, _baud, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None,
            ReadTimeout = SerialPort.InfiniteTimeout,
            WriteTimeout = 2000,
            DtrEnable = true,
            RtsEnable = true
        };
        _port.Open();
        _port.DiscardInBuffer();
        return Task.FromResult<Stream>(_port.BaseStream);
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
