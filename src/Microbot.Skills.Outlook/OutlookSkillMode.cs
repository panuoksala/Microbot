namespace Microbot.Skills.Outlook;

/// <summary>
/// Defines the permission mode for the Outlook skill.
/// </summary>
public enum OutlookSkillMode
{
    /// <summary>
    /// Read-only access to emails and calendar.
    /// Permissions: Mail.Read, Calendars.Read, User.Read
    /// </summary>
    ReadOnly = 0,

    /// <summary>
    /// Read emails and calendar, write calendar events.
    /// Permissions: Mail.Read, Calendars.Read, Calendars.ReadWrite, User.Read
    /// </summary>
    ReadWriteCalendar = 1,

    /// <summary>
    /// Full access: read/send emails, read/write calendar.
    /// Permissions: Mail.Read, Mail.Send, Calendars.Read, Calendars.ReadWrite, User.Read
    /// </summary>
    Full = 2
}
