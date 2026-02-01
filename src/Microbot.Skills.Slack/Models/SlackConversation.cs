namespace Microbot.Skills.Slack.Models;

/// <summary>
/// Represents a Slack conversation (DM or group DM).
/// </summary>
public class SlackConversation
{
    /// <summary>
    /// The unique identifier for the conversation.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The display name for the conversation.
    /// For DMs, this is typically the other user's name.
    /// For group DMs, this is a comma-separated list of member names.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Whether this is a direct message (1:1).
    /// </summary>
    public bool IsDirectMessage { get; set; }

    /// <summary>
    /// Whether this is a group message (multi-party DM).
    /// </summary>
    public bool IsGroupMessage { get; set; }

    /// <summary>
    /// List of member user IDs in the conversation.
    /// </summary>
    public List<string> MemberIds { get; set; } = [];

    /// <summary>
    /// List of member display names in the conversation.
    /// </summary>
    public List<string> MemberNames { get; set; } = [];

    /// <summary>
    /// When the conversation was last active.
    /// </summary>
    public DateTime? LastActivity { get; set; }

    /// <summary>
    /// Whether the conversation is open/active.
    /// </summary>
    public bool IsOpen { get; set; }

    /// <summary>
    /// The user ID of the other party (for 1:1 DMs).
    /// </summary>
    public string? UserId { get; set; }
}
