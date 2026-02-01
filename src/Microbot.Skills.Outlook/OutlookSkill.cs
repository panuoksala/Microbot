namespace Microbot.Skills.Outlook;

using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Me.SendMail;
using Microsoft.Graph.Me.Messages.Item.Reply;
using Microsoft.Graph.Me.Messages.Item.ReplyAll;
using Microsoft.Graph.Me.Messages.Item.Forward;
using Microsoft.SemanticKernel;
using Microbot.Core.Models;
using Microbot.Skills.Outlook.Models;
using Microbot.Skills.Outlook.Services;

/// <summary>
/// Outlook skill providing email and calendar functionality via Microsoft Graph.
/// Supports three permission modes: ReadOnly, ReadWriteCalendar, and Full.
/// </summary>
public class OutlookSkill
{
    private readonly OutlookSkillConfig _config;
    private readonly OutlookSkillMode _mode;
    private readonly OutlookAuthenticationService _authService;
    private readonly ILogger<OutlookSkill>? _logger;
    private GraphServiceClient? _graphClient;
    private readonly Action<string>? _deviceCodeCallback;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Creates a new OutlookSkill instance.
    /// </summary>
    /// <param name="config">Outlook skill configuration.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="deviceCodeCallback">Callback for device code authentication messages.</param>
    public OutlookSkill(
        OutlookSkillConfig config,
        ILogger<OutlookSkill>? logger = null,
        Action<string>? deviceCodeCallback = null)
    {
        _config = config;
        _mode = Enum.Parse<OutlookSkillMode>(config.Mode, ignoreCase: true);
        _authService = new OutlookAuthenticationService(config,
            logger as ILogger<OutlookAuthenticationService>);
        _logger = logger;
        _deviceCodeCallback = deviceCodeCallback;
        _jsonOptions = new JsonSerializerOptions { WriteIndented = true };
    }

    /// <summary>
    /// Gets the authenticated Graph client.
    /// </summary>
    private async Task<GraphServiceClient> GetClientAsync(CancellationToken ct = default)
    {
        _graphClient ??= await _authService.GetGraphClientAsync(_deviceCodeCallback, ct);
        return _graphClient;
    }

    /// <summary>
    /// Ensures the current mode has permission for the requested operation.
    /// </summary>
    private void EnsurePermission(OutlookSkillMode requiredMode, string operation)
    {
        if (_mode < requiredMode)
        {
            throw new InvalidOperationException(
                $"Operation '{operation}' requires {requiredMode} mode, " +
                $"but current mode is {_mode}. Please update your Outlook skill configuration.");
        }
    }

    #region Email Reading Functions (ReadOnly)

    /// <summary>
    /// Lists recent emails from the inbox.
    /// </summary>
    [KernelFunction("list_emails")]
    [Description("Lists recent emails from the inbox. Returns subject, sender, date, and preview for each email.")]
    public async Task<string> ListEmailsAsync(
        [Description("Maximum number of emails to return (default: 10, max: 50)")]
        int count = 10,
        [Description("Filter by unread only (default: false)")]
        bool unreadOnly = false,
        CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Listing emails (count: {Count}, unreadOnly: {UnreadOnly})", count, unreadOnly);

        var client = await GetClientAsync(cancellationToken);

        count = Math.Min(Math.Max(count, 1), 50);

        var messages = await client.Me.Messages.GetAsync(config =>
        {
            config.QueryParameters.Top = count;
            config.QueryParameters.Select = new[]
            {
                "id", "subject", "from", "toRecipients",
                "receivedDateTime", "bodyPreview", "isRead", "hasAttachments", "importance"
            };
            config.QueryParameters.Orderby = new[] { "receivedDateTime desc" };

            if (unreadOnly)
            {
                config.QueryParameters.Filter = "isRead eq false";
            }
        }, cancellationToken);

        var result = messages?.Value?.Select(m => new EmailMessage
        {
            Id = m.Id ?? string.Empty,
            Subject = m.Subject ?? "(No Subject)",
            From = m.From?.EmailAddress?.Address ?? "Unknown",
            To = m.ToRecipients?.Select(r => r.EmailAddress?.Address ?? "").ToList() ?? [],
            BodyPreview = m.BodyPreview ?? string.Empty,
            ReceivedDateTime = m.ReceivedDateTime?.DateTime ?? DateTime.MinValue,
            IsRead = m.IsRead ?? false,
            HasAttachments = m.HasAttachments ?? false,
            Importance = m.Importance?.ToString() ?? "Normal"
        }).ToList() ?? [];

        _logger?.LogInformation("Retrieved {Count} emails", result.Count);
        return JsonSerializer.Serialize(result, _jsonOptions);
    }

