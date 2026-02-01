namespace Microbot.Skills.Teams;

/// <summary>
/// Defines the permission mode for the Teams skill.
/// </summary>
public enum TeamsSkillMode
{
    /// <summary>
    /// Read-only access to teams, channels, chats, and messages.
    /// Permissions: Team.ReadBasic.All, Channel.ReadBasic.All, ChannelMessage.Read.All, 
    ///              Chat.Read, ChatMessage.Read, User.Read
    /// </summary>
    ReadOnly = 0,

    /// <summary>
    /// Full access: read teams/channels/chats and send messages.
    /// Permissions: Team.ReadBasic.All, Channel.ReadBasic.All, ChannelMessage.Read.All,
    ///              ChannelMessage.Send, Chat.Read, ChatMessage.Read, ChatMessage.Send, User.Read
    /// </summary>
    Full = 1
}
