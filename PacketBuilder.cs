using System.Text;

namespace MeshCS;

/// <summary>
/// Builds MeshCore command packets to send to the companion radio.
/// All methods return byte arrays ready for transmission.
/// </summary>
public static class PacketBuilder
{
    private const int MaxPathSize = 16;
    private const int MaxNameSize = 32;
    private const int MaxChannelSecret = 16;

    /// <summary>
    /// Builds an AppStart packet to initialize the companion session.
    /// </summary>
    /// <param name="appName">Application name (up to 32 chars).</param>
    /// <param name="targetVersion">Target protocol version (default 3 for V3 messages).</param>
    public static byte[] AppStart(string appName = "MeshCore", byte targetVersion = 3)
    {
        var nameBytes = Encoding.UTF8.GetBytes(appName);
        var data = new byte[1 + 7 + nameBytes.Length];
        data[0] = (byte)Command.AppStart;
        // data[1..8] = reserved (7 bytes, zeros)
        Buffer.BlockCopy(nameBytes, 0, data, 8, Math.Min(nameBytes.Length, MaxNameSize));
        return data;
    }

    /// <summary>
    /// Builds a DeviceQuery packet.
    /// </summary>
    /// <param name="targetVersion">Target protocol version (0-3).</param>
    public static byte[] DeviceQuery(byte targetVersion = 3)
    {
        return [(byte)Command.DeviceQuery, targetVersion];
    }

