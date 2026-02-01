namespace Microbot.Skills.Teams.Models;

/// <summary>
/// Tracks the read state for channels and chats.
/// </summary>
public class ReadState
{
    /// <summary>
    /// Last read timestamps for channels, keyed by "teamId:channelId".
    /// </summary>
    public Dictionary<string, DateTimeOffset> ChannelLastRead { get; set; } = [];

    /// <summary>
    /// Last read timestamps for chats, keyed by chatId.
    /// </summary>
    public Dictionary<string, DateTimeOffset> ChatLastRead { get; set; } = [];

    /// <summary>
    /// When this read state was last updated.
    /// </summary>
    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;
}