    /// <summary>
    /// Gets the full content of a specific email.
    /// </summary>
    [KernelFunction("get_email")]
    [Description("Gets the full content of a specific email by its ID, including the complete body.")]
    public async Task<string> GetEmailAsync(
        [Description("The ID of the email to retrieve")]
        string emailId,
        CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Getting email: {EmailId}", emailId);

        var client = await GetClientAsync(cancellationToken);

        var message = await client.Me.Messages[emailId].GetAsync(config =>
        {
            config.QueryParameters.Select = new[]
            {
                "id", "subject", "from", "toRecipients", "ccRecipients",
                "receivedDateTime", "body", "isRead", "hasAttachments", "importance"
            };
        }, cancellationToken);

        if (message == null)
            return JsonSerializer.Serialize(new { error = "Email not found" }, _jsonOptions);

        var result = new EmailMessage
        {
            Id = message.Id ?? string.Empty,
            Subject = message.Subject ?? "(No Subject)",
            From = message.From?.EmailAddress?.Address ?? "Unknown",
            To = message.ToRecipients?.Select(r => r.EmailAddress?.Address ?? "").ToList() ?? [],
            Cc = message.CcRecipients?.Select(r => r.EmailAddress?.Address ?? "").ToList() ?? [],
            Body = message.Body?.Content ?? string.Empty,
            ReceivedDateTime = message.ReceivedDateTime?.DateTime ?? DateTime.MinValue,
            IsRead = message.IsRead ?? false,
            HasAttachments = message.HasAttachments ?? false,
            Importance = message.Importance?.ToString() ?? "Normal"
        };

        return JsonSerializer.Serialize(result, _jsonOptions);
    }

    /// <summary>
    /// Searches emails by subject or content.
    /// </summary>
    [KernelFunction("search_emails")]
    [Description("Searches emails by subject or content. Returns matching emails with subject, sender, and preview.")]
    public async Task<string> SearchEmailsAsync(
        [Description("Search query to find in email subject or body")]
        string query,
        [Description("Maximum number of results (default: 10, max: 25)")]
        int count = 10,
        CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Searching emails with query: {Query}", query);

        var client = await GetClientAsync(cancellationToken);

        count = Math.Min(Math.Max(count, 1), 25);

        var messages = await client.Me.Messages.GetAsync(config =>
        {
            config.QueryParameters.Search = $"\"{query}\"";
            config.QueryParameters.Top = count;
            config.QueryParameters.Select = new[]
            {
                "id", "subject", "from", "receivedDateTime", "bodyPreview", "isRead"
            };
        }, cancellationToken);

        var result = messages?.Value?.Select(m => new EmailMessage
        {
            Id = m.Id ?? string.Empty,
            Subject = m.Subject ?? "(No Subject)",
            From = m.From?.EmailAddress?.Address ?? "Unknown",
            BodyPreview = m.BodyPreview ?? string.Empty,
            ReceivedDateTime = m.ReceivedDateTime?.DateTime ?? DateTime.MinValue,
            IsRead = m.IsRead ?? false
        }).ToList() ?? [];

        _logger?.LogInformation("Found {Count} emails matching query", result.Count);
        return JsonSerializer.Serialize(result, _jsonOptions);
    }

