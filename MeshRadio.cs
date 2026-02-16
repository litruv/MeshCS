using System.Collections.Concurrent;
using System.IO.Ports;
using System.Text;
using MeshCS.Transport;

namespace MeshCS;

/// <summary>
/// Result from scanning a serial port for a MeshCore device.
/// </summary>
public sealed class ScanResult
{
    /// <summary>Serial port name (e.g., "COM11").</summary>
    public string PortName { get; init; } = "";

    /// <summary>Whether a MeshCore device was found.</summary>
    public bool Found { get; init; }

    /// <summary>Device self-info if found.</summary>
    public SelfInfo? Self { get; init; }

    /// <summary>Device info if available.</summary>
    public DeviceInfo? Device { get; init; }

    /// <summary>Error message if scan failed.</summary>
    public string? Error { get; init; }
}

/// <summary>
/// High-level async API for communicating with a MeshCore companion radio.
/// Handles COBS framing, command/response correlation, and event dispatching.
/// </summary>
public sealed class MeshRadio : IDisposable
{
    private readonly IMeshTransport _transport;
    private readonly ConcurrentQueue<byte[]> _rxQueue = new();
    private readonly ConcurrentQueue<DirectMessage> _messageQueue = new();
    private readonly ConcurrentQueue<ChannelMessage> _channelMessageQueue = new();
    private readonly ConcurrentDictionary<byte, TaskCompletionSource<byte[]>> _waitingResponses = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly List<byte> _rxBuffer = [];
    private CancellationTokenSource? _cts;
    private Task? _readTask;
    private bool _disposed;

    /// <summary>Device self-info, populated after Connect().</summary>
    public SelfInfo? Self { get; private set; }

    /// <summary>Device info, populated after DeviceQueryAsync().</summary>
    public DeviceInfo? Device { get; private set; }

    /// <summary>Whether the transport is connected.</summary>
    public bool IsConnected => _transport.IsOpen;

    /// <summary>Number of direct messages pending in the local queue.</summary>
    public int PendingMessageCount => _messageQueue.Count;

    /// <summary>Number of channel messages pending in the local queue.</summary>
    public int PendingChannelMessageCount => _channelMessageQueue.Count;

    #region Events

    /// <summary>Fired when successfully connected to the device.</summary>
    public event Action<SelfInfo>? Connected;

    /// <summary>Fired when disconnected from the device.</summary>
    public event Action? Disconnected;

    /// <summary>Fired when an error occurs.</summary>
    public event Action<string>? ErrorOccurred;

    /// <summary>Fired when a direct message is received.</summary>
    public event Action<DirectMessage>? DirectMessageReceived;

    /// <summary>Fired when a channel message is received.</summary>
    public event Action<ChannelMessage>? ChannelMessageReceived;

    /// <summary>Fired when an advertisement is received.</summary>
    public event Action<Advert>? AdvertReceived;

    /// <summary>Fired when a message is waiting (call GetNextMessageAsync).</summary>
    public event Action? MessageWaiting;

    /// <summary>Fired when raw data is received from the mesh.</summary>
    public event Action<byte[]>? RawDataReceived;

    /// <summary>Fired for every packet received (for debugging/logging).</summary>
    public event Action<byte[]>? PacketReceived;

    /// <summary>Fired for every packet sent (for debugging/logging).</summary>
    public event Action<byte[]>? PacketSent;

    #endregion

    /// <summary>Enable verbose logging to console.</summary>
    public bool VerboseLogging { get; set; }

    /// <summary>
    /// Gets all available serial port names on this system.
    /// </summary>
    public static string[] GetAvailablePorts() => SerialPort.GetPortNames();

    /// <summary>
    /// Scans all available serial ports for MeshCore devices.
    /// </summary>
    /// <param name="timeoutMs">Timeout per port in milliseconds (default 2000).</param>
    /// <param name="baudRate">Baud rate to use (default 115200).</param>
    /// <param name="verbose">Print progress to console.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of scan results for each port.</returns>
    public static async Task<List<ScanResult>> ScanAllPortsAsync(
        int timeoutMs = 2000,
        int baudRate = 115200,
        bool verbose = false,
        CancellationToken ct = default)
    {
        var ports = GetAvailablePorts();
        var results = new List<ScanResult>();

        if (verbose)
            Console.WriteLine($"[MeshRadio] Scanning {ports.Length} ports...");

        foreach (var port in ports)
        {
            if (ct.IsCancellationRequested) break;

            var result = await ScanPortAsync(port, timeoutMs, baudRate, verbose, ct);
            results.Add(result);
        }

        return results;
    }

