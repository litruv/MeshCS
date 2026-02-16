using System.Net.Sockets;

namespace MeshCS.Transport;

/// <summary>
/// TCP transport for WiFi serial mesh radio communication.
/// Uses length-prefixed framing: to device '&lt;' + len:2LE + payload, from device '&gt;' + len:2LE + payload.
/// </summary>
public sealed class TcpTransport : IMeshTransport
{
    private const byte FrameToDevice = 0x3C;   // '<'
    private const byte FrameFromDevice = 0x3E; // '>'

    private readonly string _host;
    private readonly int _port;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private readonly byte[] _rxBuffer = new byte[4096];
    private int _rxBufferPos;
    private int _rxBufferLen;
    private bool _disposed;

    /// <inheritdoc/>
    public bool IsOpen => _client?.Connected ?? false;

    /// <inheritdoc/>
    public bool RequiresCobsFraming => false;

    /// <inheritdoc/>
    public int BytesAvailable => (_stream?.DataAvailable == true || _rxBufferLen > _rxBufferPos) ? 1 : 0;

    /// <summary>
    /// Creates a new TCP transport for WiFi serial.
    /// </summary>
    /// <param name="host">Host address (e.g., "192.168.1.201").</param>
    /// <param name="port">Port number (e.g., 5000).</param>
    public TcpTransport(string host, int port)
    {
        _host = host;
        _port = port;
    }

    /// <summary>
    /// Creates a new TCP transport from a connection string.
    /// </summary>
    /// <param name="connectionString">Format: "host:port" (e.g., "192.168.1.201:5000").</param>
    public static TcpTransport FromConnectionString(string connectionString)
    {
        var parts = connectionString.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[1], out var port))
            throw new ArgumentException("Invalid connection string. Expected format: host:port", nameof(connectionString));
        return new TcpTransport(parts[0], port);
    }

    /// <inheritdoc/>
    public async Task OpenAsync(CancellationToken ct = default)
    {
        _client = new TcpClient();
        await _client.ConnectAsync(_host, _port, ct);
        _stream = _client.GetStream();
        _stream.ReadTimeout = 100;
        _stream.WriteTimeout = 1000;
    }

    /// <inheritdoc/>
    public void Close()
    {
        _stream?.Close();
        _client?.Close();
        _stream = null;
        _client = null;
    }

    /// <inheritdoc/>
    public void SendPacket(byte[] packet)
    {
        if (_stream == null)
            throw new InvalidOperationException("Transport not connected");

        // Frame: '<' + 2-byte LE length + payload
        var frame = new byte[3 + packet.Length];
        frame[0] = FrameToDevice;
        frame[1] = (byte)(packet.Length & 0xFF);
        frame[2] = (byte)((packet.Length >> 8) & 0xFF);
        Buffer.BlockCopy(packet, 0, frame, 3, packet.Length);
        _stream.Write(frame, 0, frame.Length);
    }

    /// <inheritdoc/>
    public int ReceivePacket(byte[] buffer, int offset, int maxLength)
    {
        if (_stream == null)
            throw new InvalidOperationException("Transport not connected");

        try
        {
            // Try to read more data into rx buffer
            if (_stream.DataAvailable)
            {
                var space = _rxBuffer.Length - _rxBufferLen;
                if (space > 0)
                {
                    var read = _stream.Read(_rxBuffer, _rxBufferLen, space);
                    _rxBufferLen += read;
                }
            }

            // Check if we have a complete frame
            var available = _rxBufferLen - _rxBufferPos;
            if (available < 3)
                return -1; // Need at least header

            // Check frame marker
            if (_rxBuffer[_rxBufferPos] != FrameFromDevice)
            {
                // Unexpected data - skip byte and try again
                _rxBufferPos++;
                CompactBuffer();
                return -1;
            }

            // Get length
            var payloadLen = _rxBuffer[_rxBufferPos + 1] | (_rxBuffer[_rxBufferPos + 2] << 8);
            if (available < 3 + payloadLen)
                return -1; // Incomplete frame

            // Extract payload
            var copyLen = Math.Min(payloadLen, maxLength);
            Buffer.BlockCopy(_rxBuffer, _rxBufferPos + 3, buffer, offset, copyLen);
            _rxBufferPos += 3 + payloadLen;
            CompactBuffer();

            return copyLen;
        }
        catch (IOException ex) when (ex.InnerException is SocketException { SocketErrorCode: SocketError.TimedOut })
        {
            return -1;
        }
    }

    private void CompactBuffer()
    {
        if (_rxBufferPos > 0)
        {
            var remaining = _rxBufferLen - _rxBufferPos;
            if (remaining > 0)
                Buffer.BlockCopy(_rxBuffer, _rxBufferPos, _rxBuffer, 0, remaining);
            _rxBufferLen = remaining;
            _rxBufferPos = 0;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Close();
    }
}
