using System.IO.Ports;

namespace MeshCS.Transport;

/// <summary>
/// Serial port transport for mesh radio communication.
/// Uses COBS framing with 0x00 delimiters.
/// </summary>
public sealed class SerialTransport : IMeshTransport
{
    private readonly SerialPort _serial;
    private bool _disposed;

    /// <inheritdoc/>
    public bool IsOpen => _serial.IsOpen;

    /// <inheritdoc/>
    public bool RequiresCobsFraming => true;

    /// <inheritdoc/>
    public int BytesAvailable => _serial.IsOpen ? _serial.BytesToRead : 0;

    /// <summary>
    /// Creates a new serial transport.
    /// </summary>
    /// <param name="portName">Serial port name (e.g., "COM11" or "/dev/ttyUSB0").</param>
    /// <param name="baudRate">Baud rate (default 115200).</param>
    public SerialTransport(string portName, int baudRate = 115200)
    {
        _serial = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            ReadTimeout = 100,
            WriteTimeout = 1000
        };
    }

    /// <inheritdoc/>
    public Task OpenAsync(CancellationToken ct = default)
    {
        _serial.Open();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void Close()
    {
        if (_serial.IsOpen)
            _serial.Close();
    }

    /// <inheritdoc/>
    public void SendPacket(byte[] packet)
    {
        // For serial, MeshRadio will COBS-encode before calling this
        _serial.Write(packet, 0, packet.Length);
    }

    /// <inheritdoc/>
    public int ReceivePacket(byte[] buffer, int offset, int maxLength)
    {
        // For serial, just return raw bytes - MeshRadio handles COBS decoding
        if (_serial.BytesToRead == 0)
            return 0;
        return _serial.Read(buffer, offset, Math.Min(maxLength, _serial.BytesToRead));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Close();
        _serial.Dispose();
    }
}