    /// <summary>
    /// Scans a specific serial port for a MeshCore device.
    /// </summary>
    /// <param name="portName">Serial port name (e.g., "COM11").</param>
    /// <param name="timeoutMs">Timeout in milliseconds (default 2000).</param>
    /// <param name="baudRate">Baud rate to use (default 115200).</param>
    /// <param name="verbose">Print progress to console.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Scan result for the port.</returns>
    public static async Task<ScanResult> ScanPortAsync(
        string portName,
        int timeoutMs = 2000,
        int baudRate = 115200,
        bool verbose = false,
        CancellationToken ct = default)
    {
        if (verbose)
            Console.Write($"[MeshRadio] {portName}... ");

        SerialPort? serial = null;
        try
        {
            serial = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = 100,
                WriteTimeout = 500
            };
            serial.Open();

            // Small delay for device to be ready
            await Task.Delay(100, ct);

            // Send AppStart command
            var appStart = PacketBuilder.AppStart("Scan");
            var encoded = CobsEncode(appStart);
            var frame = new byte[encoded.Length + 1];
            Buffer.BlockCopy(encoded, 0, frame, 0, encoded.Length);
            frame[^1] = 0x00;
            serial.Write(frame, 0, frame.Length);

            // Wait for response with timeout
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            var rxBuffer = new List<byte>();

            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                if (serial.BytesToRead > 0)
                {
                    var available = serial.BytesToRead;
                    var buffer = new byte[available];
                    serial.Read(buffer, 0, available);

                    foreach (var b in buffer)
                    {
                        if (b == 0x00 && rxBuffer.Count > 0)
                        {
                            var decoded = CobsDecode([.. rxBuffer]);
                            if (decoded.Length > 0)
                            {
                                var code = PacketParser.GetBaseCode(decoded);
                                if (code == (byte)Response.SelfInfo)
                                {
                                    var selfInfo = PacketParser.ParseSelfInfo(decoded);
                                    if (selfInfo != null)
                                    {
                                        if (verbose)
                                            Console.WriteLine($"Found: {selfInfo.Name} ({selfInfo.PublicKeyPrefix})");

                                        serial.Close();
                                        return new ScanResult
                                        {
                                            PortName = portName,
                                            Found = true,
                                            Self = selfInfo
                                        };
                                    }
                                }
                            }
                            rxBuffer.Clear();
                        }
                        else if (b != 0x00)
                        {
                            rxBuffer.Add(b);
                        }
                    }
                }
                await Task.Delay(50, ct);
            }

            if (verbose)
                Console.WriteLine("No response");

