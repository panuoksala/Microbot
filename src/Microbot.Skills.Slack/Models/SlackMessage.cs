namespace Microbot.Skills.Slack.Models;

/// <summary>
/// Represents a message in Slack.
/// </summary>
public class SlackMessage
{
    /// <summary>
    /// The message timestamp (used as unique ID in Slack).
    /// </summary>
    public string Ts { get; set; } = string.Empty;

    /// <summary>
    /// The channel or conversation ID where the message was posted.
    /// </summary>
    public string ChannelId { get; set; } = string.Empty;

    /// <summary>
    /// The name of the channel or conversation.
    /// </summary>
    public string ChannelName { get; set; } = string.Empty;

    /// <summary>
    /// The message text content.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// The user ID of the message sender.
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// The display name of the message sender.
    /// </summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// When the message was sent.
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Whether this message is unread based on local tracking.
    /// </summary>
    public bool IsUnread { get; set; }

    /// <summary>
    /// The thread timestamp if this message is part of a thread.
    /// </summary>
    public string? ThreadTs { get; set; }

    /// <summary>
    /// Number of replies in the thread (if this is a parent message).
    /// </summary>
    public int ReplyCount { get; set; }

    /// <summary>
    /// Whether this message is from a bot.
    /// </summary>
    public bool IsBot { get; set; }

    /// <summary>
    /// The message subtype (e.g., "bot_message", "channel_join", etc.).
    /// </summary>
    public string? Subtype { get; set; }
}
