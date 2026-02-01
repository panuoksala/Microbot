namespace Microbot.Skills.Slack.Services;

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microbot.Skills.Slack.Models;

/// <summary>
/// Tracks and persists the last read timestamps for Slack channels and conversations.
/// </summary>
public class SlackReadStateTracker
{
    private readonly string _statePath;
    private SlackReadState _state;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ILogger<SlackReadStateTracker>? _logger;

    /// <summary>
    /// Creates a new SlackReadStateTracker instance.
    /// </summary>
    /// <param name="statePath">Path to the read state file.</param>
    /// <param name="logger">Optional logger.</param>
    public SlackReadStateTracker(
        string statePath = "./slack-read-state.json",
        ILogger<SlackReadStateTracker>? logger = null)
    {
        _statePath = statePath;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        _state = new SlackReadState();
    }

    /// <summary>
    /// Loads the read state from disk.
    /// </summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(_statePath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(_statePath, cancellationToken);
                _state = JsonSerializer.Deserialize<SlackReadState>(json, _jsonOptions) ?? new SlackReadState();
                _logger?.LogInformation("Loaded Slack read state from {Path}", _statePath);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to load Slack read state, starting fresh");
                _state = new SlackReadState();
            }
        }
    }

    /// <summary>
    /// Saves the read state to disk.
    /// </summary>
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        _state.LastUpdated = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(_state, _jsonOptions);
        await File.WriteAllTextAsync(_statePath, json, cancellationToken);
        _logger?.LogDebug("Saved Slack read state to {Path}", _statePath);
    }

    /// <summary>
    /// Gets the last read timestamp for a channel.
    /// </summary>
    /// <param name="channelId">The channel ID.</param>
    /// <returns>The last read timestamp, or null if never read.</returns>
    public DateTime? GetChannelLastRead(string channelId)
    {
        return _state.ChannelLastRead.TryGetValue(channelId, out var timestamp) ? timestamp : null;
    }

    /// <summary>
    /// Marks a channel as read up to the specified timestamp.
    /// </summary>
    /// <param name="channelId">The channel ID.</param>
    /// <param name="timestamp">The timestamp to mark as read up to (defaults to now).</param>
    public void MarkChannelAsRead(string channelId, DateTime? timestamp = null)
    {
        _state.ChannelLastRead[channelId] = timestamp ?? DateTime.UtcNow;
    }

    /// <summary>
    /// Checks if a channel message is unread based on its timestamp.
    /// </summary>
    /// <param name="channelId">The channel ID.</param>
    /// <param name="messageTimestamp">The message timestamp.</param>
    /// <returns>True if the message is unread.</returns>
    public bool IsChannelMessageUnread(string channelId, DateTime messageTimestamp)
    {
        var lastRead = GetChannelLastRead(channelId);
        return lastRead == null || messageTimestamp > lastRead;
    }

    /// <summary>
    /// Gets the last read timestamp for a conversation.
    /// </summary>
    /// <param name="conversationId">The conversation ID.</param>
    /// <returns>The last read timestamp, or null if never read.</returns>
    public DateTime? GetConversationLastRead(string conversationId)
    {
        return _state.ConversationLastRead.TryGetValue(conversationId, out var timestamp) ? timestamp : null;
    }

    /// <summary>
    /// Marks a conversation as read up to the specified timestamp.
    /// </summary>
    /// <param name="conversationId">The conversation ID.</param>
    /// <param name="timestamp">The timestamp to mark as read up to (defaults to now).</param>
    public void MarkConversationAsRead(string conversationId, DateTime? timestamp = null)
    {
        _state.ConversationLastRead[conversationId] = timestamp ?? DateTime.UtcNow;
    }

    /// <summary>
    /// Checks if a conversation message is unread based on its timestamp.
    /// </summary>
    /// <param name="conversationId">The conversation ID.</param>
    /// <param name="messageTimestamp">The message timestamp.</param>
    /// <returns>True if the message is unread.</returns>
    public bool IsConversationMessageUnread(string conversationId, DateTime messageTimestamp)
    {
        var lastRead = GetConversationLastRead(conversationId);
        return lastRead == null || messageTimestamp > lastRead;
    }
}
