namespace MeshCS;

/// <summary>
/// MeshCore companion radio command codes (app → radio).
/// </summary>
public enum Command : byte
{
    /// <summary>Initialize companion session, receive SelfInfo back.</summary>
    AppStart = 0x01,
    /// <summary>Send a text/binary message to a contact.</summary>
    SendMsg = 0x02,
    /// <summary>Send a text message to a channel.</summary>
    SendChannelMsg = 0x03,
    /// <summary>Request the contact list (with optional 'since' timestamp).</summary>
    GetContacts = 0x04,
    /// <summary>Get the device's current time.</summary>
    GetTime = 0x05,
    /// <summary>Set the device's time.</summary>
    SetTime = 0x06,
    /// <summary>Broadcast a self-advert packet.</summary>
    SendAdvert = 0x07,
    /// <summary>Set advertisement name.</summary>
    SetName = 0x08,
    /// <summary>Add or update a contact.</summary>
    UpdateContact = 0x09,
    /// <summary>Poll for next waiting message.</summary>
    GetMsg = 0x0A,
    /// <summary>Set radio parameters.</summary>
    SetRadio = 0x0B,
    /// <summary>Set radio TX power.</summary>
    SetTxPower = 0x0C,
    /// <summary>Reset path for contact.</summary>
    ResetPath = 0x0D,
    /// <summary>Set advertisement lat/lon coordinates.</summary>
    SetCoords = 0x0E,
    /// <summary>Remove contact.</summary>
    RemoveContact = 0x0F,
    /// <summary>Share contact.</summary>
    ShareContact = 0x10,
    /// <summary>Export contact URI.</summary>
    ExportContact = 0x11,
    /// <summary>Import contact from URI.</summary>
    AddContact = 0x12,
    /// <summary>Reboot device.</summary>
    Reboot = 0x13,
    /// <summary>Get battery voltage and storage info.</summary>
    GetBattery = 0x14,
    /// <summary>Set tuning parameters.</summary>
    SetTuning = 0x15,
    /// <summary>Query device info and firmware version.</summary>
    DeviceQuery = 0x16,
    /// <summary>Export private key.</summary>
    ExportPrivKey = 0x17,
    /// <summary>Import private key.</summary>
    ImportPrivKey = 0x18,
    /// <summary>Send raw data (PAYLOAD_TYPE_RAW_CUSTOM).</summary>
    SendRawData = 0x19,
    /// <summary>Send login request.</summary>
    SendLogin = 0x1A,
    /// <summary>Send status request.</summary>
    SendStatusReq = 0x1B,
    /// <summary>Check if connection exists.</summary>
    HasConnection = 0x1C,
    /// <summary>Logout / disconnect.</summary>
    Logout = 0x1D,
    /// <summary>Get contact by public key.</summary>
    GetContactByKey = 0x1E,
    /// <summary>Get channel info by index.</summary>
    GetChannel = 0x1F,
    /// <summary>Set/join a channel by index, name, and secret.</summary>
    SetChannel = 0x20,
    /// <summary>Start signing.</summary>
    SignStart = 0x21,
    /// <summary>Sign data chunk.</summary>
    SignData = 0x22,
    /// <summary>Finish signing.</summary>
    SignFinish = 0x23,
    /// <summary>Send trace path.</summary>
    SendTracePath = 0x24,
    /// <summary>Set device BLE PIN.</summary>
    SetDevicePin = 0x25,
    /// <summary>Set other parameters (telemetry, adv_loc, etc).</summary>
    SetOtherParams = 0x26,
    /// <summary>Send telemetry request.</summary>
    SendTelemetryReq = 0x27,
    /// <summary>Get custom variables.</summary>
    GetCustomVars = 0x28,
    /// <summary>Set custom variable.</summary>
    SetCustomVar = 0x29,
    /// <summary>Get advertisement path.</summary>
    GetAdvertPath = 0x2A,
    /// <summary>Get tuning parameters.</summary>
    GetTuning = 0x2B,
    /// <summary>Send binary request.</summary>
    SendBinaryReq = 0x32,
    /// <summary>Factory reset.</summary>
    FactoryReset = 0x33,
    /// <summary>Send path discovery request.</summary>
    SendPathDiscovery = 0x34,
    /// <summary>Set flood scope.</summary>
    SetFloodScope = 0x36,
    /// <summary>Send control data.</summary>
    SendControlData = 0x37,
    /// <summary>Get statistics.</summary>
    GetStats = 0x38,
    /// <summary>Send anonymous request.</summary>
    SendAnonReq = 0x39,
    /// <summary>Set auto-add configuration.</summary>
    SetAutoAdd = 0x3A,
    /// <summary>Get auto-add configuration.</summary>
    GetAutoAdd = 0x3B,
}

