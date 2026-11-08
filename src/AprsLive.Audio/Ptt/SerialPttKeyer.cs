using System.IO.Ports;

namespace AprsLive.Audio.Ptt;

public enum PttLine
{
    Rts,
    Dtr,
}

/// <summary>
/// Keys PTT via a serial port's RTS or DTR line — a separate port from any
/// KISS TNC serial link, opened purely for line control. A watchdog timer
/// force-unkeys after <paramref name="watchdog"/> regardless of what the
/// caller does, so a bug upstream (an exception between Key() and Dispose(),
/// a hung audio write) can never leave the transmitter keyed indefinitely —
/// always use <c>using var tx = keyer.Key();</c>, never call it bare.
/// </summary>
public sealed class SerialPttKeyer : IPttKeyer
{
    private readonly SerialPort _port;
    private readonly PttLine _line;
    private readonly bool _inverted;
    private readonly TimeSpan _watchdog;
    private readonly object _lock = new();
    private Timer? _watchdogTimer;

    public SerialPttKeyer(string portName, PttLine line, bool inverted = false, TimeSpan? watchdog = null)
    {
        _line = line;
        _inverted = inverted;
        _watchdog = watchdog ?? TimeSpan.FromSeconds(15);

        _port = new SerialPort(portName) { DtrEnable = false, RtsEnable = false };
        _port.Open();
    }

    public IDisposable Key()
    {
        lock (_lock)
        {
            SetLine(true);
            _watchdogTimer?.Dispose();
            _watchdogTimer = new Timer(_ => Unkey(), null, _watchdog, Timeout.InfiniteTimeSpan);
        }
        return new KeyHandle(this);
    }

    private void Unkey()
    {
        lock (_lock)
        {
            _watchdogTimer?.Dispose();
            _watchdogTimer = null;
            SetLine(false);
        }
    }

    private void SetLine(bool on)
    {
        bool value = _inverted ? !on : on;
        if (_line == PttLine.Rts) _port.RtsEnable = value;
        else _port.DtrEnable = value;
    }

    private sealed class KeyHandle(SerialPttKeyer owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            owner.Unkey();
        }
    }

    public void Dispose()
    {
        try { SetLine(false); } catch { /* best effort */ }
        _watchdogTimer?.Dispose();
        try { _port.Close(); } catch { /* best effort */ }
        _port.Dispose();
    }
}
