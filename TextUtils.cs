namespace MeshCS;

/// <summary>
/// Text manipulation utilities for mesh messaging.
/// </summary>
public static class TextUtils
{
    /// <summary>
    /// Splits text into chunks no longer than maxLen, preferring line/word boundaries.
    /// </summary>
    /// <param name="text">Text to split.</param>
    /// <param name="maxLen">Maximum chunk length.</param>
    /// <returns>List of text chunks.</returns>
    public static List<string> SplitText(string text, int maxLen)
    {
        var chunks = new List<string>();
        var remaining = text;

        while (remaining.Length > maxLen)
        {
            // Prefer breaking at a newline
            var breakAt = remaining.LastIndexOf('\n', maxLen);
            if (breakAt <= 0)
            {
                // Fall back to word boundary
                breakAt = remaining.LastIndexOf(' ', maxLen);
            }
            if (breakAt <= 0) breakAt = maxLen;

            chunks.Add(remaining[..breakAt]);
            remaining = remaining[(breakAt + 1)..];
        }

        if (remaining.Length > 0)
            chunks.Add(remaining);

        return chunks;
    }

    /// <summary>
    /// Splits text into exact-size chunks with no boundary preference.
    /// Useful for binary/encoded data that must not be altered.
    /// </summary>
    /// <param name="text">Text to split.</param>
    /// <param name="maxLen">Maximum chunk length.</param>
    /// <returns>List of text chunks.</returns>
    public static List<string> SplitExact(string text, int maxLen)
    {
        var chunks = new List<string>();
        for (var i = 0; i < text.Length; i += maxLen)
        {
            chunks.Add(text.Substring(i, Math.Min(maxLen, text.Length - i)));
        }
        return chunks;
    }
}
