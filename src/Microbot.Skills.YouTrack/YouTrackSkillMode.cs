namespace Microbot.Skills.YouTrack;

/// <summary>
/// Defines the permission mode for the YouTrack skill.
/// </summary>
public enum YouTrackSkillMode
{
    /// <summary>
    /// Read-only access to issues, projects, and comments.
    /// Allows: reading issues, searching issues, reading comments, reading projects.
    /// </summary>
    ReadOnly,

    /// <summary>
    /// Full control: read, create, update issues and comments.
    /// Allows: All ReadOnly operations + creating issues, updating issues, 
    /// adding comments, updating comments.
    /// </summary>
    FullControl
}