/// <summary>
/// MeshCore response codes (radio → app, solicited).
/// </summary>
public enum Response : byte
{
    /// <summary>Generic success.</summary>
    Ok = 0x00,
    /// <summary>Generic error.</summary>
    Err = 0x01,
    /// <summary>Start of contacts list.</summary>
    ContactsStart = 0x02,
    /// <summary>Single contact entry.</summary>
    Contact = 0x03,
    /// <summary>End of contacts list.</summary>
    ContactsEnd = 0x04,
    /// <summary>Device self-info (pubkey, name, radio params).</summary>
    SelfInfo = 0x05,
    /// <summary>Received a direct text message (legacy v&lt;3).</summary>
    ContactMsg = 0x06,
    /// <summary>Message was sent to the radio for transmission.</summary>
    Sent = 0x07,
    /// <summary>Received a channel text message (legacy v&lt;3).</summary>
    ChannelMsg = 0x08,
    /// <summary>Current device time response.</summary>
    Time = 0x09,
    /// <summary>No more messages in the queue.</summary>
    NoMoreMsgs = 0x0A,
    /// <summary>Battery and storage info response.</summary>
    Battery = 0x14,
    /// <summary>Device info response.</summary>
    DeviceInfo = 0x0D,
    /// <summary>Received a direct text message (V3).</summary>
    ContactMsgV3 = 0x10,
    /// <summary>Received a channel text message (V3).</summary>
    ChannelMsgV3 = 0x11,
    /// <summary>Channel info response.</summary>
    ChannelInfo = 0x12,
}

/// <summary>
/// MeshCore push codes (radio → app, unsolicited).
/// Push codes have bit 7 set (0x80).
/// </summary>
public enum Push : byte
{
    /// <summary>A new advert was received.</summary>
    Advert = 0x80,
    /// <summary>A message is waiting to be synced.</summary>
    MsgWaiting = 0x83,
    /// <summary>Raw data received from the mesh.</summary>
    RawData = 0x84,
    /// <summary>Received a direct text message (legacy, pushed).</summary>
    ContactMsg = 0x86,
    /// <summary>Received a channel text message (legacy, pushed).</summary>
    ChannelMsg = 0x88,
    /// <summary>Received a direct text message (V3, pushed).</summary>
    ContactMsgV3 = 0x90,
    /// <summary>Received a channel text message (V3, pushed).</summary>
    ChannelMsgV3 = 0x91,
}

/// <summary>
/// Text message type codes.
/// </summary>
public enum TextType : byte
{
    /// <summary>Plain text message.</summary>
    Plain = 0x00,
    /// <summary>CLI/binary data message.</summary>
    CliData = 0x01,
}

/// <summary>
/// Contact type codes.
/// </summary>
public enum ContactType : byte
{
    /// <summary>Room/group contact.</summary>
    Room = 0x00,
    /// <summary>Chat/direct message contact.</summary>
    Chat = 0x01,
    /// <summary>Repeater node.</summary>
    Repeater = 0x02,
}

/// <summary>
/// Extension methods for working with MeshCore codes.
/// </summary>
public static class CodeExtensions
{
    /// <summary>
    /// Checks if the code is a push notification (bit 7 set).
    /// </summary>
    public static bool IsPush(this byte code) => (code & 0x80) != 0;

    /// <summary>
    /// Gets the base response code from a push code (strips bit 7).
    /// </summary>
    public static byte ToResponse(this Push push) => (byte)((byte)push & 0x7F);

    /// <summary>
    /// Gets the base response code from any code byte.
    /// </summary>
    public static byte GetBaseCode(this byte code) => (byte)(code & 0x7F);
}
