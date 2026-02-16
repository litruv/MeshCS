namespace MeshCS;

/// <summary>
/// Handles sending messages with automatic chunking for long content.
/// </summary>
public sealed class MessageSender
{
    private readonly MeshRadio _radio;
    private readonly int _maxChunkSize;
    private readonly int _interPacketDelayMs;

    /// <summary>
    /// Fired when a chunk is about to be sent.
    /// </summary>
    public event Action<int, int>? ChunkSending;

    /// <summary>
    /// Fired when a chunk has been sent successfully.
    /// </summary>
    public event Action<int, int, bool>? ChunkSent;

    /// <summary>
    /// Fired when all chunks have been sent.
    /// </summary>
    public event Action<int, bool>? MessageComplete;

    /// <summary>
    /// Creates a new message sender.
    /// </summary>
    /// <param name="radio">MeshRadio connection.</param>
    /// <param name="maxChunkSize">Maximum characters per chunk (default: 150).</param>
    /// <param name="interPacketDelayMs">Delay between packets in ms (default: 50).</param>
    public MessageSender(MeshRadio radio, int maxChunkSize = 150, int interPacketDelayMs = 50)
    {
        _radio = radio;
        _maxChunkSize = maxChunkSize;
        _interPacketDelayMs = interPacketDelayMs;
    }

    /// <summary>
    /// Sends a text message to a contact, automatically chunking if needed.
    /// </summary>
    /// <param name="recipient">6-byte public key prefix.</param>
    /// <param name="text">Message text.</param>
    /// <param name="exactChunking">If true, splits at exact boundaries (for encoded data). Otherwise prefers word/line breaks.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if all chunks were sent successfully.</returns>
    public async Task<bool> SendTextAsync(byte[] recipient, string text, bool exactChunking = false, CancellationToken ct = default)
    {
        var chunks = exactChunking 
            ? TextUtils.SplitExact(text, _maxChunkSize)
            : TextUtils.SplitText(text, _maxChunkSize);
        var totalChunks = chunks.Count;
        var allSuccess = true;

        for (var i = 0; i < chunks.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            ChunkSending?.Invoke(i + 1, totalChunks);
            var success = await _radio.SendMessageAsync(recipient, chunks[i], TextType.Plain, ct);
            ChunkSent?.Invoke(i + 1, totalChunks, success);

            if (!success)
                allSuccess = false;

            if (chunks.Count > 1 && i < chunks.Count - 1)
                await Task.Delay(_interPacketDelayMs, ct);
        }

        MessageComplete?.Invoke(totalChunks, allSuccess);
        return allSuccess;
    }

    /// <summary>
    /// Sends a message to a channel.
    /// </summary>
    /// <param name="channelIndex">Channel index.</param>
    /// <param name="text">Message text.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the message was sent successfully.</returns>
    public Task<bool> SendChannelAsync(byte channelIndex, string text, CancellationToken ct = default)
    {
        return _radio.SendChannelMessageAsync(channelIndex, text, ct);
    }

    /// <summary>
    /// Sends raw binary data as a DM with CLI data type.
    /// </summary>
    /// <param name="recipient">6-byte public key prefix.</param>
    /// <param name="data">Raw binary data.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the message was sent successfully.</returns>
    public Task<bool> SendRawAsync(byte[] recipient, byte[] data, CancellationToken ct = default)
    {
        return _radio.SendMessageRawAsync(recipient, data, TextType.CliData, ct);
    }
}
