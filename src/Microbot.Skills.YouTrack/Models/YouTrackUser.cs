namespace Microbot.Skills.YouTrack.Models;

/// <summary>
/// Represents a YouTrack user.
/// </summary>
public class YouTrackUser
{
    /// <summary>
    /// The unique user ID.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The user's login name.
    /// </summary>
    public string Login { get; set; } = string.Empty;

    /// <summary>
    /// The user's full name.
    /// </summary>
    public string? FullName { get; set; }

    /// <summary>
    /// The user's email address.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Whether the user is banned.
    /// </summary>
    public bool Banned { get; set; }

    /// <summary>
    /// The user's avatar URL.
    /// </summary>
    public string? AvatarUrl { get; set; }

    /// <summary>
    /// Whether this is a guest user.
    /// </summary>
    public bool Guest { get; set; }
}
