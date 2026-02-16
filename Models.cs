using System.Text;

namespace MeshCS;

/// <summary>
/// Device self-info returned after AppStart command.
/// </summary>
public sealed class SelfInfo
{
    /// <summary>32-byte public key of the device.</summary>
    public byte[] PublicKey { get; init; } = new byte[32];

    /// <summary>Advertised name of the device.</summary>
    public string Name { get; init; } = "";

    /// <summary>Latitude in millionths of degrees (divide by 1e6 for decimal).</summary>
    public int Latitude { get; init; }

    /// <summary>Longitude in millionths of degrees (divide by 1e6 for decimal).</summary>
    public int Longitude { get; init; }

    /// <summary>Radio frequency in Hz.</summary>
    public uint Frequency { get; init; }

    /// <summary>LoRa bandwidth setting (0-9).</summary>
    public byte Bandwidth { get; init; }

    /// <summary>LoRa spreading factor (6-12).</summary>
    public byte SpreadingFactor { get; init; }

    /// <summary>LoRa coding rate (5-8).</summary>
    public byte CodingRate { get; init; }

    /// <summary>TX power in dBm.</summary>
    public sbyte TxPower { get; init; }

    /// <summary>Gets the public key as a hex string.</summary>
    public string PublicKeyHex => BitConverter.ToString(PublicKey).Replace("-", "");

    /// <summary>Gets the 6-byte prefix of the public key as a hex string.</summary>
    public string PublicKeyPrefix => BitConverter.ToString(PublicKey[..6]).Replace("-", "");
}

/// <summary>
/// Device info returned from DeviceQuery command.
/// </summary>
public sealed class DeviceInfo
{
    /// <summary>Firmware version string.</summary>
    public string FirmwareVersion { get; init; } = "";

    /// <summary>Device model/type identifier.</summary>
    public string Model { get; init; } = "";

    /// <summary>Maximum packet size supported.</summary>
    public ushort MaxPacketSize { get; init; }

    /// <summary>Protocol version supported.</summary>
    public byte ProtocolVersion { get; init; }
}

/// <summary>
/// Contact entry from the device's contact list.
/// </summary>
public sealed class Contact
{
    /// <summary>32-byte public key of the contact.</summary>
    public byte[] PublicKey { get; init; } = new byte[32];

    /// <summary>Contact type (Chat, Room, Repeater).</summary>
    public ContactType Type { get; init; }

    /// <summary>Contact flags.</summary>
    public byte Flags { get; init; }

    /// <summary>Path length to this contact.</summary>
    public byte PathLen { get; init; }

    /// <summary>Routing path data (up to 16 bytes).</summary>
    public byte[] Path { get; init; } = [];

    /// <summary>Display name of the contact.</summary>
    public string Name { get; init; } = "";

    /// <summary>Last seen timestamp (Unix epoch seconds).</summary>
    public uint LastSeen { get; init; }

    /// <summary>Gets the public key as a hex string.</summary>
    public string PublicKeyHex => BitConverter.ToString(PublicKey).Replace("-", "");

    /// <summary>Gets the 6-byte prefix as hex (for message addressing).</summary>
    public string PublicKeyPrefix => BitConverter.ToString(PublicKey[..6]).Replace("-", "");
}

/// <summary>
/// Channel configuration.
/// </summary>
public sealed class Channel
{
    /// <summary>Channel index (0-7).</summary>
    public byte Index { get; init; }

    /// <summary>Channel name (up to 32 characters).</summary>
    public string Name { get; init; } = "";

    /// <summary>16-byte channel secret/key.</summary>
    public byte[] Secret { get; init; } = new byte[16];

    /// <summary>Whether channel messages should be forwarded.</summary>
    public bool ForwardEnabled { get; init; }

    /// <summary>Gets the secret as a hex string.</summary>
    public string SecretHex => BitConverter.ToString(Secret).Replace("-", "");
}

