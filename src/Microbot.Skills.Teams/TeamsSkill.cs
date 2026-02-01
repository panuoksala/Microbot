namespace Microbot.Skills.Teams;

using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.SemanticKernel;
using Microbot.Core.Models;
using Microbot.Skills.Teams.Models;
using Microbot.Skills.Teams.Services;

/// <summary>
/// Microsoft Teams skill for Microbot.
/// Provides functions to read teams, channels, chats, and messages across all tenants.
/// Supports tracking unread messages using local timestamp storage.
/// </summary>
public class TeamsSkill
{
    private readonly TeamsSkillConfig _config;
    private readonly TeamsAuthenticationService _authService;
    private readonly ReadStateTracker _readStateTracker;
    private readonly ILogger<TeamsSkill>? _logger;
    private GraphServiceClient? _graphClient;
    private string? _currentUserId;

    /// <summary>
    /// Creates a new TeamsSkill instance.
    /// </summary>
    public TeamsSkill(
        TeamsSkillConfig config,
        ILogger<TeamsSkill>? logger = null,
        ILogger<TeamsAuthenticationService>? authLogger = null,
        ILogger<ReadStateTracker>? readStateLogger = null)
    {
        _config = config;
        _logger = logger;
        _authService = new TeamsAuthenticationService(config, authLogger);
        _readStateTracker = new ReadStateTracker(logger: readStateLogger);
    }

    /// <summary>
    /// Creates a new TeamsSkill instance with a single logger for all components.
    /// </summary>
    public TeamsSkill(
        TeamsSkillConfig config,
        ILoggerFactory? loggerFactory)
    {
        _config = config;
        _logger = loggerFactory?.CreateLogger<TeamsSkill>();
        _authService = new TeamsAuthenticationService(config, loggerFactory?.CreateLogger<TeamsAuthenticationService>());
        _readStateTracker = new ReadStateTracker(logger: loggerFactory?.CreateLogger<ReadStateTracker>());
    }

    /// <summary>
    /// Initializes the skill by loading read state and authenticating.
    /// </summary>
    public async Task InitializeAsync(
        Action<string>? deviceCodeCallback = null,
        CancellationToken cancellationToken = default)
    {
        await _readStateTracker.LoadAsync(cancellationToken);
        _graphClient = await _authService.GetGraphClientAsync(deviceCodeCallback, cancellationToken);
        
        // Get current user ID for filtering
        var me = await _graphClient.Me.GetAsync(cancellationToken: cancellationToken);
        _currentUserId = me?.Id;
    }

    private async Task<GraphServiceClient> GetClientAsync()
    {
        if (_graphClient == null)
        {
            throw new InvalidOperationException("TeamsSkill not initialized. Call InitializeAsync first.");
        }
        return _graphClient;
    }

    #region Team Functions

    /// <summary>
    /// Lists all teams the user is a member of, including teams from guest tenants.
    /// </summary>
    [KernelFunction("list_teams")]
    [Description("Lists all Microsoft Teams the user is a member of, including teams from guest tenants (other organizations).")]
    public async Task<string> ListTeamsAsync(
        [Description("Maximum number of teams to return (default: 50)")] int top = 50,
        CancellationToken cancellationToken = default)
    {
        var client = await GetClientAsync();
        
        var response = await client.Me.JoinedTeams.GetAsync(config =>
        {
            config.QueryParameters.Top = top;
            config.QueryParameters.Select = ["id", "displayName", "description", "tenantId", "visibility", "createdDateTime"];
        }, cancellationToken);

        if (response?.Value == null || response.Value.Count == 0)
        {
            return "No teams found.";
        }

        var teams = response.Value.Select(t => new Models.Team
        {
            Id = t.Id ?? string.Empty,
            DisplayName = t.DisplayName ?? "Unnamed Team",
            Description = t.Description,
            TenantId = t.TenantId,
            Visibility = t.Visibility?.ToString(),
            CreatedDateTime = t.CreatedDateTime
        }).ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"Found {teams.Count} team(s):");
        sb.AppendLine();