    #endregion

    #region Calendar Reading Functions (ReadOnly)

    /// <summary>
    /// Lists upcoming calendar events.
    /// </summary>
    [KernelFunction("list_calendar_events")]
    [Description("Lists upcoming calendar events within the specified number of days.")]
    public async Task<string> ListCalendarEventsAsync(
        [Description("Number of days to look ahead (default: 7, max: 30)")]
        int daysAhead = 7,
        [Description("Maximum number of events to return (default: 20, max: 50)")]
        int count = 20,
        CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Listing calendar events (daysAhead: {DaysAhead}, count: {Count})", daysAhead, count);

        var client = await GetClientAsync(cancellationToken);

        daysAhead = Math.Min(Math.Max(daysAhead, 1), 30);
        count = Math.Min(Math.Max(count, 1), 50);

        var startDateTime = DateTime.UtcNow;
        var endDateTime = startDateTime.AddDays(daysAhead);

        var events = await client.Me.CalendarView.GetAsync(config =>
        {
            config.QueryParameters.StartDateTime = startDateTime.ToString("o");
            config.QueryParameters.EndDateTime = endDateTime.ToString("o");
            config.QueryParameters.Top = count;
            config.QueryParameters.Select = new[]
            {
                "id", "subject", "start", "end", "location",
                "attendees", "isOnlineMeeting", "onlineMeeting", "isAllDay",
                "organizer", "responseStatus"
            };
            config.QueryParameters.Orderby = new[] { "start/dateTime" };
        }, cancellationToken);

        var result = events?.Value?.Select(e => new CalendarEvent
        {
            Id = e.Id ?? string.Empty,
            Subject = e.Subject ?? "(No Subject)",
            Start = ParseDateTime(e.Start?.DateTime),
            End = ParseDateTime(e.End?.DateTime),
            TimeZone = e.Start?.TimeZone ?? TimeZoneInfo.Local.Id,
            Location = e.Location?.DisplayName ?? string.Empty,
            Attendees = e.Attendees?.Select(a => a.EmailAddress?.Address ?? "").ToList() ?? [],
            IsOnlineMeeting = e.IsOnlineMeeting ?? false,
            OnlineMeetingUrl = e.OnlineMeeting?.JoinUrl,
            IsAllDay = e.IsAllDay ?? false,
            Organizer = e.Organizer?.EmailAddress?.Address ?? string.Empty,
            ResponseStatus = e.ResponseStatus?.Response?.ToString() ?? string.Empty
        }).ToList() ?? [];

        _logger?.LogInformation("Retrieved {Count} calendar events", result.Count);
        return JsonSerializer.Serialize(result, _jsonOptions);
    }

    /// <summary>
    /// Gets details of a specific calendar event.
    /// </summary>
    [KernelFunction("get_calendar_event")]
    [Description("Gets detailed information about a specific calendar event by its ID.")]
    public async Task<string> GetCalendarEventAsync(
        [Description("The ID of the calendar event to retrieve")]
        string eventId,
        CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Getting calendar event: {EventId}", eventId);

        var client = await GetClientAsync(cancellationToken);

        var calendarEvent = await client.Me.Events[eventId].GetAsync(
            cancellationToken: cancellationToken);

        if (calendarEvent == null)
            return JsonSerializer.Serialize(new { error = "Event not found" }, _jsonOptions);

        var result = new CalendarEvent
        {
            Id = calendarEvent.Id ?? string.Empty,
            Subject = calendarEvent.Subject ?? "(No Subject)",
            Body = calendarEvent.Body?.Content ?? string.Empty,
            Start = ParseDateTime(calendarEvent.Start?.DateTime),
            End = ParseDateTime(calendarEvent.End?.DateTime),
            TimeZone = calendarEvent.Start?.TimeZone ?? TimeZoneInfo.Local.Id,
            Location = calendarEvent.Location?.DisplayName ?? string.Empty,
            Attendees = calendarEvent.Attendees?.Select(a => a.EmailAddress?.Address ?? "").ToList() ?? [],
            IsOnlineMeeting = calendarEvent.IsOnlineMeeting ?? false,
            OnlineMeetingUrl = calendarEvent.OnlineMeeting?.JoinUrl,
            IsAllDay = calendarEvent.IsAllDay ?? false,
            Organizer = calendarEvent.Organizer?.EmailAddress?.Address ?? string.Empty,
            ResponseStatus = calendarEvent.ResponseStatus?.Response?.ToString() ?? string.Empty
        };

        return JsonSerializer.Serialize(result, _jsonOptions);
    }

