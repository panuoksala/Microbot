namespace Microbot.Skills.Slack;

using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using SlackNet;
using SlackNet.Events;
using SlackNet.WebApi;
using Microbot.Core.Models;
using Microbot.Skills.Slack.Models;
using Microbot.Skills.Slack.Services;

/// <summary>
/// Slack skill for Microbot.
/// Provides functions to read channels, direct messages, and send messages.
/// Supports tracking unread messages using local timestamp storage.
/// </summary>
public class SlackSkill
{
    private readonly SlackSkillConfig _config;
    private readonly SlackAuthenticationService _authService;
    private readonly SlackReadStateTracker _readStateTracker;
    private readonly ILogger<SlackSkill>? _logger;
    private ISlackApiClient? _slackClient;
    private readonly Dictionary<string, SlackUser> _userCache = [];

    /// <summary>
    /// Creates a new SlackSkill instance.
    /// </summary>
    public SlackSkill(
        SlackSkillConfig config,
        ILogger<SlackSkill>? logger = null,
        ILogger<SlackAuthenticationService>? authLogger = null,
        ILogger<SlackReadStateTracker>? readStateLogger = null)
    {
        _config = config;
        _logger = logger;
        _authService = new SlackAuthenticationService(config, authLogger);
        _readStateTracker = new SlackReadStateTracker(config.ReadStatePath, readStateLogger);
    }

    /// <summary>
    /// Creates a new SlackSkill instance with a logger factory.
    /// </summary>
    public SlackSkill(
        SlackSkillConfig config,
        ILoggerFactory? loggerFactory)
    {
        _config = config;
        _logger = loggerFactory?.CreateLogger<SlackSkill>();
        _authService = new SlackAuthenticationService(config, loggerFactory?.CreateLogger<SlackAuthenticationService>());
        _readStateTracker = new SlackReadStateTracker(config.ReadStatePath, loggerFactory?.CreateLogger<SlackReadStateTracker>());
    }

