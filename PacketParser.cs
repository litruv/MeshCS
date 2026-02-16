using System.Text;

namespace MeshCS;

/// <summary>
/// Parses MeshCore response packets received from the companion radio.
/// </summary>
public static class PacketParser
{
    /// <summary>
    /// Gets the response code from a packet.
    /// </summary>
    public static byte GetCode(ReadOnlySpan<byte> packet) => packet.Length > 0 ? packet[0] : (byte)0xFF;

    /// <summary>
    /// Checks if this is a push notification (unsolicited event).
    /// </summary>
    public static bool IsPush(ReadOnlySpan<byte> packet) => packet.Length > 0 && (packet[0] & 0x80) != 0;

    /// <summary>
    /// Gets the base response code (strips push bit).
    /// </summary>
    public static byte GetBaseCode(ReadOnlySpan<byte> packet) => (byte)(GetCode(packet) & 0x7F);

    /// <summary>
    /// Parses a SelfInfo response (0x05).
    /// </summary>
    public static SelfInfo? ParseSelfInfo(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 36 || GetBaseCode(packet) != (byte)Response.SelfInfo)
            return null;

        var pos = 1;
        var pubKey = packet.Slice(pos, 32).ToArray();
        pos += 32;

        // Optional fields depend on packet length
        var lat = 0;
        var lon = 0;
        uint freq = 0;
        byte bw = 0, sf = 0, cr = 0;
        sbyte txPower = 0;
        var name = "";

        if (packet.Length >= pos + 4)
        {
            lat = ReadInt32LE(packet, ref pos);
        }
        if (packet.Length >= pos + 4)
        {
            lon = ReadInt32LE(packet, ref pos);
        }
        if (packet.Length >= pos + 4)
        {
            freq = ReadUInt32LE(packet, ref pos);
        }
        if (packet.Length >= pos + 4)
        {
            bw = packet[pos++];
            sf = packet[pos++];
            cr = packet[pos++];
            txPower = (sbyte)packet[pos++];
        }
        if (packet.Length > pos)
        {
            var nameEnd = packet[pos..].IndexOf((byte)0);
            name = nameEnd > 0
                ? Encoding.UTF8.GetString(packet.Slice(pos, nameEnd))
                : Encoding.UTF8.GetString(packet[pos..]);
        }

