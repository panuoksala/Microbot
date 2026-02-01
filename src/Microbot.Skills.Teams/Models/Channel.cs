namespace Microbot.Skills.Teams.Models;

/// <summary>
/// Represents a channel within a Microsoft Teams team.
/// </summary>
public class Channel
{
    /// <summary>
    /// The unique identifier of the channel.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The display name of the channel.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// The description of the channel.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// The ID of the team this channel belongs to.
    /// </summary>
    public string TeamId { get; set; } = string.Empty;

    /// <summary>
    /// The membership type of the channel (standard, private, shared).
    /// </summary>
    public string? MembershipType { get; set; }

    /// <summary>
    /// When the channel was created.
    /// </summary>
    public DateTimeOffset? CreatedDateTime { get; set; }

    /// <summary>
    /// The web URL to access this channel in Teams.
    /// </summary>
    public string? WebUrl { get; set; }
}