    #endregion

    #region Calendar Write Functions (ReadWriteCalendar)

    /// <summary>
    /// Creates a new calendar event.
    /// </summary>
    [KernelFunction("create_calendar_event")]
    [Description("Creates a new calendar event. Requires ReadWriteCalendar or Full mode.")]
    public async Task<string> CreateCalendarEventAsync(
        [Description("Subject/title of the event")]
        string subject,
        [Description("Start date and time in ISO 8601 format (e.g., 2024-03-15T10:00:00)")]
        string startDateTime,
        [Description("End date and time in ISO 8601 format (e.g., 2024-03-15T11:00:00)")]
        string endDateTime,
        [Description("Location of the event (optional)")]
        string? location = null,
        [Description("Event description/body (optional)")]
        string? body = null,
        [Description("Comma-separated list of attendee email addresses (optional)")]
        string? attendees = null,
        [Description("Whether to create as an online Teams meeting (default: false)")]
        bool isOnlineMeeting = false,
        CancellationToken cancellationToken = default)
    {
        EnsurePermission(OutlookSkillMode.ReadWriteCalendar, "create_calendar_event");

        _logger?.LogInformation("Creating calendar event: {Subject}", subject);

        var client = await GetClientAsync(cancellationToken);

        var newEvent = new Event
        {
            Subject = subject,
            Start = new DateTimeTimeZone
            {
                DateTime = startDateTime,
                TimeZone = TimeZoneInfo.Local.Id
            },
            End = new DateTimeTimeZone
            {
                DateTime = endDateTime,
                TimeZone = TimeZoneInfo.Local.Id
            }
        };

        if (!string.IsNullOrEmpty(location))
        {
            newEvent.Location = new Location { DisplayName = location };
        }

        if (!string.IsNullOrEmpty(body))
        {
            newEvent.Body = new ItemBody
            {
                ContentType = BodyType.Text,
                Content = body
            };
        }

        if (!string.IsNullOrEmpty(attendees))
        {
            newEvent.Attendees = attendees.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(email => new Attendee
                {
                    EmailAddress = new EmailAddress { Address = email.Trim() },
                    Type = AttendeeType.Required
                }).ToList();
        }

        if (isOnlineMeeting)
        {
            newEvent.IsOnlineMeeting = true;
            newEvent.OnlineMeetingProvider = OnlineMeetingProviderType.TeamsForBusiness;
        }

        var createdEvent = await client.Me.Events.PostAsync(newEvent, cancellationToken: cancellationToken);

        _logger?.LogInformation("Created calendar event with ID: {EventId}", createdEvent?.Id);

        return JsonSerializer.Serialize(new
        {
            success = true,
            message = "Event created successfully",
            eventId = createdEvent?.Id,
            onlineMeetingUrl = createdEvent?.OnlineMeeting?.JoinUrl
        }, _jsonOptions);
    }

