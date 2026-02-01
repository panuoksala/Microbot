namespace Microbot.Skills.Slack.Models;

/// <summary>
/// Represents a Slack user.
/// </summary>
public class SlackUser
{
    /// <summary>
    /// The unique identifier for the user.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The user's username (handle).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The user's real/full name.
    /// </summary>
    public string RealName { get; set; } = string.Empty;

    /// <summary>
    /// The user's display name.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// The user's email address.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Whether this user is a bot.
    /// </summary>
    public bool IsBot { get; set; }

    /// <summary>
    /// Whether this user is a workspace admin.
    /// </summary>
    public bool IsAdmin { get; set; }

    /// <summary>
    /// Whether this user is the workspace owner.
    /// </summary>
    public bool IsOwner { get; set; }

    /// <summary>
    /// The user's current status text.
    /// </summary>
    public string? StatusText { get; set; }

    /// <summary>
    /// The user's current status emoji.
    /// </summary>
    public string? StatusEmoji { get; set; }

    /// <summary>
    /// The user's timezone.
    /// </summary>
    public string? Timezone { get; set; }
}