    /// <summary>
    /// Initializes the skill by loading read state and authenticating.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _readStateTracker.LoadAsync(cancellationToken);
        _slackClient = await _authService.GetSlackClientAsync(cancellationToken);
    }

    private ISlackApiClient GetClient()
    {
        if (_slackClient == null)
        {
            throw new InvalidOperationException("SlackSkill not initialized. Call InitializeAsync first.");
        }
        return _slackClient;
    }

    #region Channel Functions

    /// <summary>
    /// Lists all channels the bot has access to.
    /// </summary>
    [KernelFunction("list_channels")]
    [Description("Lists all Slack channels the bot has access to, including public and private channels.")]
    public async Task<string> ListChannelsAsync(
        [Description("Include archived channels (default: false)")] bool includeArchived = false,
        [Description("Maximum number of channels to return (default: 100)")] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var client = GetClient();

        var response = await client.Conversations.List(
            types: [ConversationType.PublicChannel, ConversationType.PrivateChannel],
            excludeArchived: !includeArchived,
            limit: limit,
            cancellationToken: cancellationToken);

        if (response.Channels == null || response.Channels.Count == 0)
        {
            return "No channels found.";
        }

        var channels = response.Channels.Select(c => new SlackChannel
        {
            Id = c.Id,
            Name = c.Name ?? "Unnamed",
            Topic = c.Topic?.Value,
            Purpose = c.Purpose?.Value,
            IsPrivate = c.IsPrivate,
            IsArchived = c.IsArchived,
            IsMember = c.IsMember,
            MemberCount = c.NumMembers,
            CreatedDateTime = DateTimeOffset.FromUnixTimeSeconds(c.Created).DateTime
        }).ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"Found {channels.Count} channel(s):");
        sb.AppendLine();

        foreach (var channel in channels)
        {
            var privateMarker = channel.IsPrivate ? "🔒 " : "";
            var memberMarker = channel.IsMember ? "✓ " : "";
            sb.AppendLine($"{memberMarker}{privateMarker}**#{channel.Name}**");
            sb.AppendLine($"  - ID: {channel.Id}");
            if (!string.IsNullOrEmpty(channel.Topic))
                sb.AppendLine($"  - Topic: {channel.Topic}");
            if (!string.IsNullOrEmpty(channel.Purpose))
                sb.AppendLine($"  - Purpose: {channel.Purpose}");
            sb.AppendLine($"  - Members: {channel.MemberCount}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Gets information about a specific channel.
    /// </summary>
    [KernelFunction("get_channel")]
    [Description("Gets detailed information about a specific Slack channel.")]
    public async Task<string> GetChannelAsync(
        [Description("The channel ID or name (with or without #)")] string channel,
        CancellationToken cancellationToken = default)
    {
        var client = GetClient();
        var channelId = await ResolveChannelIdAsync(channel, cancellationToken);

        var c = await client.Conversations.Info(channelId, cancellationToken: cancellationToken);

        if (c == null)
        {
            return $"Channel not found: {channel}";
        }

        var sb = new StringBuilder();
        var privateMarker = c.IsPrivate ? "🔒 Private " : "Public ";
        sb.AppendLine($"**#{c.Name}** ({privateMarker}Channel)");
        sb.AppendLine($"- ID: {c.Id}");
        if (!string.IsNullOrEmpty(c.Topic?.Value))
            sb.AppendLine($"- Topic: {c.Topic.Value}");
        if (!string.IsNullOrEmpty(c.Purpose?.Value))
            sb.AppendLine($"- Purpose: {c.Purpose.Value}");
        sb.AppendLine($"- Members: {c.NumMembers}");
        sb.AppendLine($"- Created: {DateTimeOffset.FromUnixTimeSeconds(c.Created):g}");
        sb.AppendLine($"- Bot is member: {(c.IsMember ? "Yes" : "No")}");

        return sb.ToString();
    }

    /// <summary>
    /// Lists messages in a channel.
    /// </summary>
    [KernelFunction("list_channel_messages")]
    [Description("Lists recent messages in a Slack channel.")]
    public async Task<string> ListChannelMessagesAsync(
        [Description("The channel ID or name (with or without #)")] string channel,
        [Description("Maximum number of messages to return (default: 20)")] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var client = GetClient();
        var channelId = await ResolveChannelIdAsync(channel, cancellationToken);
        var channelName = await GetChannelNameAsync(channelId, cancellationToken);

        var response = await client.Conversations.History(
            channelId,
            limit: limit,
            cancellationToken: cancellationToken);

        if (response.Messages == null || response.Messages.Count == 0)
        {
            return $"No messages found in #{channelName}.";
        }

        var messages = await MapMessagesAsync(response.Messages, channelId, channelName, cancellationToken);
        return FormatMessages(messages, $"#{channelName}", channelId, isChannel: true);
    }

    /// <summary>
    /// Gets unread messages from a channel.
    /// </summary>
    [KernelFunction("get_unread_channel_messages")]
    [Description("Gets unread messages from a Slack channel based on local read tracking.")]
    public async Task<string> GetUnreadChannelMessagesAsync(
        [Description("The channel ID or name (with or without #)")] string channel,
        [Description("Maximum number of messages to return (default: 50)")] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var client = GetClient();
        var channelId = await ResolveChannelIdAsync(channel, cancellationToken);
        var channelName = await GetChannelNameAsync(channelId, cancellationToken);
        var lastRead = _readStateTracker.GetChannelLastRead(channelId);

        var response = await client.Conversations.History(
            channelId,
            limit: limit,
            cancellationToken: cancellationToken);

        if (response.Messages == null || response.Messages.Count == 0)
        {
            return $"No messages found in #{channelName}.";
        }

        var allMessages = await MapMessagesAsync(response.Messages, channelId, channelName, cancellationToken);
        var botUserId = _authService.GetBotUserId();

        // Filter to unread messages (after lastRead timestamp), excluding bot's own messages
        var unreadMessages = allMessages
            .Where(m => _readStateTracker.IsChannelMessageUnread(channelId, m.Timestamp))
            .Where(m => m.UserId != botUserId)
            .ToList();

        if (unreadMessages.Count == 0)
        {
            return lastRead.HasValue
                ? $"No unread messages in #{channelName} since {lastRead:g}."
                : $"No unread messages in #{channelName} (channel never marked as read).";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Found {unreadMessages.Count} unread message(s) in #{channelName}:");
        if (lastRead.HasValue)
            sb.AppendLine($"(Messages since {lastRead:g})");
        sb.AppendLine();
        sb.Append(FormatMessages(unreadMessages, $"#{channelName}", channelId, isChannel: true, includeHeader: false));

        return sb.ToString();
    }

    /// <summary>
    /// Sends a message to a channel.
    /// </summary>
    [KernelFunction("send_channel_message")]
    [Description("Sends a message to a Slack channel. Requires Full mode.")]
    public async Task<string> SendChannelMessageAsync(
        [Description("The channel ID or name (with or without #)")] string channel,
        [Description("The message text to send")] string text,
        CancellationToken cancellationToken = default)
    {
        if (!_authService.CanSendMessages())
        {
            return "Error: Sending messages requires Full mode. Current mode is ReadOnly.";
        }

        var client = GetClient();
        var channelId = await ResolveChannelIdAsync(channel, cancellationToken);

        var response = await client.Chat.PostMessage(new Message
        {
            Channel = channelId,
            Text = text
        }, cancellationToken);

        return $"Message sent successfully to #{await GetChannelNameAsync(channelId, cancellationToken)}. Timestamp: {response.Ts}";
    }

    /// <summary>
    /// Marks a channel as read up to the current time.
    /// </summary>
    [KernelFunction("mark_channel_as_read")]
    [Description("Marks all messages in a channel as read up to the current time.")]
    public async Task<string> MarkChannelAsReadAsync(
        [Description("The channel ID or name (with or without #)")] string channel,
        CancellationToken cancellationToken = default)
    {
        var channelId = await ResolveChannelIdAsync(channel, cancellationToken);
        var channelName = await GetChannelNameAsync(channelId, cancellationToken);

        _readStateTracker.MarkChannelAsRead(channelId);
        await _readStateTracker.SaveAsync(cancellationToken);

        return $"#{channelName} marked as read at {DateTimeOffset.UtcNow:g}.";
    }

    #endregion

    #region Direct Message Functions

    /// <summary>
    /// Lists all direct message conversations.
    /// </summary>
    [KernelFunction("list_direct_messages")]
    [Description("Lists all direct message (DM) conversations, including 1:1 and group DMs.")]
    public async Task<string> ListDirectMessagesAsync(
        [Description("Maximum number of conversations to return (default: 50)")] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var client = GetClient();

        var response = await client.Conversations.List(
            types: [ConversationType.Im, ConversationType.Mpim],
            limit: limit,
            cancellationToken: cancellationToken);

        if (response.Channels == null || response.Channels.Count == 0)
        {
            return "No direct message conversations found.";
        }

        var conversations = new List<SlackConversation>();

        foreach (var c in response.Channels)
        {
            var conv = new SlackConversation
            {
                Id = c.Id,
                IsDirectMessage = c.IsIm,
                IsGroupMessage = c.IsMpim,
                UserId = c.User
            };

            // Get member names for display
            if (c.IsIm && !string.IsNullOrEmpty(c.User))
            {
                var user = await GetUserAsync(c.User, cancellationToken);
                conv.Name = user?.DisplayName ?? user?.RealName ?? user?.Name ?? c.User;
                conv.MemberIds = [c.User];
                conv.MemberNames = [conv.Name];
            }
            else if (c.IsMpim)
            {
                conv.Name = c.Name ?? "Group DM";
                // For group DMs, we'd need to fetch members separately
            }

            conversations.Add(conv);
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Found {conversations.Count} direct message conversation(s):");
        sb.AppendLine();

        foreach (var conv in conversations)
        {
            var typeMarker = conv.IsGroupMessage ? "👥 Group: " : "💬 ";
            sb.AppendLine($"{typeMarker}**{conv.Name}**");
            sb.AppendLine($"  - ID: {conv.Id}");
            sb.AppendLine($"  - Type: {(conv.IsGroupMessage ? "Group DM" : "Direct Message")}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Lists messages in a direct message conversation.
    /// </summary>
    [KernelFunction("list_dm_messages")]
    [Description("Lists recent messages in a direct message conversation.")]
    public async Task<string> ListDmMessagesAsync(
        [Description("The conversation ID or user name/ID for 1:1 DMs")] string conversation,
        [Description("Maximum number of messages to return (default: 20)")] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var client = GetClient();
        var conversationId = await ResolveConversationIdAsync(conversation, cancellationToken);
        var conversationName = await GetConversationNameAsync(conversationId, cancellationToken);

        var response = await client.Conversations.History(
            conversationId,
            limit: limit,
            cancellationToken: cancellationToken);

        if (response.Messages == null || response.Messages.Count == 0)
        {
            return $"No messages found in conversation with {conversationName}.";
        }

        var messages = await MapMessagesAsync(response.Messages, conversationId, conversationName, cancellationToken);
        return FormatMessages(messages, conversationName, conversationId, isChannel: false);
    }

    /// <summary>
    /// Gets unread messages from a direct message conversation.
    /// </summary>
    [KernelFunction("get_unread_dm_messages")]
    [Description("Gets unread messages from a direct message conversation based on local read tracking.")]
    public async Task<string> GetUnreadDmMessagesAsync(
        [Description("The conversation ID or user name/ID for 1:1 DMs")] string conversation,
        [Description("Maximum number of messages to return (default: 50)")] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var client = GetClient();
        var conversationId = await ResolveConversationIdAsync(conversation, cancellationToken);
        var conversationName = await GetConversationNameAsync(conversationId, cancellationToken);
        var lastRead = _readStateTracker.GetConversationLastRead(conversationId);

        var response = await client.Conversations.History(
            conversationId,
            limit: limit,
            cancellationToken: cancellationToken);

        if (response.Messages == null || response.Messages.Count == 0)
        {
            return $"No messages found in conversation with {conversationName}.";
        }

        var allMessages = await MapMessagesAsync(response.Messages, conversationId, conversationName, cancellationToken);
        var botUserId = _authService.GetBotUserId();

        // Filter to unread messages, excluding bot's own messages
        var unreadMessages = allMessages
            .Where(m => _readStateTracker.IsConversationMessageUnread(conversationId, m.Timestamp))
            .Where(m => m.UserId != botUserId)
            .ToList();

        if (unreadMessages.Count == 0)
        {
            return lastRead.HasValue
                ? $"No unread messages from {conversationName} since {lastRead:g}."
                : $"No unread messages from {conversationName} (conversation never marked as read).";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Found {unreadMessages.Count} unread message(s) from {conversationName}:");
        if (lastRead.HasValue)
            sb.AppendLine($"(Messages since {lastRead:g})");
        sb.AppendLine();
        sb.Append(FormatMessages(unreadMessages, conversationName, conversationId, isChannel: false, includeHeader: false));

        return sb.ToString();
    }

    /// <summary>
    /// Sends a direct message to a user.
    /// </summary>
    [KernelFunction("send_direct_message")]
    [Description("Sends a direct message to a user. Requires Full mode.")]
    public async Task<string> SendDirectMessageAsync(
        [Description("The user ID, username, or conversation ID")] string user,
        [Description("The message text to send")] string text,
        CancellationToken cancellationToken = default)
    {
        if (!_authService.CanSendMessages())
        {
            return "Error: Sending messages requires Full mode. Current mode is ReadOnly.";
        }

        var client = GetClient();
        var conversationId = await ResolveConversationIdAsync(user, cancellationToken);

        var response = await client.Chat.PostMessage(new Message
        {
            Channel = conversationId,
            Text = text
        }, cancellationToken);

        var conversationName = await GetConversationNameAsync(conversationId, cancellationToken);
        return $"Message sent successfully to {conversationName}. Timestamp: {response.Ts}";
    }

    /// <summary>
    /// Marks a direct message conversation as read.
    /// </summary>
    [KernelFunction("mark_dm_as_read")]
    [Description("Marks all messages in a direct message conversation as read up to the current time.")]
    public async Task<string> MarkDmAsReadAsync(
        [Description("The conversation ID or user name/ID")] string conversation,
        CancellationToken cancellationToken = default)
    {
        var conversationId = await ResolveConversationIdAsync(conversation, cancellationToken);
        var conversationName = await GetConversationNameAsync(conversationId, cancellationToken);

        _readStateTracker.MarkConversationAsRead(conversationId);
        await _readStateTracker.SaveAsync(cancellationToken);

        return $"Conversation with {conversationName} marked as read at {DateTimeOffset.UtcNow:g}.";
    }

    #endregion

    #region Unread Summary Functions

    /// <summary>
    /// Gets a summary of all unread messages across channels and DMs.
    /// </summary>
    [KernelFunction("get_unread_summary")]
    [Description("Gets a summary of unread messages across all channels and direct messages.")]
    public async Task<string> GetUnreadSummaryAsync(
        [Description("Maximum messages to check per channel/conversation (default: 20)")] int messagesPerLocation = 20,
        CancellationToken cancellationToken = default)
    {
        var client = GetClient();
        var sb = new StringBuilder();
        var totalUnread = 0;
        var botUserId = _authService.GetBotUserId();

        sb.AppendLine("**Unread Messages Summary**");
        sb.AppendLine();

        // Check channels
        var channelsResponse = await client.Conversations.List(
            types: [ConversationType.PublicChannel, ConversationType.PrivateChannel],
            excludeArchived: true,
            cancellationToken: cancellationToken);

        var channelUnreadTotal = 0;
        var channelSummaries = new List<string>();

        if (channelsResponse.Channels != null)
        {
            foreach (var channel in channelsResponse.Channels.Where(c => c.IsMember))
            {
                try
                {
                    var messagesResponse = await client.Conversations.History(
                        channel.Id,
                        limit: messagesPerLocation,
                        cancellationToken: cancellationToken);

                    if (messagesResponse.Messages != null)
                    {
                        var unreadCount = messagesResponse.Messages.Count(m =>
                        {
                            var ts = ParseSlackTimestamp(m.Ts);
                            return _readStateTracker.IsChannelMessageUnread(channel.Id, ts) &&
                                   m.User != botUserId;
                        });

                        if (unreadCount > 0)
                        {
                            channelUnreadTotal += unreadCount;
                            channelSummaries.Add($"  - #{channel.Name}: {unreadCount} unread");
                        }
                    }
                }
                catch
                {
                    // Skip channels we can't access
                }
            }
        }

        if (channelUnreadTotal > 0)
        {
            sb.AppendLine($"**Channels**: {channelUnreadTotal} unread");
            foreach (var summary in channelSummaries)
            {
                sb.AppendLine(summary);
            }
            sb.AppendLine();
            totalUnread += channelUnreadTotal;
        }

        // Check DMs
        var dmsResponse = await client.Conversations.List(
            types: [ConversationType.Im, ConversationType.Mpim],
            cancellationToken: cancellationToken);

        var dmUnreadTotal = 0;
        var dmSummaries = new List<string>();

        if (dmsResponse.Channels != null)
        {
            foreach (var dm in dmsResponse.Channels)
            {
                try
                {
                    var messagesResponse = await client.Conversations.History(
                        dm.Id,
                        limit: messagesPerLocation,
                        cancellationToken: cancellationToken);

                    if (messagesResponse.Messages != null)
                    {
                        var unreadCount = messagesResponse.Messages.Count(m =>
                        {
                            var ts = ParseSlackTimestamp(m.Ts);
                            return _readStateTracker.IsConversationMessageUnread(dm.Id, ts) &&
                                   m.User != botUserId;
                        });

                        if (unreadCount > 0)
                        {
                            dmUnreadTotal += unreadCount;
                            var dmName = await GetConversationNameAsync(dm.Id, cancellationToken);
                            dmSummaries.Add($"  - {dmName}: {unreadCount} unread");
                        }
                    }
                }
                catch
                {
                    // Skip DMs we can't access
                }
            }
        }

        if (dmUnreadTotal > 0)
        {
            sb.AppendLine($"**Direct Messages**: {dmUnreadTotal} unread");
            foreach (var summary in dmSummaries)
            {
                sb.AppendLine(summary);
            }
            sb.AppendLine();
            totalUnread += dmUnreadTotal;
        }

        if (totalUnread == 0)
        {
            return "No unread messages across all channels and direct messages.";
        }

        sb.Insert(0, $"Total: {totalUnread} unread message(s)\n\n");
        return sb.ToString();
    }

    #endregion

    #region User Functions

    /// <summary>
    /// Gets information about a user.
    /// </summary>
    [KernelFunction("get_user")]
    [Description("Gets information about a Slack user.")]
    public async Task<string> GetUserInfoAsync(
        [Description("The user ID or username")] string user,
        CancellationToken cancellationToken = default)
    {
        var userInfo = await GetUserAsync(user, cancellationToken);

        if (userInfo == null)
        {
            return $"User not found: {user}";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"**{userInfo.DisplayName ?? userInfo.RealName ?? userInfo.Name}**");
        sb.AppendLine($"- ID: {userInfo.Id}");
        sb.AppendLine($"- Username: @{userInfo.Name}");
        if (!string.IsNullOrEmpty(userInfo.RealName))
            sb.AppendLine($"- Real Name: {userInfo.RealName}");
        if (!string.IsNullOrEmpty(userInfo.Email))
            sb.AppendLine($"- Email: {userInfo.Email}");
        if (userInfo.IsBot)
            sb.AppendLine("- Type: Bot");
        if (userInfo.IsAdmin)
            sb.AppendLine("- Role: Admin");
        if (userInfo.IsOwner)
            sb.AppendLine("- Role: Owner");
        if (!string.IsNullOrEmpty(userInfo.StatusText))
            sb.AppendLine($"- Status: {userInfo.StatusEmoji} {userInfo.StatusText}");
        if (!string.IsNullOrEmpty(userInfo.Timezone))
            sb.AppendLine($"- Timezone: {userInfo.Timezone}");

        return sb.ToString();
    }

    #endregion

    #region Helper Methods

    private async Task<string> ResolveChannelIdAsync(string channel, CancellationToken cancellationToken)
    {
        // If it looks like an ID, use it directly
        if (channel.StartsWith("C") || channel.StartsWith("G"))
            return channel;

        // Remove # prefix if present
        var channelName = channel.TrimStart('#');

        var client = GetClient();
        var response = await client.Conversations.List(
            types: [ConversationType.PublicChannel, ConversationType.PrivateChannel],
            cancellationToken: cancellationToken);

        var found = response.Channels?.FirstOrDefault(c =>
            c.Name?.Equals(channelName, StringComparison.OrdinalIgnoreCase) == true);

        return found?.Id ?? throw new ArgumentException($"Channel not found: {channel}");
    }

    private async Task<string> GetChannelNameAsync(string channelId, CancellationToken cancellationToken)
    {
        var client = GetClient();
        try
        {
            var info = await client.Conversations.Info(channelId, cancellationToken: cancellationToken);
            return info?.Name ?? channelId;
        }
        catch
        {
            return channelId;
        }
    }

    private async Task<string> ResolveConversationIdAsync(string conversation, CancellationToken cancellationToken)
    {
        // If it looks like a conversation ID, use it directly
        if (conversation.StartsWith("D") || conversation.StartsWith("G"))
            return conversation;

        // Try to find user and open DM
        var client = GetClient();

        // Remove @ prefix if present
        var userName = conversation.TrimStart('@');

        // Try to find user by name
        var usersResponse = await client.Users.List();
        var user = usersResponse.Members?.FirstOrDefault(u =>
            u.Name?.Equals(userName, StringComparison.OrdinalIgnoreCase) == true ||
            u.Profile?.DisplayName?.Equals(userName, StringComparison.OrdinalIgnoreCase) == true ||
            u.RealName?.Equals(userName, StringComparison.OrdinalIgnoreCase) == true);

        if (user != null)
        {
            // Open DM with user - Conversations.Open returns the channel ID directly
            var dmChannel = await client.Conversations.Open([user.Id], cancellationToken: cancellationToken);
            return dmChannel ?? throw new ArgumentException($"Could not open DM with user: {conversation}");
        }

        throw new ArgumentException($"User or conversation not found: {conversation}");
    }

    private async Task<string> GetConversationNameAsync(string conversationId, CancellationToken cancellationToken)
    {
        var client = GetClient();
        try
        {
            var info = await client.Conversations.Info(conversationId, cancellationToken: cancellationToken);
            if (info?.IsIm == true && !string.IsNullOrEmpty(info.User))
            {
                var user = await GetUserAsync(info.User, cancellationToken);
                return user?.DisplayName ?? user?.RealName ?? user?.Name ?? conversationId;
            }
            return info?.Name ?? conversationId;
        }
        catch
        {
            return conversationId;
        }
    }

    private async Task<SlackUser?> GetUserAsync(string userId, CancellationToken cancellationToken)
    {
        if (_userCache.TryGetValue(userId, out var cached))
            return cached;

        var client = GetClient();
        try
        {
            var response = await client.Users.Info(userId);
            if (response == null)
                return null;

            var user = new SlackUser
            {
                Id = response.Id,
                Name = response.Name ?? string.Empty,
                RealName = response.RealName ?? string.Empty,
                DisplayName = response.Profile?.DisplayName,
                Email = response.Profile?.Email,
                IsBot = response.IsBot,
                IsAdmin = response.IsAdmin,
                IsOwner = response.IsOwner,
                StatusText = response.Profile?.StatusText,
                StatusEmoji = response.Profile?.StatusEmoji,
                Timezone = response.Tz
            };

            _userCache[userId] = user;
            return user;
        }
        catch
        {
            return null;
        }
    }

    private async Task<List<SlackMessage>> MapMessagesAsync(
        IEnumerable<MessageEvent> messages,
        string channelId,
        string channelName,
        CancellationToken cancellationToken)
    {
        var result = new List<SlackMessage>();

        foreach (var m in messages)
        {
            var timestamp = ParseSlackTimestamp(m.Ts);
            var user = !string.IsNullOrEmpty(m.User) ? await GetUserAsync(m.User, cancellationToken) : null;

            result.Add(new SlackMessage
            {
                Ts = m.Ts,
                ChannelId = channelId,
                ChannelName = channelName,
                Text = m.Text ?? string.Empty,
                UserId = m.User ?? string.Empty,
                UserName = user?.DisplayName ?? user?.RealName ?? user?.Name ?? m.User ?? "Unknown",
                Timestamp = timestamp,
                IsUnread = _readStateTracker.IsChannelMessageUnread(channelId, timestamp),
                ThreadTs = m.ThreadTs,
                ReplyCount = m.ReplyCount,
                IsBot = m.BotId != null,
                Subtype = m.Subtype
            });
        }

        return result;
    }

    private string FormatMessages(
        List<SlackMessage> messages,
        string locationName,
        string locationId,
        bool isChannel,
        bool includeHeader = true)
    {
        var sb = new StringBuilder();

        if (includeHeader)
        {
            sb.AppendLine($"Found {messages.Count} message(s) in {locationName}:");
            sb.AppendLine();
        }

        foreach (var msg in messages.Where(m => string.IsNullOrEmpty(m.Subtype) || m.Subtype == "bot_message"))
        {
            var isUnread = isChannel
                ? _readStateTracker.IsChannelMessageUnread(locationId, msg.Timestamp)
                : _readStateTracker.IsConversationMessageUnread(locationId, msg.Timestamp);
            var unreadMarker = isUnread ? "🔵 " : "";
            var botMarker = msg.IsBot ? "🤖 " : "";

            sb.AppendLine($"{unreadMarker}{botMarker}**{msg.UserName}** ({msg.Timestamp:g}):");
            sb.AppendLine(msg.Text);
            if (msg.ReplyCount > 0)
                sb.AppendLine($"  [{msg.ReplyCount} replies]");
            sb.AppendLine($"[ts: {msg.Ts}]");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static DateTime ParseSlackTimestamp(string ts)
    {
        // Slack timestamps are Unix timestamps with microseconds: "1234567890.123456"
        if (string.IsNullOrEmpty(ts))
            return DateTime.MinValue;

        var parts = ts.Split('.');
        if (parts.Length > 0 && long.TryParse(parts[0], out var seconds))
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds).DateTime;
        }

        return DateTime.MinValue;
    }

    #endregion
}