    /// <summary>
    /// Builds a SendMsg packet to send a DM to a contact.
    /// </summary>
    /// <param name="recipientPubKeyPrefix">First 6 bytes of recipient's public key.</param>
    /// <param name="text">Message text.</param>
    /// <param name="textType">Text type (Plain or CliData).</param>
    /// <param name="timestamp">Unix timestamp (null = use current time).</param>
    public static byte[] SendMsg(byte[] recipientPubKeyPrefix, string text, TextType textType = TextType.Plain, uint? timestamp = null)
    {
        if (recipientPubKeyPrefix.Length < 6)
            throw new ArgumentException("Recipient prefix must be at least 6 bytes", nameof(recipientPubKeyPrefix));

        var textBytes = Encoding.UTF8.GetBytes(text);
        var ts = timestamp ?? (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Format: cmd(1) + txtType(1) + attempt(1) + timestamp(4) + pubkey(6) + payload
        var data = new byte[1 + 1 + 1 + 4 + 6 + textBytes.Length];
        var pos = 0;

        data[pos++] = (byte)Command.SendMsg;
        data[pos++] = (byte)textType;
        data[pos++] = 0; // attempt = 0
        WriteUInt32LE(data, ref pos, ts);
        Buffer.BlockCopy(recipientPubKeyPrefix, 0, data, pos, 6);
        pos += 6;
        Buffer.BlockCopy(textBytes, 0, data, pos, textBytes.Length);

        return data;
    }

    /// <summary>
    /// Builds a SendMsg packet using raw payload bytes.
    /// </summary>
    public static byte[] SendMsgRaw(byte[] recipientPubKeyPrefix, byte[] payload, TextType textType = TextType.CliData, uint? timestamp = null)
    {
        if (recipientPubKeyPrefix.Length < 6)
            throw new ArgumentException("Recipient prefix must be at least 6 bytes", nameof(recipientPubKeyPrefix));

        var ts = timestamp ?? (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var data = new byte[1 + 1 + 1 + 4 + 6 + payload.Length];
        var pos = 0;

        data[pos++] = (byte)Command.SendMsg;
        data[pos++] = (byte)textType;
        data[pos++] = 0;
        WriteUInt32LE(data, ref pos, ts);
        Buffer.BlockCopy(recipientPubKeyPrefix, 0, data, pos, 6);
        pos += 6;
        Buffer.BlockCopy(payload, 0, data, pos, payload.Length);

        return data;
    }

    /// <summary>
    /// Builds a SendChannelMsg packet to send a message to a channel.
    /// </summary>
    /// <param name="channelIndex">Channel index (0-7).</param>
    /// <param name="text">Message text.</param>
    /// <param name="textType">Text type (Plain or CliData).</param>
    /// <param name="timestamp">Unix timestamp (null = use current time).</param>
    public static byte[] SendChannelMsg(byte channelIndex, string text, TextType textType = TextType.Plain, uint? timestamp = null)
    {
        var textBytes = Encoding.UTF8.GetBytes(text);
        var ts = timestamp ?? (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Format: cmd(1) + txtType(1) + channelIdx(1) + timestamp(4) + payload
        var data = new byte[1 + 1 + 1 + 4 + textBytes.Length];
        var pos = 0;

        data[pos++] = (byte)Command.SendChannelMsg;
        data[pos++] = (byte)textType;
        data[pos++] = channelIndex;
        WriteUInt32LE(data, ref pos, ts);
        Buffer.BlockCopy(textBytes, 0, data, pos, textBytes.Length);

        return data;
    }

    /// <summary>
    /// Builds a GetMsg (SyncNextMessage) packet to fetch the next waiting message.
    /// </summary>
    public static byte[] GetMsg() => [(byte)Command.GetMsg];

    /// <summary>
    /// Builds a GetContacts packet.
    /// </summary>
    /// <param name="since">Only return contacts updated since this timestamp (0 = all).</param>
    public static byte[] GetContacts(uint since = 0)
    {
        var data = new byte[5];
        var pos = 0;
        data[pos++] = (byte)Command.GetContacts;
        WriteUInt32LE(data, ref pos, since);
        return data;
    }

    /// <summary>
    /// Builds an UpdateContact packet to add or update a contact.
    /// </summary>
    /// <param name="publicKey">32-byte public key of the contact.</param>
    /// <param name="name">Display name (up to 32 chars).</param>
    /// <param name="type">Contact type (Chat, Room, Repeater).</param>
    /// <param name="flags">Contact flags.</param>
    public static byte[] UpdateContact(byte[] publicKey, string name, ContactType type = ContactType.Chat, byte flags = 0)
    {
        if (publicKey.Length != 32)
            throw new ArgumentException("Public key must be exactly 32 bytes", nameof(publicKey));

        var nameBytes = Encoding.UTF8.GetBytes(name);
        // Format: cmd(1) + pubkey(32) + type(1) + flags(1) + pathLen(1) + path(16) + name(32) + timestamp(4)
        var data = new byte[1 + 32 + 1 + 1 + 1 + MaxPathSize + MaxNameSize + 4];
        var pos = 0;

        data[pos++] = (byte)Command.UpdateContact;
        Buffer.BlockCopy(publicKey, 0, data, pos, 32);
        pos += 32;
        data[pos++] = (byte)type;
        data[pos++] = flags;
        data[pos++] = 0xFF; // pathLen = -1 (unknown/auto)
        pos += MaxPathSize; // path (zeros = auto-discover)
        Buffer.BlockCopy(nameBytes, 0, data, pos, Math.Min(nameBytes.Length, MaxNameSize));
        pos += MaxNameSize;
        // timestamp = 0 (zeros, means "now")

        return data;
    }

    /// <summary>
    /// Builds a RemoveContact packet.
    /// </summary>
    /// <param name="publicKeyPrefix">First 6 bytes of the contact's public key.</param>
    public static byte[] RemoveContact(byte[] publicKeyPrefix)
    {
        if (publicKeyPrefix.Length < 6)
            throw new ArgumentException("Public key prefix must be at least 6 bytes", nameof(publicKeyPrefix));

        var data = new byte[7];
        data[0] = (byte)Command.RemoveContact;
        Buffer.BlockCopy(publicKeyPrefix, 0, data, 1, 6);
        return data;
    }

    /// <summary>
    /// Builds a GetChannel packet to query channel configuration.
    /// </summary>
    /// <param name="channelIndex">Channel index (0-7).</param>
    public static byte[] GetChannel(byte channelIndex) => [(byte)Command.GetChannel, channelIndex];

    /// <summary>
    /// Builds a SetChannel packet to configure a channel.
    /// </summary>
    /// <param name="channelIndex">Channel index (0-7).</param>
    /// <param name="name">Channel name (up to 32 chars).</param>
    /// <param name="secret">16-byte channel secret.</param>
    public static byte[] SetChannel(byte channelIndex, string name, byte[] secret)
    {
        if (secret.Length != 16)
            throw new ArgumentException("Channel secret must be exactly 16 bytes", nameof(secret));

        var nameBytes = Encoding.UTF8.GetBytes(name);
        // Format: cmd(1) + channelIdx(1) + name(32) + secret(16)
        var data = new byte[1 + 1 + MaxNameSize + MaxChannelSecret];
        var pos = 0;

        data[pos++] = (byte)Command.SetChannel;
        data[pos++] = channelIndex;
        Buffer.BlockCopy(nameBytes, 0, data, pos, Math.Min(nameBytes.Length, MaxNameSize));
        pos += MaxNameSize;
        Buffer.BlockCopy(secret, 0, data, pos, 16);

        return data;
    }

    /// <summary>
    /// Builds a SetChannel packet using a hex string for the secret.
    /// </summary>
    public static byte[] SetChannel(byte channelIndex, string name, string secretHex)
    {
        return SetChannel(channelIndex, name, Convert.FromHexString(secretHex));
    }

    /// <summary>
    /// Builds a GetTime packet.
    /// </summary>
    public static byte[] GetTime() => [(byte)Command.GetTime];

    /// <summary>
    /// Builds a SetTime packet.
    /// </summary>
    /// <param name="unixTime">Unix timestamp to set.</param>
    public static byte[] SetTime(uint unixTime)
    {
        var data = new byte[5];
        var pos = 0;
        data[pos++] = (byte)Command.SetTime;
        WriteUInt32LE(data, ref pos, unixTime);
        return data;
    }

    /// <summary>
    /// Builds a SetTime packet using current system time.
    /// </summary>
    public static byte[] SetTimeNow() => SetTime((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    /// <summary>
    /// Builds a GetBattery packet.
    /// </summary>
    public static byte[] GetBattery() => [(byte)Command.GetBattery];

    /// <summary>
    /// Builds a SendAdvert packet to broadcast a self-advertisement.
    /// </summary>
    public static byte[] SendAdvert() => [(byte)Command.SendAdvert];

    /// <summary>
    /// Builds a SetName packet to change the device's advertised name.
    /// </summary>
    /// <param name="name">New name (up to 32 chars).</param>
    public static byte[] SetName(string name)
    {
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var data = new byte[1 + MaxNameSize];
        data[0] = (byte)Command.SetName;
        Buffer.BlockCopy(nameBytes, 0, data, 1, Math.Min(nameBytes.Length, MaxNameSize));
        return data;
    }

    /// <summary>
    /// Builds a Reboot packet to restart the device.
    /// </summary>
    public static byte[] Reboot() => [(byte)Command.Reboot];

    /// <summary>
    /// Builds a FactoryReset packet.
    /// </summary>
    public static byte[] FactoryReset() => [(byte)Command.FactoryReset];

    /// <summary>
    /// Builds a SetCoords packet to set the device's GPS coordinates.
    /// </summary>
    /// <param name="latitude">Latitude in decimal degrees.</param>
    /// <param name="longitude">Longitude in decimal degrees.</param>
    public static byte[] SetCoords(double latitude, double longitude)
    {
        var data = new byte[9];
        var pos = 0;
        data[pos++] = (byte)Command.SetCoords;
        WriteInt32LE(data, ref pos, (int)(latitude * 1_000_000));
        WriteInt32LE(data, ref pos, (int)(longitude * 1_000_000));
        return data;
    }

    /// <summary>
    /// Builds a SetTxPower packet.
    /// </summary>
    /// <param name="powerDbm">TX power in dBm.</param>
    public static byte[] SetTxPower(sbyte powerDbm) => [(byte)Command.SetTxPower, (byte)powerDbm];

    private static void WriteUInt32LE(byte[] buffer, ref int pos, uint value)
    {
        buffer[pos++] = (byte)(value & 0xFF);
        buffer[pos++] = (byte)((value >> 8) & 0xFF);
        buffer[pos++] = (byte)((value >> 16) & 0xFF);
        buffer[pos++] = (byte)((value >> 24) & 0xFF);
    }

    private static void WriteInt32LE(byte[] buffer, ref int pos, int value)
    {
        WriteUInt32LE(buffer, ref pos, (uint)value);
    }
}