        foreach (var team in teams)
        {
            sb.AppendLine($"**{team.DisplayName}**");
            sb.AppendLine($"  - ID: {team.Id}");
            if (!string.IsNullOrEmpty(team.Description))
                sb.AppendLine($"  - Description: {team.Description}");
            if (!string.IsNullOrEmpty(team.TenantId))
                sb.AppendLine($"  - Tenant: {team.TenantId}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    #endregion

    #region Channel Functions

    /// <summary>
    /// Lists all channels in a team.
    /// </summary>
    [KernelFunction("list_channels")]
    [Description("Lists all channels in a Microsoft Teams team.")]
    public async Task<string> ListChannelsAsync(
        [Description("The ID of the team")] string teamId,
        CancellationToken cancellationToken = default)
    {
        var client = await GetClientAsync();
        
        var response = await client.Teams[teamId].Channels.GetAsync(config =>
        {
            config.QueryParameters.Select = ["id", "displayName", "description", "membershipType", "createdDateTime", "webUrl"];
        }, cancellationToken);

        if (response?.Value == null || response.Value.Count == 0)
        {
            return "No channels found in this team.";
        }

        var channels = response.Value.Select(c => new Models.Channel
        {
            Id = c.Id ?? string.Empty,
            DisplayName = c.DisplayName ?? "Unnamed Channel",
            Description = c.Description,
            TeamId = teamId,
            MembershipType = c.MembershipType?.ToString(),
            CreatedDateTime = c.CreatedDateTime,
            WebUrl = c.WebUrl
        }).ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"Found {channels.Count} channel(s):");
        sb.AppendLine();

        foreach (var channel in channels)
        {
            sb.AppendLine($"**{channel.DisplayName}**");
            sb.AppendLine($"  - ID: {channel.Id}");
            if (!string.IsNullOrEmpty(channel.Description))
                sb.AppendLine($"  - Description: {channel.Description}");
            sb.AppendLine($"  - Type: {channel.MembershipType ?? "standard"}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Gets details about a specific channel.
    /// </summary>
    [KernelFunction("get_channel")]
    [Description("Gets details about a specific channel in a team.")]
    public async Task<string> GetChannelAsync(
        [Description("The ID of the team")] string teamId,
        [Description("The ID of the channel")] string channelId,
        CancellationToken cancellationToken = default)
    {
        var client = await GetClientAsync();
        
        var channel = await client.Teams[teamId].Channels[channelId].GetAsync(config =>
        {
            config.QueryParameters.Select = ["id", "displayName", "description", "membershipType", "createdDateTime", "webUrl"];
        }, cancellationToken);

        if (channel == null)
        {
            return "Channel not found.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"**{channel.DisplayName}**");
        sb.AppendLine($"- ID: {channel.Id}");
        if (!string.IsNullOrEmpty(channel.Description))
            sb.AppendLine($"- Description: {channel.Description}");
        sb.AppendLine($"- Type: {channel.MembershipType?.ToString() ?? "standard"}");
        if (channel.CreatedDateTime.HasValue)
            sb.AppendLine($"- Created: {channel.CreatedDateTime:g}");
        if (!string.IsNullOrEmpty(channel.WebUrl))
            sb.AppendLine($"- Web URL: {channel.WebUrl}");

        return sb.ToString();
    }

    #endregion

    #region Channel Message Functions

    /// <summary>
    /// Lists messages in a channel.
    /// </summary>
    [KernelFunction("list_channel_messages")]
    [Description("Lists recent messages in a Teams channel.")]
    public async Task<string> ListChannelMessagesAsync(
        [Description("The ID of the team")] string teamId,
        [Description("The ID of the channel")] string channelId,
        [Description("Maximum number of messages to return (default: 20)")] int top = 20,
        CancellationToken cancellationToken = default)
    {
        var client = await GetClientAsync();
        
        var response = await client.Teams[teamId].Channels[channelId].Messages.GetAsync(config =>
        {
            config.QueryParameters.Top = top;
        }, cancellationToken);

        if (response?.Value == null || response.Value.Count == 0)
        {
            return "No messages found in this channel.";
        }

        var messages = MapChannelMessages(response.Value, teamId, channelId);
        return FormatChannelMessages(messages, teamId, channelId);
    }

    /// <summary>
    /// Gets unread messages from a channel.
    /// </summary>
    [KernelFunction("get_unread_channel_messages")]
    [Description("Gets unread messages from a Teams channel based on local read tracking.")]
    public async Task<string> GetUnreadChannelMessagesAsync(
        [Description("The ID of the team")] string teamId,
        [Description("The ID of the channel")] string channelId,
        [Description("Maximum number of messages to return (default: 50)")] int top = 50,
        CancellationToken cancellationToken = default)
    {
        var client = await GetClientAsync();
        
        var lastRead = _readStateTracker.GetChannelLastRead(teamId, channelId);
        
        var response = await client.Teams[teamId].Channels[channelId].Messages.GetAsync(config =>
        {
            config.QueryParameters.Top = top;
        }, cancellationToken);

        if (response?.Value == null || response.Value.Count == 0)
        {
            return "No messages found in this channel.";
        }

        var allMessages = MapChannelMessages(response.Value, teamId, channelId);
        
        // Filter to unread messages (after lastRead timestamp)
        var unreadMessages = allMessages
            .Where(m => _readStateTracker.IsChannelMessageUnread(teamId, channelId, m.CreatedDateTime))
            .Where(m => m.SenderEmail != _currentUserId) // Exclude own messages
            .ToList();

        if (unreadMessages.Count == 0)
        {
            return lastRead.HasValue 
                ? $"No unread messages since {lastRead:g}." 
                : "No unread messages (channel never marked as read).";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Found {unreadMessages.Count} unread message(s):");
        if (lastRead.HasValue)
            sb.AppendLine($"(Messages since {lastRead:g})");
        sb.AppendLine();
        sb.Append(FormatChannelMessages(unreadMessages, teamId, channelId, includeHeader: false));

        return sb.ToString();
    }

    /// <summary>
    /// Gets a specific message from a channel.
    /// </summary>
    [KernelFunction("get_channel_message")]
    [Description("Gets a specific message from a Teams channel.")]
    public async Task<string> GetChannelMessageAsync(
        [Description("The ID of the team")] string teamId,
        [Description("The ID of the channel")] string channelId,
        [Description("The ID of the message")] string messageId,
        CancellationToken cancellationToken = default)
    {
        var client = await GetClientAsync();
        
        var message = await client.Teams[teamId].Channels[channelId].Messages[messageId].GetAsync(
            cancellationToken: cancellationToken);

        if (message == null)
        {
            return "Message not found.";
        }

        var mapped = MapChannelMessage(message, teamId, channelId);
        return FormatChannelMessageDetail(mapped);
    }

    /// <summary>
    /// Sends a message to a channel.
    /// </summary>
    [KernelFunction("send_channel_message")]
    [Description("Sends a new message to a Teams channel. Requires Full mode.")]
    public async Task<string> SendChannelMessageAsync(
        [Description("The ID of the team")] string teamId,
        [Description("The ID of the channel")] string channelId,
        [Description("The message content to send")] string content,
        CancellationToken cancellationToken = default)
    {
        if (!_authService.CanSendMessages())
        {
            return "Error: Sending messages requires Full mode. Current mode is ReadOnly.";
        }

        var client = await GetClientAsync();
        
        var chatMessage = new Microsoft.Graph.Models.ChatMessage
        {
            Body = new ItemBody
            {
                Content = content,
                ContentType = BodyType.Text
            }
        };

        var result = await client.Teams[teamId].Channels[channelId].Messages.PostAsync(
            chatMessage, cancellationToken: cancellationToken);

        if (result == null)
        {
            return "Failed to send message.";
        }

        return $"Message sent successfully. Message ID: {result.Id}";
    }

    /// <summary>
    /// Replies to a message in a channel.
    /// </summary>
    [KernelFunction("reply_to_channel_message")]
    [Description("Replies to an existing message in a Teams channel. Requires Full mode.")]
    public async Task<string> ReplyToChannelMessageAsync(
        [Description("The ID of the team")] string teamId,
        [Description("The ID of the channel")] string channelId,
        [Description("The ID of the message to reply to")] string messageId,
        [Description("The reply content")] string content,
        CancellationToken cancellationToken = default)
    {
        if (!_authService.CanSendMessages())
        {
            return "Error: Sending messages requires Full mode. Current mode is ReadOnly.";
        }

        var client = await GetClientAsync();
        
        var reply = new Microsoft.Graph.Models.ChatMessage
        {
            Body = new ItemBody
            {
                Content = content,
                ContentType = BodyType.Text
            }
        };

        var result = await client.Teams[teamId].Channels[channelId].Messages[messageId].Replies.PostAsync(
            reply, cancellationToken: cancellationToken);

        if (result == null)
        {
            return "Failed to send reply.";
        }

        return $"Reply sent successfully. Reply ID: {result.Id}";
    }

    /// <summary>
    /// Marks a channel as read up to the current time.
    /// </summary>
    [KernelFunction("mark_channel_as_read")]
    [Description("Marks all messages in a channel as read up to the current time.")]
    public async Task<string> MarkChannelAsReadAsync(
        [Description("The ID of the team")] string teamId,
        [Description("The ID of the channel")] string channelId,
        CancellationToken cancellationToken = default)
    {
        _readStateTracker.MarkChannelAsRead(teamId, channelId);
        await _readStateTracker.SaveAsync(cancellationToken);
        
        return $"Channel marked as read at {DateTimeOffset.UtcNow:g}.";
    }

    #endregion

    #region Chat Functions

    /// <summary>
    /// Lists all chats (1:1 and group chats).
    /// </summary>
    [KernelFunction("list_chats")]
    [Description("Lists all 1:1 and group chats the user is part of.")]
    public async Task<string> ListChatsAsync(
        [Description("Maximum number of chats to return (default: 50)")] int top = 50,
        CancellationToken cancellationToken = default)
    {
        var client = await GetClientAsync();
        
        var response = await client.Me.Chats.GetAsync(config =>
        {
            config.QueryParameters.Top = top;
            config.QueryParameters.Expand = ["members"];
        }, cancellationToken);

        if (response?.Value == null || response.Value.Count == 0)
        {
            return "No chats found.";
        }

        var chats = response.Value.Select(c => new Models.Chat
        {
            Id = c.Id ?? string.Empty,
            Topic = c.Topic,
            ChatType = c.ChatType?.ToString() ?? "unknown",
            CreatedDateTime = c.CreatedDateTime,
            LastUpdatedDateTime = c.LastUpdatedDateTime,
            TenantId = c.TenantId,
            WebUrl = c.WebUrl,
            MemberNames = c.Members?
                .Where(m => m.DisplayName != null)
                .Select(m => m.DisplayName!)
                .ToList() ?? []
        }).ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"Found {chats.Count} chat(s):");
        sb.AppendLine();

        foreach (var chat in chats)
        {
            sb.AppendLine($"**{chat.DisplayName}**");
            sb.AppendLine($"  - ID: {chat.Id}");
            sb.AppendLine($"  - Type: {chat.ChatType}");
            if (chat.LastUpdatedDateTime.HasValue)
                sb.AppendLine($"  - Last activity: {chat.LastUpdatedDateTime:g}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Lists messages in a chat.
    /// </summary>
    [KernelFunction("list_chat_messages")]
    [Description("Lists recent messages in a 1:1 or group chat.")]
    public async Task<string> ListChatMessagesAsync(
        [Description("The ID of the chat")] string chatId,
        [Description("Maximum number of messages to return (default: 20)")] int top = 20,
        CancellationToken cancellationToken = default)
    {
        var client = await GetClientAsync();
        
        var response = await client.Me.Chats[chatId].Messages.GetAsync(config =>
        {
            config.QueryParameters.Top = top;
        }, cancellationToken);

        if (response?.Value == null || response.Value.Count == 0)
        {
            return "No messages found in this chat.";
        }

        var messages = MapChatMessages(response.Value, chatId);
        return FormatChatMessages(messages, chatId);
    }

    /// <summary>
    /// Gets unread messages from a chat.
    /// </summary>
    [KernelFunction("get_unread_chat_messages")]
    [Description("Gets unread messages from a chat based on local read tracking.")]
    public async Task<string> GetUnreadChatMessagesAsync(
        [Description("The ID of the chat")] string chatId,
        [Description("Maximum number of messages to return (default: 50)")] int top = 50,
        CancellationToken cancellationToken = default)
    {
        var client = await GetClientAsync();
        
        var lastRead = _readStateTracker.GetChatLastRead(chatId);
        
        var response = await client.Me.Chats[chatId].Messages.GetAsync(config =>
        {
            config.QueryParameters.Top = top;
        }, cancellationToken);

        if (response?.Value == null || response.Value.Count == 0)
        {
            return "No messages found in this chat.";
        }

        var allMessages = MapChatMessages(response.Value, chatId);
        
        // Filter to unread messages
        var unreadMessages = allMessages
            .Where(m => _readStateTracker.IsChatMessageUnread(chatId, m.CreatedDateTime))
            .Where(m => m.SenderEmail != _currentUserId) // Exclude own messages
            .ToList();

        if (unreadMessages.Count == 0)
        {
            return lastRead.HasValue 
                ? $"No unread messages since {lastRead:g}." 
                : "No unread messages (chat never marked as read).";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Found {unreadMessages.Count} unread message(s):");
        if (lastRead.HasValue)
            sb.AppendLine($"(Messages since {lastRead:g})");
        sb.AppendLine();
        sb.Append(FormatChatMessages(unreadMessages, chatId, includeHeader: false));

        return sb.ToString();
    }

    /// <summary>
    /// Sends a message to a chat.
    /// </summary>
    [KernelFunction("send_chat_message")]
    [Description("Sends a message to a 1:1 or group chat. Requires Full mode.")]
    public async Task<string> SendChatMessageAsync(
        [Description("The ID of the chat")] string chatId,
        [Description("The message content to send")] string content,
        CancellationToken cancellationToken = default)
    {
        if (!_authService.CanSendMessages())
        {
            return "Error: Sending messages requires Full mode. Current mode is ReadOnly.";
        }

        var client = await GetClientAsync();
        
        var chatMessage = new Microsoft.Graph.Models.ChatMessage
        {
            Body = new ItemBody
            {
                Content = content,
                ContentType = BodyType.Text
            }
        };

        var result = await client.Me.Chats[chatId].Messages.PostAsync(
            chatMessage, cancellationToken: cancellationToken);

        if (result == null)
        {
            return "Failed to send message.";
        }

        return $"Message sent successfully. Message ID: {result.Id}";
    }

    /// <summary>
    /// Marks a chat as read up to the current time.
    /// </summary>
    [KernelFunction("mark_chat_as_read")]
    [Description("Marks all messages in a chat as read up to the current time.")]
    public async Task<string> MarkChatAsReadAsync(
        [Description("The ID of the chat")] string chatId,
        CancellationToken cancellationToken = default)
    {
        _readStateTracker.MarkChatAsRead(chatId);
        await _readStateTracker.SaveAsync(cancellationToken);
        
        return $"Chat marked as read at {DateTimeOffset.UtcNow:g}.";
    }

    #endregion

    #region Unread Summary Functions

    /// <summary>
    /// Gets a summary of all unread messages across all teams and chats.
    /// </summary>
    [KernelFunction("get_unread_summary")]
    [Description("Gets a summary of unread messages across all teams and chats.")]
    public async Task<string> GetUnreadSummaryAsync(
        [Description("Maximum messages to check per channel/chat (default: 20)")] int messagesPerLocation = 20,
        CancellationToken cancellationToken = default)
    {
        var client = await GetClientAsync();
        var sb = new StringBuilder();
        var totalUnread = 0;

        // Get all teams
        var teamsResponse = await client.Me.JoinedTeams.GetAsync(cancellationToken: cancellationToken);
        var teams = teamsResponse?.Value ?? [];

        sb.AppendLine("**Unread Messages Summary**");
        sb.AppendLine();

        // Check each team's channels
        foreach (var team in teams)
        {
            if (team.Id == null) continue;

            var channelsResponse = await client.Teams[team.Id].Channels.GetAsync(cancellationToken: cancellationToken);
            var channels = channelsResponse?.Value ?? [];

            var teamUnread = 0;
            var channelSummaries = new List<string>();

            foreach (var channel in channels)
            {
                if (channel.Id == null) continue;

                try
                {
                    var messagesResponse = await client.Teams[team.Id].Channels[channel.Id].Messages.GetAsync(config =>
                    {
                        config.QueryParameters.Top = messagesPerLocation;
                    }, cancellationToken);

                    var messages = messagesResponse?.Value ?? [];
                    var unreadCount = messages.Count(m => 
                        _readStateTracker.IsChannelMessageUnread(team.Id, channel.Id, m.CreatedDateTime) &&
                        m.From?.User?.Id != _currentUserId);

                    if (unreadCount > 0)
                    {
                        teamUnread += unreadCount;
                        channelSummaries.Add($"    - {channel.DisplayName}: {unreadCount} unread");
                    }
                }
                catch
                {
                    // Skip channels we can't access
                }
            }

            if (teamUnread > 0)
            {
                sb.AppendLine($"**{team.DisplayName}**: {teamUnread} unread");
                foreach (var summary in channelSummaries)
                {
                    sb.AppendLine(summary);
                }
                sb.AppendLine();
                totalUnread += teamUnread;
            }
        }

        // Check chats
        var chatsResponse = await client.Me.Chats.GetAsync(config =>
        {
            config.QueryParameters.Top = 50;
            config.QueryParameters.Expand = ["members"];
        }, cancellationToken);

        var chats = chatsResponse?.Value ?? [];
        var chatUnreadTotal = 0;
        var chatSummaries = new List<string>();

        foreach (var chat in chats)
        {
            if (chat.Id == null) continue;

            try
            {
                var messagesResponse = await client.Me.Chats[chat.Id].Messages.GetAsync(config =>
                {
                    config.QueryParameters.Top = messagesPerLocation;
                }, cancellationToken);

                var messages = messagesResponse?.Value ?? [];
                var unreadCount = messages.Count(m => 
                    _readStateTracker.IsChatMessageUnread(chat.Id, m.CreatedDateTime) &&
                    m.From?.User?.Id != _currentUserId);

                if (unreadCount > 0)
                {
                    chatUnreadTotal += unreadCount;
                    var chatName = chat.Topic ?? 
                        string.Join(", ", chat.Members?.Take(3).Select(m => m.DisplayName) ?? []) ?? 
                        $"Chat {chat.Id[..8]}...";
                    chatSummaries.Add($"  - {chatName}: {unreadCount} unread");
                }
            }
            catch
            {
                // Skip chats we can't access
            }
        }

        if (chatUnreadTotal > 0)
        {
            sb.AppendLine($"**Chats**: {chatUnreadTotal} unread");
            foreach (var summary in chatSummaries)
            {
                sb.AppendLine(summary);
            }
            sb.AppendLine();
            totalUnread += chatUnreadTotal;
        }

        if (totalUnread == 0)
        {
            return "No unread messages across all teams and chats.";
        }

        sb.Insert(0, $"Total: {totalUnread} unread message(s)\n\n");
        return sb.ToString();
    }

    #endregion

    #region Helper Methods

    private List<ChannelMessage> MapChannelMessages(IEnumerable<Microsoft.Graph.Models.ChatMessage> messages, string teamId, string channelId)
    {
        return messages.Select(m => MapChannelMessage(m, teamId, channelId)).ToList();
    }

    private ChannelMessage MapChannelMessage(Microsoft.Graph.Models.ChatMessage m, string teamId, string channelId)
    {
        return new ChannelMessage
        {
            Id = m.Id ?? string.Empty,
            TeamId = teamId,
            ChannelId = channelId,
            SenderDisplayName = m.From?.User?.DisplayName ?? m.From?.Application?.DisplayName ?? "Unknown",
            SenderEmail = m.From?.User?.Id,
            Body = StripHtml(m.Body?.Content),
            BodyContentType = m.Body?.ContentType?.ToString(),
            CreatedDateTime = m.CreatedDateTime,
            LastModifiedDateTime = m.LastModifiedDateTime,
            Subject = m.Subject,
            Importance = m.Importance?.ToString(),
            WebUrl = m.WebUrl,
            IsDeleted = m.DeletedDateTime.HasValue
        };
    }

    private List<Models.ChatMessage> MapChatMessages(IEnumerable<Microsoft.Graph.Models.ChatMessage> messages, string chatId)
    {
        return messages.Select(m => new Models.ChatMessage
        {
            Id = m.Id ?? string.Empty,
            ChatId = chatId,
            SenderDisplayName = m.From?.User?.DisplayName ?? m.From?.Application?.DisplayName ?? "Unknown",
            SenderEmail = m.From?.User?.Id,
            Body = StripHtml(m.Body?.Content),
            BodyContentType = m.Body?.ContentType?.ToString(),
            CreatedDateTime = m.CreatedDateTime,
            LastModifiedDateTime = m.LastModifiedDateTime,
            Importance = m.Importance?.ToString(),
            IsDeleted = m.DeletedDateTime.HasValue,
            MessageType = m.MessageType?.ToString()
        }).ToList();
    }

    private string FormatChannelMessages(List<ChannelMessage> messages, string teamId, string channelId, bool includeHeader = true)
    {
        var sb = new StringBuilder();
        
        if (includeHeader)
        {
            sb.AppendLine($"Found {messages.Count} message(s):");
            sb.AppendLine();
        }

        foreach (var msg in messages.Where(m => !m.IsDeleted))
        {
            var isUnread = _readStateTracker.IsChannelMessageUnread(teamId, channelId, msg.CreatedDateTime);
            var unreadMarker = isUnread ? "🔵 " : "";
            
            sb.AppendLine($"{unreadMarker}**{msg.SenderDisplayName}** ({msg.CreatedDateTime:g}):");
            if (!string.IsNullOrEmpty(msg.Subject))
                sb.AppendLine($"Subject: {msg.Subject}");
            sb.AppendLine(msg.Body ?? "(no content)");
            sb.AppendLine($"[ID: {msg.Id}]");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private string FormatChannelMessageDetail(ChannelMessage msg)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"**Message from {msg.SenderDisplayName}**");
        sb.AppendLine($"- ID: {msg.Id}");
        sb.AppendLine($"- Sent: {msg.CreatedDateTime:g}");
        if (!string.IsNullOrEmpty(msg.Subject))
            sb.AppendLine($"- Subject: {msg.Subject}");
        if (!string.IsNullOrEmpty(msg.Importance) && msg.Importance != "Normal")
            sb.AppendLine($"- Importance: {msg.Importance}");
        sb.AppendLine();
        sb.AppendLine("**Content:**");
        sb.AppendLine(msg.Body ?? "(no content)");
        if (!string.IsNullOrEmpty(msg.WebUrl))
        {
            sb.AppendLine();
            sb.AppendLine($"[Open in Teams]({msg.WebUrl})");
        }
        return sb.ToString();
    }

    private string FormatChatMessages(List<Models.ChatMessage> messages, string chatId, bool includeHeader = true)
    {
        var sb = new StringBuilder();
        
        if (includeHeader)
        {
            sb.AppendLine($"Found {messages.Count} message(s):");
            sb.AppendLine();
        }

        foreach (var msg in messages.Where(m => !m.IsDeleted && m.MessageType == "Message"))
        {
            var isUnread = _readStateTracker.IsChatMessageUnread(chatId, msg.CreatedDateTime);
            var unreadMarker = isUnread ? "🔵 " : "";
            
            sb.AppendLine($"{unreadMarker}**{msg.SenderDisplayName}** ({msg.CreatedDateTime:g}):");
            sb.AppendLine(msg.Body ?? "(no content)");
            sb.AppendLine($"[ID: {msg.Id}]");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string? StripHtml(string? html)
    {
        if (string.IsNullOrEmpty(html)) return html;
        
        // Simple HTML stripping - remove tags
        var result = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]*>", "");
        // Decode common HTML entities
        result = result.Replace("&nbsp;", " ")
                      .Replace("&amp;", "&")
                      .Replace("&lt;", "<")
                      .Replace("&gt;", ">")
                      .Replace("&quot;", "\"");
        return result.Trim();
    }

    #endregion

}
