namespace Microbot.Skills.Slack.Models;

/// <summary>
/// Represents a Slack channel (public or private).
/// </summary>
public class SlackChannel
{
    /// <summary>
    /// The unique identifier for the channel.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The name of the channel (without the # prefix).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The channel's topic.
    /// </summary>
    public string? Topic { get; set; }

    /// <summary>
    /// The channel's purpose/description.
    /// </summary>
    public string? Purpose { get; set; }

    /// <summary>
    /// Whether this is a private channel.
    /// </summary>
    public bool IsPrivate { get; set; }

    /// <summary>
    /// Whether the channel is archived.
    /// </summary>
    public bool IsArchived { get; set; }

    /// <summary>
    /// Whether the bot is a member of this channel.
    /// </summary>
    public bool IsMember { get; set; }

    /// <summary>
    /// Number of members in the channel.
    /// </summary>
    public int MemberCount { get; set; }

    /// <summary>
    /// When the channel was created.
    /// </summary>
    public DateTime CreatedDateTime { get; set; }
}
