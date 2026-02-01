namespace Microbot.Skills.Teams.Models;

/// <summary>
/// Represents a message in a Teams channel.
/// </summary>
public class ChannelMessage
{
    /// <summary>
    /// The unique identifier of the message.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The ID of the team containing this message.
    /// </summary>
    public string TeamId { get; set; } = string.Empty;

    /// <summary>
    /// The ID of the channel containing this message.
    /// </summary>
    public string ChannelId { get; set; } = string.Empty;

    /// <summary>
    /// The display name of the sender.
    /// </summary>
    public string? SenderDisplayName { get; set; }

    /// <summary>
    /// The email/UPN of the sender.
    /// </summary>
    public string? SenderEmail { get; set; }

    /// <summary>
    /// The message body content (plain text).
    /// </summary>
    public string? Body { get; set; }

    /// <summary>
    /// The message body content type (text or html).
    /// </summary>
    public string? BodyContentType { get; set; }

    /// <summary>
    /// When the message was created.
    /// </summary>
    public DateTimeOffset? CreatedDateTime { get; set; }

    /// <summary>
    /// When the message was last modified.
    /// </summary>
    public DateTimeOffset? LastModifiedDateTime { get; set; }

    /// <summary>
    /// The subject of the message (for root messages).
    /// </summary>
    public string? Subject { get; set; }

    /// <summary>
    /// The importance of the message.
    /// </summary>
    public string? Importance { get; set; }

    /// <summary>
    /// The web URL to access this message in Teams.
    /// </summary>
    public string? WebUrl { get; set; }

    /// <summary>
    /// The ID of the parent message if this is a reply.
    /// </summary>
    public string? ReplyToId { get; set; }

    /// <summary>
    /// Whether this message has been deleted.
    /// </summary>
    public bool IsDeleted { get; set; }

    /// <summary>
    /// The number of replies to this message.
    /// </summary>
    public int ReplyCount { get; set; }
}
