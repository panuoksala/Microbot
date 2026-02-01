namespace Microbot.Skills.Teams.Models;

/// <summary>
/// Represents a Microsoft Teams team.
/// </summary>
public class Team
{
    /// <summary>
    /// The unique identifier of the team.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The display name of the team.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// The description of the team.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// The tenant ID where this team belongs.
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// Whether this team is from a guest tenant (user is a guest member).
    /// </summary>
    public bool IsGuestTenant { get; set; }

    /// <summary>
    /// The visibility of the team (public or private).
    /// </summary>
    public string? Visibility { get; set; }

    /// <summary>
    /// When the team was created.
    /// </summary>
    public DateTimeOffset? CreatedDateTime { get; set; }
}
