namespace Microbot.Skills.Slack.Models;

/// <summary>
/// Tracks the last read timestamps for Slack channels and conversations.
/// </summary>
public class SlackReadState
{
    /// <summary>
    /// Last read timestamp per channel (key: channelId).
    /// </summary>
    public Dictionary<string, DateTime> ChannelLastRead { get; set; } = [];

    /// <summary>
    /// Last read timestamp per conversation/DM (key: conversationId).
    /// </summary>
    public Dictionary<string, DateTime> ConversationLastRead { get; set; } = [];

    /// <summary>
    /// Last time the read state was updated.
    /// </summary>
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}