        return new SelfInfo
        {
            PublicKey = pubKey,
            Latitude = lat,
            Longitude = lon,
            Frequency = freq,
            Bandwidth = bw,
            SpreadingFactor = sf,
            CodingRate = cr,
            TxPower = txPower,
            Name = name.TrimEnd('\0')
        };
    }

    /// <summary>
    /// Parses a DeviceInfo response (0x0D).
    /// </summary>
    public static DeviceInfo? ParseDeviceInfo(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 2 || GetBaseCode(packet) != (byte)Response.DeviceInfo)
            return null;

        var pos = 1;
        var version = "";
        var model = "";
        ushort maxPacket = 230;
        byte protoVer = 3;

        // Parse null-terminated strings and values
        if (packet.Length > pos)
        {
            var versionEnd = packet[pos..].IndexOf((byte)0);
            if (versionEnd > 0)
            {
                version = Encoding.UTF8.GetString(packet.Slice(pos, versionEnd));
                pos += versionEnd + 1;
            }
        }
        if (packet.Length > pos)
        {
            var modelEnd = packet[pos..].IndexOf((byte)0);
            if (modelEnd > 0)
            {
                model = Encoding.UTF8.GetString(packet.Slice(pos, modelEnd));
                pos += modelEnd + 1;
            }
        }
        if (packet.Length >= pos + 2)
        {
            maxPacket = ReadUInt16LE(packet, ref pos);
        }
        if (packet.Length > pos)
        {
            protoVer = packet[pos];
        }

        return new DeviceInfo
        {
            FirmwareVersion = version,
            Model = model,
            MaxPacketSize = maxPacket,
            ProtocolVersion = protoVer
        };
    }

    /// <summary>
    /// Parses a Contact response (0x03).
    /// </summary>
    public static Contact? ParseContact(ReadOnlySpan<byte> packet)
    {
        // Contact: code(1) + pubkey(32) + type(1) + flags(1) + pathLen(1) + path(16) + name(32) + lastSeen(4)
        if (packet.Length < 88 || GetBaseCode(packet) != (byte)Response.Contact)
            return null;

        var pos = 1;
        var pubKey = packet.Slice(pos, 32).ToArray();
        pos += 32;
        var type = (ContactType)packet[pos++];
        var flags = packet[pos++];
        var pathLen = packet[pos++];
        var path = packet.Slice(pos, 16).ToArray();
        pos += 16;
        var nameBytes = packet.Slice(pos, 32);
        pos += 32;
        var lastSeen = ReadUInt32LE(packet, ref pos);

        var nameEnd = nameBytes.IndexOf((byte)0);
        var name = nameEnd > 0
            ? Encoding.UTF8.GetString(nameBytes[..nameEnd])
            : Encoding.UTF8.GetString(nameBytes);

        return new Contact
        {
            PublicKey = pubKey,
            Type = type,
            Flags = flags,
            PathLen = pathLen,
            Path = path,
            Name = name.TrimEnd('\0'),
            LastSeen = lastSeen
        };
    }

    /// <summary>
    /// Parses a ChannelInfo response (0x12).
    /// </summary>
    public static Channel? ParseChannelInfo(ReadOnlySpan<byte> packet)
    {
        // ChannelInfo: code(1) + index(1) + name(32) + secret(16) + [flags(1) optional]
        // Some firmware versions omit the flags byte
        if (packet.Length < 50 || GetBaseCode(packet) != (byte)Response.ChannelInfo)
            return null;

        var pos = 1;
        var index = packet[pos++];
        var nameBytes = packet.Slice(pos, 32);
        pos += 32;
        var secret = packet.Slice(pos, 16).ToArray();
        pos += 16;
        var forwardEnabled = packet.Length > pos && (packet[pos] & 0x01) != 0;

        var nameEnd = nameBytes.IndexOf((byte)0);
        var name = nameEnd > 0
            ? Encoding.UTF8.GetString(nameBytes[..nameEnd])
            : Encoding.UTF8.GetString(nameBytes);

        return new Channel
        {
            Index = index,
            Name = name.TrimEnd('\0'),
            Secret = secret,
            ForwardEnabled = forwardEnabled
        };
    }

    /// <summary>
    /// Parses a DirectMessage (0x06, 0x10, 0x86, 0x90).
    /// </summary>
    public static DirectMessage? ParseDirectMessage(ReadOnlySpan<byte> packet)
    {
        var code = GetBaseCode(packet);
        if (code != (byte)Response.ContactMsg && code != (byte)Response.ContactMsgV3)
            return null;

        var isV3 = code == (byte)Response.ContactMsgV3;
        var pos = 1;

        // V3: code(1) + txtType(1) + senderPubKey(6) + pathLen(1) + senderTs(4) + snr(1) + nameLen(1) + name... + payload
        // V2: code(1) + senderPubKey(6) + pathLen(1) + txtType(1) + senderTs(4) + payload
        byte[] senderPrefix;
        TextType textType;
        byte pathLen;
        uint timestamp;
        sbyte snr = 0;
        string senderName = "";
        byte[] payload;

        if (isV3)
        {
            if (packet.Length < 16) return null;

            textType = (TextType)packet[pos++];
            senderPrefix = packet.Slice(pos, 6).ToArray();
            pos += 6;
            pathLen = packet[pos++];
            timestamp = ReadUInt32LE(packet, ref pos);
            snr = (sbyte)packet[pos++];

            var nameLen = packet[pos++];
            if (nameLen > 0 && packet.Length >= pos + nameLen)
            {
                senderName = Encoding.UTF8.GetString(packet.Slice(pos, nameLen));
                pos += nameLen;
            }

            payload = packet[pos..].ToArray();
        }
        else
        {
            if (packet.Length < 13) return null;

            senderPrefix = packet.Slice(pos, 6).ToArray();
            pos += 6;
            pathLen = packet[pos++];
            textType = (TextType)packet[pos++];
            timestamp = ReadUInt32LE(packet, ref pos);
            payload = packet[pos..].ToArray();
        }

        return new DirectMessage
        {
            SenderPrefix = senderPrefix,
            TextType = textType,
            PathLen = pathLen,
            Timestamp = timestamp,
            Snr = snr,
            SenderName = senderName.TrimEnd('\0'),
            Payload = payload,
            IsV3 = isV3
        };
    }

    /// <summary>
    /// Parses a ChannelMessage (0x08, 0x11, 0x88, 0x91).
    /// </summary>
    public static ChannelMessage? ParseChannelMessage(ReadOnlySpan<byte> packet)
    {
        var code = GetBaseCode(packet);
        if (code != (byte)Response.ChannelMsg && code != (byte)Response.ChannelMsgV3)
            return null;

        var isV3 = code == (byte)Response.ChannelMsgV3;
        var pos = 1;

        // V3: code(1) + txtType(1) + channelIdx(1) + pathLen(1) + senderTs(4) + snr(1) + nameLen(1) + name... + payload
        // V2: code(1) + channelIdx(1) + pathLen(1) + txtType(1) + senderTs(4) + senderPubKey(32) + ": " + text
        byte channelIndex;
        TextType textType;
        byte pathLen;
        uint timestamp;
        sbyte snr = 0;
        string senderName = "";
        byte[] senderPubKey = [];
        byte[] payload;

        if (isV3)
        {
            if (packet.Length < 11) return null;

            textType = (TextType)packet[pos++];
            channelIndex = packet[pos++];
            pathLen = packet[pos++];
            timestamp = ReadUInt32LE(packet, ref pos);
            snr = (sbyte)packet[pos++];

            var nameLen = packet[pos++];
            if (nameLen > 0 && packet.Length >= pos + nameLen)
            {
                senderName = Encoding.UTF8.GetString(packet.Slice(pos, nameLen));
                pos += nameLen;
            }

            payload = packet[pos..].ToArray();
        }
        else
        {
            if (packet.Length < 8) return null;

            channelIndex = packet[pos++];
            pathLen = packet[pos++];
            textType = (TextType)packet[pos++];
            timestamp = ReadUInt32LE(packet, ref pos);
            
            // V2 payload format: senderBlob + ": " (0x3A 0x20) + text
            // The sender blob is a variable-length identity (pubkey hash/suffix).
            // Search for the ": " separator to split sender from message.
            var rawPayload = packet[pos..];
            var separatorIdx = -1;
            for (var i = 0; i < rawPayload.Length - 1; i++)
            {
                if (rawPayload[i] == (byte)':' && rawPayload[i + 1] == (byte)' ')
                {
                    separatorIdx = i;
                    break;
                }
            }

            if (separatorIdx > 0)
            {
                senderPubKey = rawPayload[..separatorIdx].ToArray();
                payload = rawPayload[(separatorIdx + 2)..].ToArray();
            }
            else
            {
                payload = rawPayload.ToArray();
            }
        }

        return new ChannelMessage
        {
            ChannelIndex = channelIndex,
            TextType = textType,
            PathLen = pathLen,
            Timestamp = timestamp,
            Snr = snr,
            SenderName = senderName.TrimEnd('\0'),
            SenderPubKey = senderPubKey,
            Payload = payload,
            IsV3 = isV3
        };
    }

    /// <summary>
    /// Parses an Advert push (0x80).
    /// </summary>
    public static Advert? ParseAdvert(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 43 || GetCode(packet) != (byte)Push.Advert)
            return null;

        var pos = 1;
        var pubKey = packet.Slice(pos, 32).ToArray();
        pos += 32;
        var lat = ReadInt32LE(packet, ref pos);
        var lon = ReadInt32LE(packet, ref pos);
        var pathLen = packet[pos++];
        var snr = (sbyte)packet[pos++];

        var name = "";
        if (packet.Length > pos)
        {
            var nameEnd = packet[pos..].IndexOf((byte)0);
            name = nameEnd > 0
                ? Encoding.UTF8.GetString(packet.Slice(pos, nameEnd))
                : Encoding.UTF8.GetString(packet[pos..]);
        }

        return new Advert
        {
            PublicKey = pubKey,
            Latitude = lat,
            Longitude = lon,
            PathLen = pathLen,
            Snr = snr,
            Name = name.TrimEnd('\0')
        };
    }

    /// <summary>
    /// Parses a Time response (0x09).
    /// </summary>
    public static uint? ParseTime(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 5 || GetBaseCode(packet) != (byte)Response.Time)
            return null;

        var pos = 1;
        return ReadUInt32LE(packet, ref pos);
    }

    /// <summary>
    /// Parses a Battery response packet.
    /// </summary>
    public static BatteryInfo? ParseBatteryInfo(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 5 || GetBaseCode(packet) != (byte)Response.Battery)
            return null;

        var pos = 1;
        var voltage = ReadUInt16LE(packet, ref pos);
        var percentage = packet[pos++];
        var isCharging = packet.Length > pos && packet[pos++] != 0;
        var freeStorage = packet.Length >= pos + 4 ? ReadUInt32LE(packet, ref pos) : 0u;

        return new BatteryInfo
        {
            VoltageMillivolts = voltage,
            Percentage = percentage,
            IsCharging = isCharging,
            FreeStorageBytes = freeStorage
        };
    }

    /// <summary>
    /// Checks if this is a Sent acknowledgment (0x07).
    /// </summary>
    public static bool IsSent(ReadOnlySpan<byte> packet) =>
        packet.Length > 0 && GetBaseCode(packet) == (byte)Response.Sent;

    /// <summary>
    /// Checks if this is an Error response (0x01).
    /// </summary>
    public static bool IsError(ReadOnlySpan<byte> packet) =>
        packet.Length > 0 && GetBaseCode(packet) == (byte)Response.Err;

    /// <summary>
    /// Checks if this is an Ok response (0x00).
    /// </summary>
    public static bool IsOk(ReadOnlySpan<byte> packet) =>
        packet.Length > 0 && GetBaseCode(packet) == (byte)Response.Ok;

    /// <summary>
    /// Checks if this is a NoMoreMessages response (0x0A).
    /// </summary>
    public static bool IsNoMoreMessages(ReadOnlySpan<byte> packet) =>
        packet.Length > 0 && GetBaseCode(packet) == (byte)Response.NoMoreMsgs;

    /// <summary>
    /// Checks if this is a MessageWaiting push (0x83).
    /// </summary>
    public static bool IsMessageWaiting(ReadOnlySpan<byte> packet) =>
        packet.Length > 0 && GetCode(packet) == (byte)Push.MsgWaiting;

    private static uint ReadUInt32LE(ReadOnlySpan<byte> data, ref int pos)
    {
        var value = (uint)(data[pos] | (data[pos + 1] << 8) | (data[pos + 2] << 16) | (data[pos + 3] << 24));
        pos += 4;
        return value;
    }

    private static int ReadInt32LE(ReadOnlySpan<byte> data, ref int pos)
    {
        return (int)ReadUInt32LE(data, ref pos);
    }

    private static ushort ReadUInt16LE(ReadOnlySpan<byte> data, ref int pos)
    {
        var value = (ushort)(data[pos] | (data[pos + 1] << 8));
        pos += 2;
        return value;
    }
}
