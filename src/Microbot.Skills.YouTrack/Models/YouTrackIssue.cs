namespace Microbot.Skills.YouTrack.Models;

/// <summary>
/// Represents a YouTrack issue.
/// </summary>
public class YouTrackIssue
{
    /// <summary>
    /// The unique issue ID (e.g., "PROJ-123").
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The internal database ID.
    /// </summary>
    public string? IdReadable { get; set; }

    /// <summary>
    /// The project the issue belongs to.
    /// </summary>
    public string? ProjectId { get; set; }

    /// <summary>
    /// The project short name.
    /// </summary>
    public string? ProjectShortName { get; set; }

    /// <summary>
    /// The issue summary/title.
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// The issue description (markdown).
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// The issue state (e.g., "Open", "In Progress", "Done").
    /// </summary>
    public string? State { get; set; }

    /// <summary>
    /// The issue priority (e.g., "Critical", "Major", "Normal", "Minor").
    /// </summary>
    public string? Priority { get; set; }

    /// <summary>
    /// The issue type (e.g., "Bug", "Feature", "Task").
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    /// The assignee's login.
    /// </summary>
    public string? Assignee { get; set; }

    /// <summary>
    /// The reporter's login.
    /// </summary>
    public string? Reporter { get; set; }

    /// <summary>
    /// The creation timestamp.
    /// </summary>
    public DateTime? Created { get; set; }

    /// <summary>
    /// The last update timestamp.
    /// </summary>
    public DateTime? Updated { get; set; }

    /// <summary>
    /// The resolved timestamp.
    /// </summary>
    public DateTime? Resolved { get; set; }

    /// <summary>
    /// The number of comments on the issue.
    /// </summary>
    public int CommentsCount { get; set; }

    /// <summary>
    /// Tags/labels attached to the issue.
    /// </summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// Custom fields as key-value pairs.
    /// </summary>
    public Dictionary<string, string?> CustomFields { get; set; } = [];
}
