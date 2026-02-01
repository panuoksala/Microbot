namespace Microbot.Skills.Outlook.Models;

/// <summary>
/// Simplified calendar event model for AI consumption.
/// </summary>
public class CalendarEvent
{
    /// <summary>
    /// Unique identifier for the event.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Subject/title of the event.
    /// </summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>
    /// Event description/body content.
    /// </summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// Start date and time of the event.
    /// </summary>
    public DateTime Start { get; set; }

    /// <summary>
    /// End date and time of the event.
    /// </summary>
    public DateTime End { get; set; }

    /// <summary>
    /// Time zone for the event.
    /// </summary>
    public string TimeZone { get; set; } = string.Empty;

    /// <summary>
    /// Location of the event.
    /// </summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>
    /// List of attendee email addresses.
    /// </summary>
    public List<string> Attendees { get; set; } = [];

    /// <summary>
    /// Whether this is an online meeting.
    /// </summary>
    public bool IsOnlineMeeting { get; set; }

    /// <summary>
    /// URL to join the online meeting (if applicable).
    /// </summary>
    public string? OnlineMeetingUrl { get; set; }

    /// <summary>
    /// Whether this is an all-day event.
    /// </summary>
    public bool IsAllDay { get; set; }

    /// <summary>
    /// The organizer's email address.
    /// </summary>
    public string Organizer { get; set; } = string.Empty;

    /// <summary>
    /// User's response status (accepted, tentative, declined, etc.).
    /// </summary>
    public string ResponseStatus { get; set; } = string.Empty;
}