            serial.Close();
            return new ScanResult { PortName = portName, Found = false };
        }
        catch (Exception ex)
        {
            if (verbose)
                Console.WriteLine($"Error: {ex.Message}");

            return new ScanResult
            {
                PortName = portName,
                Found = false,
                Error = ex.Message
            };
        }
        finally
        {
            if (serial?.IsOpen == true)
                serial.Close();
            serial?.Dispose();
        }
    }

    /// <summary>
    /// Finds the first available MeshCore device.
    /// </summary>
    /// <param name="timeoutMs">Timeout per port in milliseconds.</param>
    /// <param name="baudRate">Baud rate to use.</param>
    /// <param name="verbose">Print progress to console.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The first found device, or null if none found.</returns>
    public static async Task<ScanResult?> FindFirstDeviceAsync(
        int timeoutMs = 2000,
        int baudRate = 115200,
        bool verbose = false,
        CancellationToken ct = default)
    {
        var ports = GetAvailablePorts();

        if (verbose)
            Console.WriteLine($"[MeshRadio] Searching {ports.Length} ports for MeshCore device...");

        foreach (var port in ports)
        {
            if (ct.IsCancellationRequested) break;

            var result = await ScanPortAsync(port, timeoutMs, baudRate, verbose, ct);
            if (result.Found)
                return result;
        }

        if (verbose)
            Console.WriteLine("[MeshRadio] No MeshCore device found");

        return null;
    }

    /// <summary>
    /// Creates and connects to the first available MeshCore device.
    /// </summary>
    /// <param name="appName">Application name to identify to the device.</param>
    /// <param name="timeoutMs">Timeout per port in milliseconds.</param>
    /// <param name="baudRate">Baud rate to use.</param>
    /// <param name="verbose">Print progress to console.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Connected MeshRadio instance, or null if no device found.</returns>
    public static async Task<MeshRadio?> ConnectFirstAsync(
        string appName = "MeshCore",
        int timeoutMs = 2000,
        int baudRate = 115200,
        bool verbose = false,
        CancellationToken ct = default)
    {
        var result = await FindFirstDeviceAsync(timeoutMs, baudRate, verbose, ct);
        if (result == null || !result.Found)
            return null;

        var radio = new MeshRadio(result.PortName, baudRate) { VerboseLogging = verbose };
        await radio.ConnectAsync(appName, ct);
        return radio;
    }

    /// <summary>
    /// Creates a new MeshRadio instance with a transport.
    /// </summary>
    /// <param name="transport">The transport to use for communication.</param>
    public MeshRadio(IMeshTransport transport)
    {
        _transport = transport;
    }

    /// <summary>
    /// Creates a new MeshRadio instance using serial port.
    /// </summary>
    /// <param name="portName">Serial port name (e.g., "COM11" or "/dev/ttyUSB0").</param>
    /// <param name="baudRate">Baud rate (default 115200).</param>
    public MeshRadio(string portName, int baudRate = 115200)
        : this(new SerialTransport(portName, baudRate))
    {
    }

    /// <summary>
    /// Creates a new MeshRadio instance connecting via TCP/WiFi.
    /// </summary>
    /// <param name="host">Host address (e.g., "192.168.1.201").</param>
    /// <param name="port">Port number (e.g., 5000).</param>
    public static MeshRadio CreateTcp(string host, int port)
    {
        return new MeshRadio(new TcpTransport(host, port));
    }

    /// <summary>
    /// Opens the transport connection and initializes the companion session.
    /// </summary>
    /// <param name="appName">Application name to identify to the device.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task ConnectAsync(string appName = "MeshCore", CancellationToken ct = default)
    {
        if (_transport.IsOpen)
            throw new InvalidOperationException("Already connected");

        await _transport.OpenAsync(ct);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _readTask = Task.Run(() => ReadLoop(_cts.Token), _cts.Token);

        // Small delay for device to be ready
        await Task.Delay(200, ct);

        // Send AppStart and wait for SelfInfo
        var response = await SendCommandAsync(PacketBuilder.AppStart(appName), Response.SelfInfo, 5000, ct);
        Self = PacketParser.ParseSelfInfo(response);

        if (VerboseLogging && Self != null)
            Console.WriteLine($"[MeshRadio] Connected: {Self.Name} ({Self.PublicKeyPrefix})");

        if (Self != null)
            Connected?.Invoke(Self);
    }

    /// <summary>
    /// Queries the device info (firmware version, model, etc.).
    /// </summary>
    public async Task<DeviceInfo?> DeviceQueryAsync(CancellationToken ct = default)
    {
        var response = await SendCommandAsync(PacketBuilder.DeviceQuery(), Response.DeviceInfo, 2000, ct);
        Device = PacketParser.ParseDeviceInfo(response);
        return Device;
    }

    /// <summary>
    /// Sends a direct message to a contact.
    /// </summary>
    /// <param name="recipientPubKeyPrefix">First 6 bytes of recipient's public key.</param>
    /// <param name="text">Message text.</param>
    /// <param name="textType">Text type (Plain or CliData).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<bool> SendMessageAsync(byte[] recipientPubKeyPrefix, string text, TextType textType = TextType.Plain, CancellationToken ct = default)
    {
        var packet = PacketBuilder.SendMsg(recipientPubKeyPrefix, text, textType);
        var response = await SendCommandAsync(packet, Response.Sent, 5000, ct);
        return PacketParser.IsSent(response);
    }

    /// <summary>
    /// Sends a direct message with raw binary payload.
    /// </summary>
    /// <param name="recipientPubKeyPrefix">First 6 bytes of recipient's public key.</param>
    /// <param name="payload">Raw message payload bytes.</param>
    /// <param name="textType">Text type (Plain or CliData).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<bool> SendMessageRawAsync(byte[] recipientPubKeyPrefix, byte[] payload, TextType textType = TextType.CliData, CancellationToken ct = default)
    {
        var packet = PacketBuilder.SendMsgRaw(recipientPubKeyPrefix, payload, textType);
        var response = await SendCommandAsync(packet, Response.Sent, 5000, ct);
        return PacketParser.IsSent(response);
    }

    /// <summary>
    /// Sends a message to a channel.
    /// </summary>
    /// <param name="channelIndex">Channel index (0-7).</param>
    /// <param name="text">Message text.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<bool> SendChannelMessageAsync(byte channelIndex, string text, CancellationToken ct = default)
    {
        var packet = PacketBuilder.SendChannelMsg(channelIndex, text);
        // Device returns Ok (0x00) for channel messages, not Sent (0x07)
        var response = await SendCommandAsync(packet, Response.Ok, 5000, ct);
        return PacketParser.IsOk(response);
    }

    /// <summary>
    /// Fetches the next waiting message from the device queue.
    /// </summary>
    /// <returns>The message, or null if queue is empty.</returns>
    public async Task<DirectMessage?> GetNextMessageAsync(CancellationToken ct = default)
    {
        var packet = PacketBuilder.GetMsg();
        var response = await SendCommandAsync(packet, null, 2000, ct);

        if (PacketParser.IsNoMoreMessages(response))
            return null;

        return PacketParser.ParseDirectMessage(response);
    }

    /// <summary>
    /// Fetches the next waiting message, handling both direct and channel messages.
    /// Channel messages are fired via the ChannelMessageReceived event.
    /// </summary>
    /// <returns>DirectMessage if it was a DM, null if channel message or no more messages.</returns>
    public async Task<DirectMessage?> FetchNextMessageAsync(CancellationToken ct = default)
    {
        var packet = PacketBuilder.GetMsg();
        var response = await SendCommandAsync(packet, null, 2000, ct);

        if (PacketParser.IsNoMoreMessages(response))
            return null;

        var code = PacketParser.GetBaseCode(response);
        
        // Check if it's a channel message (0x08 or 0x11)
        if (code == (byte)Response.ChannelMsg || code == (byte)Response.ChannelMsgV3)
        {
            var channelMsg = PacketParser.ParseChannelMessage(response);
            if (channelMsg != null)
            {
                _channelMessageQueue.Enqueue(channelMsg);
                ChannelMessageReceived?.Invoke(channelMsg);
            }
            return null;
        }

        // Otherwise try to parse as direct message
        return PacketParser.ParseDirectMessage(response);
    }

    /// <summary>
    /// Tries to dequeue a pushed message that was received via push notification.
    /// </summary>
    /// <param name="message">The dequeued message, or null if queue is empty.</param>
    /// <returns>True if a message was dequeued.</returns>
    public bool TryDequeueMessage([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out DirectMessage? message)
    {
        return _messageQueue.TryDequeue(out message);
    }

    /// <summary>
    /// Tries to dequeue a pushed channel message that was received via push notification.
    /// </summary>
    /// <param name="message">The dequeued channel message, or null if queue is empty.</param>
    /// <returns>True if a message was dequeued.</returns>
    public bool TryDequeueChannelMessage([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ChannelMessage? message)
    {
        return _channelMessageQueue.TryDequeue(out message);
    }

    /// <summary>
    /// Adds or updates a contact.
    /// </summary>
    /// <param name="publicKey">32-byte public key.</param>
    /// <param name="name">Display name.</param>
    /// <param name="type">Contact type.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<bool> AddContactAsync(byte[] publicKey, string name, ContactType type = ContactType.Chat, CancellationToken ct = default)
    {
        var packet = PacketBuilder.UpdateContact(publicKey, name, type);
        var response = await SendCommandAsync(packet, Response.Ok, 2000, ct);
        return PacketParser.IsOk(response);
    }

    /// <summary>
    /// Removes a contact.
    /// </summary>
    /// <param name="publicKeyPrefix">First 6 bytes of the contact's public key.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<bool> RemoveContactAsync(byte[] publicKeyPrefix, CancellationToken ct = default)
    {
        var packet = PacketBuilder.RemoveContact(publicKeyPrefix);
        var response = await SendCommandAsync(packet, Response.Ok, 2000, ct);
        return PacketParser.IsOk(response);
    }

    /// <summary>
    /// Gets all contacts from the device.
    /// </summary>
    /// <param name="since">Only return contacts updated since this timestamp (0 = all).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<List<Contact>> GetContactsAsync(uint since = 0, CancellationToken ct = default)
    {
        var contacts = new List<Contact>();
        var packet = PacketBuilder.GetContacts(since);

        // Send and wait for ContactsStart
        await SendRawAsync(packet, ct);

        // Collect contacts until ContactsEnd
        while (!ct.IsCancellationRequested)
        {
            var response = await WaitForResponseAsync(null, 5000, ct);
            var code = PacketParser.GetBaseCode(response);

            if (code == (byte)Response.ContactsEnd)
                break;

            if (code == (byte)Response.Contact)
            {
                var contact = PacketParser.ParseContact(response);
                if (contact != null)
                    contacts.Add(contact);
            }
        }

        return contacts;
    }

    /// <summary>
    /// Configures a channel.
    /// </summary>
    /// <param name="channelIndex">Channel index (0-7).</param>
    /// <param name="name">Channel name.</param>
    /// <param name="secret">16-byte channel secret.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<bool> SetChannelAsync(byte channelIndex, string name, byte[] secret, CancellationToken ct = default)
    {
        var packet = PacketBuilder.SetChannel(channelIndex, name, secret);
        var response = await SendCommandAsync(packet, Response.Ok, 2000, ct);
        return PacketParser.IsOk(response);
    }

    /// <summary>
    /// Gets channel configuration.
    /// </summary>
    /// <param name="channelIndex">Channel index (0-7).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<Channel?> GetChannelAsync(byte channelIndex, CancellationToken ct = default)
    {
        try
        {
            var packet = PacketBuilder.GetChannel(channelIndex);
            var response = await SendCommandAsync(packet, Response.ChannelInfo, 2000, ct);
            
            if (VerboseLogging)
                Console.WriteLine($"[MeshRadio] GetChannel response: {BitConverter.ToString(response).Replace("-", "")}");
            
            return PacketParser.ParseChannelInfo(response);
        }
        catch (TimeoutException)
        {
            Console.WriteLine("[MeshRadio] GetChannel timed out (device may not support GetChannel)");
            return null;
        }
    }

    /// <summary>
    /// Gets the device's current time.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task<DateTime?> GetTimeAsync(CancellationToken ct = default)
    {
        var packet = PacketBuilder.GetTime();
        var response = await SendCommandAsync(packet, Response.Time, 2000, ct);
        var unixTime = PacketParser.ParseTime(response);
        return unixTime != null ? DateTimeOffset.FromUnixTimeSeconds(unixTime.Value).UtcDateTime : null;
    }

    /// <summary>
    /// Sets the device's time to the current system time.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task<bool> SetTimeAsync(CancellationToken ct = default)
    {
        var packet = PacketBuilder.SetTimeNow();
        var response = await SendCommandAsync(packet, Response.Ok, 2000, ct);
        return PacketParser.IsOk(response);
    }

    /// <summary>
    /// Broadcasts a self-advertisement.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task<bool> SendAdvertAsync(CancellationToken ct = default)
    {
        var packet = PacketBuilder.SendAdvert();
        var response = await SendCommandAsync(packet, Response.Ok, 2000, ct);
        return PacketParser.IsOk(response);
    }

    /// <summary>
    /// Sets the device's advertised name.
    /// </summary>
    /// <param name="name">New name (up to 32 characters).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<bool> SetNameAsync(string name, CancellationToken ct = default)
    {
        var packet = PacketBuilder.SetName(name);
        var response = await SendCommandAsync(packet, Response.Ok, 2000, ct);
        return PacketParser.IsOk(response);
    }

    /// <summary>
    /// Reboots the device.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task RebootAsync(CancellationToken ct = default)
    {
        await SendRawAsync(PacketBuilder.Reboot(), ct);
    }

    /// <summary>
    /// Sets the device's GPS coordinates.
    /// </summary>
    /// <param name="latitude">Latitude in decimal degrees.</param>
    /// <param name="longitude">Longitude in decimal degrees.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<bool> SetCoordsAsync(double latitude, double longitude, CancellationToken ct = default)
    {
        var packet = PacketBuilder.SetCoords(latitude, longitude);
        var response = await SendCommandAsync(packet, Response.Ok, 2000, ct);
        return PacketParser.IsOk(response);
    }

    /// <summary>
    /// Sets the device's transmit power.
    /// </summary>
    /// <param name="powerDbm">TX power in dBm.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<bool> SetTxPowerAsync(sbyte powerDbm, CancellationToken ct = default)
    {
        var packet = PacketBuilder.SetTxPower(powerDbm);
        var response = await SendCommandAsync(packet, Response.Ok, 2000, ct);
        return PacketParser.IsOk(response);
    }

    /// <summary>
    /// Gets the device's battery status.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task<BatteryInfo?> GetBatteryAsync(CancellationToken ct = default)
    {
        var packet = PacketBuilder.GetBattery();
        var response = await SendCommandAsync(packet, Response.Battery, 2000, ct);
        return PacketParser.ParseBatteryInfo(response);
    }

    /// <summary>
    /// Factory resets the device (erases all settings and contacts).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task FactoryResetAsync(CancellationToken ct = default)
    {
        await SendRawAsync(PacketBuilder.FactoryReset(), ct);
    }

    /// <summary>
    /// Closes the transport connection.
    /// </summary>
    public void Disconnect()
    {
        _cts?.Cancel();
        try { _readTask?.Wait(1000); } catch { }
        if (_transport.IsOpen)
        {
            _transport.Close();
            Disconnected?.Invoke();
        }
    }

    /// <summary>
    /// Releases all resources used by the MeshRadio.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Disconnect();
        _cts?.Dispose();
        _transport.Dispose();
        _sendLock.Dispose();
    }

    private async Task<byte[]> SendCommandAsync(byte[] packet, Response? expectedResponse, int timeoutMs, CancellationToken ct)
    {
        await SendRawAsync(packet, ct);
        return await WaitForResponseAsync(expectedResponse, timeoutMs, ct);
    }

    private async Task SendRawAsync(byte[] packet, CancellationToken ct)
    {
        await _sendLock.WaitAsync(ct);
        try
        {
            if (VerboseLogging)
                Console.WriteLine($"[MeshRadio] TX: {BitConverter.ToString(packet).Replace("-", " ")}");

            if (_transport.RequiresCobsFraming)
            {
                // Serial: COBS encode with 0x00 delimiter
                var encoded = CobsEncode(packet);
                var frame = new byte[encoded.Length + 1];
                Buffer.BlockCopy(encoded, 0, frame, 0, encoded.Length);
                frame[^1] = 0x00;
                _transport.SendPacket(frame);
            }
            else
            {
                // TCP: transport handles framing
                _transport.SendPacket(packet);
            }

            PacketSent?.Invoke(packet);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task<byte[]> WaitForResponseAsync(Response? expectedResponse, int timeoutMs, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            if (_rxQueue.TryDequeue(out var packet))
            {
                var code = PacketParser.GetBaseCode(packet);

                // If no specific response expected, return any non-push
                if (!expectedResponse.HasValue && !PacketParser.IsPush(packet))
                    return packet;

                // If expected response matches
                if (expectedResponse.HasValue && code == (byte)expectedResponse.Value)
                    return packet;

                // Handle errors
                if (code == (byte)Response.Err)
                    return packet;

                // Re-queue non-matching responses so they aren't lost
                _rxQueue.Enqueue(packet);
            }
            await Task.Delay(10, ct);
        }

        throw new TimeoutException($"Timeout waiting for response {expectedResponse}");
    }

    private void ReadLoop(CancellationToken ct)
    {
        if (_transport.RequiresCobsFraming)
            ReadLoopSerial(ct);
        else
            ReadLoopTcp(ct);
    }

    private void ReadLoopSerial(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!_transport.IsOpen) break;

                var buffer = new byte[256];
                var read = _transport.ReceivePacket(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    Thread.Sleep(10);
                    continue;
                }

                for (var i = 0; i < read; i++)
                {
                    var b = buffer[i];
                    if (b == 0x00)
                    {
                        // Frame complete
                        if (_rxBuffer.Count > 0)
                        {
                            var decoded = CobsDecode([.. _rxBuffer]);
                            if (decoded.Length > 0)
                                ProcessPacket(decoded);
                            _rxBuffer.Clear();
                        }
                    }
                    else
                    {
                        _rxBuffer.Add(b);
                    }
                }
            }
            catch (TimeoutException) { }
            catch (Exception ex)
            {
                if (VerboseLogging)
                    Console.WriteLine($"[MeshRadio] Read error: {ex.Message}");
                ErrorOccurred?.Invoke(ex.Message);
            }
        }
    }

    private void ReadLoopTcp(CancellationToken ct)
    {
        var buffer = new byte[4096];
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!_transport.IsOpen) break;

                var read = _transport.ReceivePacket(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    Thread.Sleep(10);
                    continue;
                }

                // TCP transport returns complete packets, no COBS decoding needed
                var packet = new byte[read];
                Buffer.BlockCopy(buffer, 0, packet, 0, read);
                ProcessPacket(packet);
            }
            catch (TimeoutException) { }
            catch (Exception ex)
            {
                if (VerboseLogging)
                    Console.WriteLine($"[MeshRadio] Read error: {ex.Message}");
                ErrorOccurred?.Invoke(ex.Message);
            }
        }
    }

    private void ProcessPacket(byte[] packet)
    {
        if (packet.Length == 0) return;

        if (VerboseLogging)
            Console.WriteLine($"[MeshRadio] RX: {BitConverter.ToString(packet).Replace("-", " ")}");

        // Fire low-level packet event for debugging
        PacketReceived?.Invoke(packet);

        var code = packet[0];

        // Handle push notifications
        if (PacketParser.IsPush(packet))
        {
            if (code == (byte)Push.MsgWaiting)
            {
                MessageWaiting?.Invoke();
                return;
            }

            if (code == (byte)Push.Advert)
            {
                var advert = PacketParser.ParseAdvert(packet);
                if (advert != null)
                    AdvertReceived?.Invoke(advert);
                return;
            }

            if (code == (byte)Push.RawData)
            {
                // Raw data payload starts at byte 1
                if (packet.Length > 1)
                    RawDataReceived?.Invoke(packet[1..]);
                return;
            }

            if (code == (byte)Push.ChannelMsg || code == (byte)Push.ChannelMsgV3)
            {
                var msg = PacketParser.ParseChannelMessage(packet);
                if (msg != null)
                {
                    _channelMessageQueue.Enqueue(msg);
                    ChannelMessageReceived?.Invoke(msg);
                }
                return;
            }

            if (code == (byte)Push.ContactMsg || code == (byte)Push.ContactMsgV3)
            {
                var msg = PacketParser.ParseDirectMessage(packet);
                if (msg != null)
                {
                    _messageQueue.Enqueue(msg);
                    DirectMessageReceived?.Invoke(msg);
                }
                return;
            }
        }

        // Queue responses for command handling
        _rxQueue.Enqueue(packet);
    }

    private static byte[] CobsEncode(byte[] data)
    {
        var output = new List<byte>(data.Length + data.Length / 254 + 1);
        var codeIndex = 0;
        byte code = 1;
        output.Add(0); // Placeholder for first code

        foreach (var b in data)
        {
            if (b == 0)
            {
                output[codeIndex] = code;
                codeIndex = output.Count;
                output.Add(0);
                code = 1;
            }
            else
            {
                output.Add(b);
                code++;
                if (code == 255)
                {
                    output[codeIndex] = code;
                    codeIndex = output.Count;
                    output.Add(0);
                    code = 1;
                }
            }
        }
        output[codeIndex] = code;
        return [.. output];
    }

    private static byte[] CobsDecode(byte[] data)
    {
        var output = new List<byte>(data.Length);
        var i = 0;

        while (i < data.Length)
        {
            var code = data[i++];
            if (code == 0) break;

            for (var j = 1; j < code && i < data.Length; j++)
                output.Add(data[i++]);

            if (code < 255 && i < data.Length)
                output.Add(0);
        }

        // Remove trailing zero if present
        if (output.Count > 0 && output[^1] == 0)
            output.RemoveAt(output.Count - 1);

        return [.. output];
    }
}
