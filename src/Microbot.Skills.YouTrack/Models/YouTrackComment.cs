namespace Microbot.Skills.YouTrack.Models;

/// <summary>
/// Represents a comment on a YouTrack issue.
/// </summary>
public class YouTrackComment
{
    /// <summary>
    /// The unique comment ID.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The issue ID this comment belongs to.
    /// </summary>
    public string IssueId { get; set; } = string.Empty;

    /// <summary>
    /// The comment text (markdown).
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// The author's login.
    /// </summary>
    public string? Author { get; set; }

    /// <summary>
    /// The author's full name.
    /// </summary>
    public string? AuthorFullName { get; set; }

    /// <summary>
    /// The creation timestamp.
    /// </summary>
    public DateTime? Created { get; set; }

    /// <summary>
    /// The last update timestamp.
    /// </summary>
    public DateTime? Updated { get; set; }

    /// <summary>
    /// Whether the comment has been deleted.
    /// </summary>
    public bool Deleted { get; set; }
}