    /// <summary>
    /// Updates an existing calendar event.
    /// </summary>
    [KernelFunction("update_calendar_event")]
    [Description("Updates an existing calendar event. Only provide the fields you want to change. Requires ReadWriteCalendar or Full mode.")]
    public async Task<string> UpdateCalendarEventAsync(
        [Description("The ID of the event to update")]
        string eventId,
        [Description("New subject/title (optional, leave empty to keep current)")]
        string? subject = null,
        [Description("New start date and time in ISO 8601 format (optional)")]
        string? startDateTime = null,
        [Description("New end date and time in ISO 8601 format (optional)")]
        string? endDateTime = null,
        [Description("New location (optional)")]
        string? location = null,
        [Description("New description/body (optional)")]
        string? body = null,
        CancellationToken cancellationToken = default)
    {
        EnsurePermission(OutlookSkillMode.ReadWriteCalendar, "update_calendar_event");

        _logger?.LogInformation("Updating calendar event: {EventId}", eventId);

        var client = await GetClientAsync(cancellationToken);

        var updateEvent = new Event();

        if (!string.IsNullOrEmpty(subject))
            updateEvent.Subject = subject;

        if (!string.IsNullOrEmpty(startDateTime))
        {
            updateEvent.Start = new DateTimeTimeZone
            {
                DateTime = startDateTime,
                TimeZone = TimeZoneInfo.Local.Id
            };
        }

        if (!string.IsNullOrEmpty(endDateTime))
        {
            updateEvent.End = new DateTimeTimeZone
            {
                DateTime = endDateTime,
                TimeZone = TimeZoneInfo.Local.Id
            };
        }

        if (!string.IsNullOrEmpty(location))
            updateEvent.Location = new Location { DisplayName = location };

        if (!string.IsNullOrEmpty(body))
        {
            updateEvent.Body = new ItemBody
            {
                ContentType = BodyType.Text,
                Content = body
            };
        }

        await client.Me.Events[eventId].PatchAsync(updateEvent, cancellationToken: cancellationToken);

        _logger?.LogInformation("Updated calendar event: {EventId}", eventId);

        return JsonSerializer.Serialize(new
        {
            success = true,
            message = $"Event {eventId} updated successfully"
        }, _jsonOptions);
    }

    /// <summary>
    /// Deletes a calendar event.
    /// </summary>
    [KernelFunction("delete_calendar_event")]
    [Description("Deletes a calendar event. This action cannot be undone. Requires ReadWriteCalendar or Full mode.")]
    public async Task<string> DeleteCalendarEventAsync(
        [Description("The ID of the event to delete")]
        string eventId,
        CancellationToken cancellationToken = default)
    {
        EnsurePermission(OutlookSkillMode.ReadWriteCalendar, "delete_calendar_event");

        _logger?.LogInformation("Deleting calendar event: {EventId}", eventId);

        var client = await GetClientAsync(cancellationToken);

        await client.Me.Events[eventId].DeleteAsync(cancellationToken: cancellationToken);

        _logger?.LogInformation("Deleted calendar event: {EventId}", eventId);

        return JsonSerializer.Serialize(new
        {
            success = true,
            message = $"Event {eventId} deleted successfully"
        }, _jsonOptions);
    }

    #endregion

    #region Email Send Functions (Full)

    /// <summary>
    /// Sends a new email.
    /// </summary>
    [KernelFunction("send_email")]
    [Description("Sends a new email to the specified recipients. Requires Full mode.")]
    public async Task<string> SendEmailAsync(
        [Description("Recipient email address (comma-separated for multiple recipients)")]
        string to,
        [Description("Email subject")]
        string subject,
        [Description("Email body content")]
        string body,
        [Description("Whether the body is HTML (default: false for plain text)")]
        bool isHtml = false,
        [Description("CC recipients (comma-separated, optional)")]
        string? cc = null,
        CancellationToken cancellationToken = default)
    {
        EnsurePermission(OutlookSkillMode.Full, "send_email");

        _logger?.LogInformation("Sending email to: {To}, Subject: {Subject}", to, subject);

        var client = await GetClientAsync(cancellationToken);

        var message = new Message
        {
            Subject = subject,
            Body = new ItemBody
            {
                ContentType = isHtml ? BodyType.Html : BodyType.Text,
                Content = body
            },
            ToRecipients = to.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(email => new Recipient
                {
                    EmailAddress = new EmailAddress { Address = email.Trim() }
                }).ToList()
        };

        if (!string.IsNullOrEmpty(cc))
        {
            message.CcRecipients = cc.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(email => new Recipient
                {
                    EmailAddress = new EmailAddress { Address = email.Trim() }
                }).ToList();
        }

        await client.Me.SendMail.PostAsync(new SendMailPostRequestBody
        {
            Message = message,
            SaveToSentItems = true
        }, cancellationToken: cancellationToken);

        _logger?.LogInformation("Email sent successfully to: {To}", to);

        return JsonSerializer.Serialize(new
        {
            success = true,
            message = "Email sent successfully"
        }, _jsonOptions);
    }

