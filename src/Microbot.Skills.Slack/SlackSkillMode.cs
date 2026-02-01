namespace Microbot.Skills.Slack;

/// <summary>
/// Defines the permission mode for the Slack skill.
/// </summary>
public enum SlackSkillMode
{
    /// <summary>
    /// Read-only access to channels, DMs, and messages.
    /// Scopes: channels:read, channels:history, groups:read, groups:history,
    ///         im:read, im:history, mpim:read, mpim:history, users:read
    /// </summary>
    ReadOnly,

    /// <summary>
    /// Full access: read and send messages to channels and DMs.
    /// Scopes: All ReadOnly scopes + chat:write, chat:write.public
    /// </summary>
    Full
}
