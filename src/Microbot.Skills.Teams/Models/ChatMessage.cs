namespace Microbot.Skills.Teams.Models;

/// <summary>
/// Represents a message in a 1:1 or group chat.
/// </summary>
public class ChatMessage
{
    /// <summary>
    /// The unique identifier of the message.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The ID of the chat containing this message.
    /// </summary>
    public string ChatId { get; set; } = string.Empty;

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
    /// The importance of the message.
    /// </summary>
    public string? Importance { get; set; }

    /// <summary>
    /// Whether this message has been deleted.
    /// </summary>
    public bool IsDeleted { get; set; }

    /// <summary>
    /// The type of message (message, chatEvent, typing, etc.).
    /// </summary>
    public string? MessageType { get; set; }
}
