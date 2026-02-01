namespace Microbot.Skills.Teams.Models;

/// <summary>
/// Represents a 1:1 or group chat in Microsoft Teams.
/// </summary>
public class Chat
{
    /// <summary>
    /// The unique identifier of the chat.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The topic/title of the chat (for group chats).
    /// </summary>
    public string? Topic { get; set; }

    /// <summary>
    /// The type of chat: oneOnOne, group, or meeting.
    /// </summary>
    public string ChatType { get; set; } = string.Empty;

    /// <summary>
    /// When the chat was created.
    /// </summary>
    public DateTimeOffset? CreatedDateTime { get; set; }

    /// <summary>
    /// When the chat was last updated.
    /// </summary>
    public DateTimeOffset? LastUpdatedDateTime { get; set; }

    /// <summary>
    /// The tenant ID where this chat belongs.
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// The web URL to access this chat in Teams.
    /// </summary>
    public string? WebUrl { get; set; }

    /// <summary>
    /// List of member display names in the chat.
    /// </summary>
    public List<string> MemberNames { get; set; } = [];

    /// <summary>
    /// A display-friendly name for the chat (computed from topic or members).
    /// </summary>
    public string DisplayName => !string.IsNullOrEmpty(Topic) 
        ? Topic 
        : MemberNames.Count > 0 
            ? string.Join(", ", MemberNames) 
            : $"Chat {Id[..8]}...";
}
