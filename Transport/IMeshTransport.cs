namespace MeshCS.Transport;

/// <summary>
/// Abstraction for mesh radio transport (Serial, TCP, etc.).
/// </summary>
public interface IMeshTransport : IDisposable
{
    /// <summary>
    /// Whether the transport is currently connected/open.
    /// </summary>
    bool IsOpen { get; }

    /// <summary>
    /// Whether this transport requires COBS framing.
    /// Serial uses COBS with 0x00 delimiters, TCP uses length-prefixed frames.
    /// </summary>
    bool RequiresCobsFraming { get; }

    /// <summary>
    /// Opens the transport connection.
    /// </summary>
    Task OpenAsync(CancellationToken ct = default);

    /// <summary>
    /// Closes the transport connection.
    /// </summary>
    void Close();

    /// <summary>
    /// Sends a packet. For serial, caller handles COBS. For TCP, transport handles framing.
    /// </summary>
    /// <param name="packet">Raw packet bytes to send.</param>
    void SendPacket(byte[] packet);

    /// <summary>
    /// Receives a packet. For serial, returns raw bytes (caller handles COBS). 
    /// For TCP, transport handles framing and returns payload.
    /// </summary>
    /// <param name="buffer">Buffer to read into.</param>
    /// <param name="offset">Offset in buffer.</param>
    /// <param name="maxLength">Maximum bytes to read.</param>
    /// <returns>Number of bytes read, or -1 if no complete packet available.</returns>
    int ReceivePacket(byte[] buffer, int offset, int maxLength);

    /// <summary>
    /// For serial: number of bytes available to read.
    /// For TCP: always returns 1 if data might be available (check via ReceivePacket).
    /// </summary>
    int BytesAvailable { get; }
}
