namespace Microbot.Skills.YouTrack.Models;

/// <summary>
/// Represents a YouTrack project.
/// </summary>
public class YouTrackProject
{
    /// <summary>
    /// The unique project ID.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The short name (prefix) of the project (e.g., "PROJ").
    /// </summary>
    public string ShortName { get; set; } = string.Empty;

    /// <summary>
    /// The full name of the project.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The project description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether the project is archived.
    /// </summary>
    public bool Archived { get; set; }

    /// <summary>
    /// The project leader's login.
    /// </summary>
    public string? Leader { get; set; }

    /// <summary>
    /// The creation timestamp.
    /// </summary>
    public DateTime? CreatedDate { get; set; }
}