    /// <summary>
    /// Replies to an existing email.
    /// </summary>
    [KernelFunction("reply_to_email")]
    [Description("Replies to an existing email. Requires Full mode.")]
    public async Task<string> ReplyToEmailAsync(
        [Description("The ID of the email to reply to")]
        string emailId,
        [Description("Reply message content")]
        string replyContent,
        [Description("Whether to reply to all recipients (default: false)")]
        bool replyAll = false,
        CancellationToken cancellationToken = default)
    {
        EnsurePermission(OutlookSkillMode.Full, "reply_to_email");

        _logger?.LogInformation("Replying to email: {EmailId}, ReplyAll: {ReplyAll}", emailId, replyAll);

        var client = await GetClientAsync(cancellationToken);

        if (replyAll)
        {
            await client.Me.Messages[emailId].ReplyAll.PostAsync(
                new ReplyAllPostRequestBody
                {
                    Comment = replyContent
                }, cancellationToken: cancellationToken);
        }
        else
        {
            await client.Me.Messages[emailId].Reply.PostAsync(
                new ReplyPostRequestBody
                {
                    Comment = replyContent
                }, cancellationToken: cancellationToken);
        }

        _logger?.LogInformation("Reply sent successfully");

        return JsonSerializer.Serialize(new
        {
            success = true,
            message = replyAll ? "Reply sent to all recipients" : "Reply sent successfully"
        }, _jsonOptions);
    }

    /// <summary>
    /// Forwards an email to specified recipients.
    /// </summary>
    [KernelFunction("forward_email")]
    [Description("Forwards an email to specified recipients. Requires Full mode.")]
    public async Task<string> ForwardEmailAsync(
        [Description("The ID of the email to forward")]
        string emailId,
        [Description("Recipient email addresses (comma-separated)")]
        string to,
        [Description("Optional comment to include with the forwarded email")]
        string? comment = null,
        CancellationToken cancellationToken = default)
    {
        EnsurePermission(OutlookSkillMode.Full, "forward_email");

        _logger?.LogInformation("Forwarding email: {EmailId} to: {To}", emailId, to);

        var client = await GetClientAsync(cancellationToken);

        var toRecipients = to.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(email => new Recipient
            {
                EmailAddress = new EmailAddress { Address = email.Trim() }
            }).ToList();

        await client.Me.Messages[emailId].Forward.PostAsync(
            new ForwardPostRequestBody
            {
                ToRecipients = toRecipients,
                Comment = comment
            }, cancellationToken: cancellationToken);

        _logger?.LogInformation("Email forwarded successfully to: {To}", to);

        return JsonSerializer.Serialize(new
        {
            success = true,
            message = "Email forwarded successfully"
        }, _jsonOptions);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Parses a datetime string from Microsoft Graph format.
    /// </summary>
    private static DateTime ParseDateTime(string? dateTimeString)
    {
        if (string.IsNullOrEmpty(dateTimeString))
            return DateTime.MinValue;

        if (DateTime.TryParse(dateTimeString, out var result))
            return result;

        return DateTime.MinValue;
    }

    #endregion
}