/// <summary>
/// Received direct message from a contact.
/// </summary>
public sealed class DirectMessage
{
    /// <summary>First 6 bytes of the sender's public key.</summary>
    public byte[] SenderPrefix { get; init; } = new byte[6];

    /// <summary>Text message type.</summary>
    public TextType TextType { get; init; }

    /// <summary>Number of hops the message traversed.</summary>
    public byte PathLen { get; init; }

    /// <summary>Sender's local timestamp (Unix epoch seconds).</summary>
    public uint Timestamp { get; init; }

    /// <summary>Signal-to-noise ratio (V3 only).</summary>
    public sbyte Snr { get; init; }

    /// <summary>Raw payload bytes.</summary>
    public byte[] Payload { get; init; } = [];

    /// <summary>Sender's display name (if available).</summary>
    public string SenderName { get; init; } = "";

    /// <summary>Gets the sender prefix as hex string.</summary>
    public string SenderPrefixHex => BitConverter.ToString(SenderPrefix).Replace("-", "");

    /// <summary>Gets the payload as UTF-8 text.</summary>
    public string Text => Encoding.UTF8.GetString(Payload).TrimEnd('\0');

    /// <summary>Whether this is a V3 format message.</summary>
    public bool IsV3 { get; init; }
}

/// <summary>
/// Received message from a channel.
/// </summary>
public sealed class ChannelMessage
{
    /// <summary>Channel index the message was received on.</summary>
    public byte ChannelIndex { get; init; }

    /// <summary>Text message type.</summary>
    public TextType TextType { get; init; }

    /// <summary>Number of hops the message traversed.</summary>
    public byte PathLen { get; init; }

    /// <summary>Sender's local timestamp (Unix epoch seconds).</summary>
    public uint Timestamp { get; init; }

    /// <summary>Signal-to-noise ratio (V3 only).</summary>
    public sbyte Snr { get; init; }

    /// <summary>Raw payload bytes.</summary>
    public byte[] Payload { get; init; } = [];

    /// <summary>Sender's display name (if available, V3 only).</summary>
    public string SenderName { get; init; } = "";

    /// <summary>Sender's 32-byte public key (V2 only).</summary>
    public byte[] SenderPubKey { get; init; } = [];

    /// <summary>Gets the sender's public key as hex string.</summary>
    public string SenderPubKeyHex => SenderPubKey.Length > 0 
        ? BitConverter.ToString(SenderPubKey).Replace("-", "") 
        : "";

    /// <summary>Gets the payload as UTF-8 text.</summary>
    public string Text => Encoding.UTF8.GetString(Payload).TrimEnd('\0');

    /// <summary>Whether this is a V3 format message.</summary>
    public bool IsV3 { get; init; }
}

/// <summary>
/// Received advertisement from the mesh.
/// </summary>
public sealed class Advert
{
    /// <summary>32-byte public key of the advertising node.</summary>
    public byte[] PublicKey { get; init; } = new byte[32];

    /// <summary>Advertised name.</summary>
    public string Name { get; init; } = "";

    /// <summary>Latitude in millionths of degrees.</summary>
    public int Latitude { get; init; }

    /// <summary>Longitude in millionths of degrees.</summary>
    public int Longitude { get; init; }

    /// <summary>Path length to this node.</summary>
    public byte PathLen { get; init; }

    /// <summary>Signal-to-noise ratio.</summary>
    public sbyte Snr { get; init; }

    /// <summary>Gets the public key as hex.</summary>
    public string PublicKeyHex => BitConverter.ToString(PublicKey).Replace("-", "");
}

/// <summary>
/// Battery and storage info.
/// </summary>
public sealed class BatteryInfo
{
    /// <summary>Battery voltage in millivolts.</summary>
    public ushort VoltageMillivolts { get; init; }

    /// <summary>Battery percentage (0-100).</summary>
    public byte Percentage { get; init; }

    /// <summary>Whether device is charging.</summary>
    public bool IsCharging { get; init; }

    /// <summary>Free storage bytes.</summary>
    public uint FreeStorageBytes { get; init; }
}
