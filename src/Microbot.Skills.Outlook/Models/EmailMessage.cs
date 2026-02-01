namespace Microbot.Skills.Outlook.Models;

/// <summary>
/// Simplified email message model for AI consumption.
/// </summary>
public class EmailMessage
{
    /// <summary>
    /// Unique identifier for the email.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Email subject line.
    /// </summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>
    /// Sender's email address.
    /// </summary>
    public string From { get; set; } = string.Empty;

    /// <summary>
    /// List of recipient email addresses.
    /// </summary>
    public List<string> To { get; set; } = [];

    /// <summary>
    /// List of CC recipient email addresses.
    /// </summary>
    public List<string> Cc { get; set; } = [];

    /// <summary>
    /// Short preview of the email body.
    /// </summary>
    public string BodyPreview { get; set; } = string.Empty;

    /// <summary>
    /// Full email body content.
    /// </summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// Date and time when the email was received.
    /// </summary>
    public DateTime ReceivedDateTime { get; set; }

    /// <summary>
    /// Whether the email has been read.
    /// </summary>
    public bool IsRead { get; set; }

    /// <summary>
    /// Whether the email has attachments.
    /// </summary>
    public bool HasAttachments { get; set; }

    /// <summary>
    /// Importance level of the email.
    /// </summary>
    public string Importance { get; set; } = "Normal";
}
