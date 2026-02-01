namespace Microbot.Skills.Teams.Services;

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microbot.Skills.Teams.Models;

/// <summary>
/// Tracks read state for Teams channels and chats using local file storage.
/// Since Microsoft Graph doesn't provide built-in unread tracking for channel messages,
/// this service maintains local timestamps to track what the user has "read".
/// </summary>
public class ReadStateTracker
{
    private readonly string _stateFilePath;
    private readonly ILogger<ReadStateTracker>? _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private ReadState _state;
    private readonly object _lock = new();

    /// <summary>
    /// Creates a new ReadStateTracker instance.
    /// </summary>
    /// <param name="stateFilePath">Path to the state file. Defaults to ~/.microbot/teams-read-state.json</param>
    /// <param name="logger">Optional logger.</param>
    public ReadStateTracker(string? stateFilePath = null, ILogger<ReadStateTracker>? logger = null)
    {
        _stateFilePath = stateFilePath ?? GetDefaultStatePath();
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        _state = new ReadState();
    }

    /// <summary>
    /// Gets the default state file path (~/.microbot/teams-read-state.json).
    /// </summary>
    private static string GetDefaultStatePath()
    {
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var microbotDir = Path.Combine(homeDir, ".microbot");
        return Path.Combine(microbotDir, "teams-read-state.json");
    }

    /// <summary>
    /// Loads the read state from disk.
    /// </summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (!File.Exists(_stateFilePath))
            {
                _logger?.LogInformation("No existing read state file found at {Path}", _stateFilePath);
                _state = new ReadState();
                return;
            }
        }

        try
        {
            var json = await File.ReadAllTextAsync(_stateFilePath, cancellationToken);
            lock (_lock)
            {
                _state = JsonSerializer.Deserialize<ReadState>(json, _jsonOptions) ?? new ReadState();
            }
            _logger?.LogInformation("Loaded read state with {ChannelCount} channels and {ChatCount} chats",
                _state.ChannelLastRead.Count, _state.ChatLastRead.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load read state from {Path}, starting fresh", _stateFilePath);
            lock (_lock)
            {
                _state = new ReadState();
            }
        }
    }

    /// <summary>
    /// Saves the read state to disk.
    /// </summary>
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Ensure directory exists
            var directory = Path.GetDirectoryName(_stateFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json;
            lock (_lock)
            {
                _state.LastUpdated = DateTimeOffset.UtcNow;
                json = JsonSerializer.Serialize(_state, _jsonOptions);
            }

            await File.WriteAllTextAsync(_stateFilePath, json, cancellationToken);
            _logger?.LogDebug("Saved read state to {Path}", _stateFilePath);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to save read state to {Path}", _stateFilePath);
            throw;
        }
    }

    /// <summary>
    /// Gets the last read timestamp for a channel.
    /// </summary>
    /// <param name="teamId">The team ID.</param>
    /// <param name="channelId">The channel ID.</param>
    /// <returns>The last read timestamp, or null if never read.</returns>
    public DateTimeOffset? GetChannelLastRead(string teamId, string channelId)
    {
        var key = $"{teamId}:{channelId}";
        lock (_lock)
        {
            return _state.ChannelLastRead.TryGetValue(key, out var timestamp) ? timestamp : null;
        }
    }

    /// <summary>
    /// Marks a channel as read up to the specified timestamp.
    /// </summary>
    /// <param name="teamId">The team ID.</param>
    /// <param name="channelId">The channel ID.</param>
    /// <param name="timestamp">The timestamp to mark as read up to. Defaults to now.</param>
    public void MarkChannelAsRead(string teamId, string channelId, DateTimeOffset? timestamp = null)
    {
        var key = $"{teamId}:{channelId}";
        var ts = timestamp ?? DateTimeOffset.UtcNow;
        lock (_lock)
        {
            _state.ChannelLastRead[key] = ts;
        }
        _logger?.LogDebug("Marked channel {Key} as read up to {Timestamp}", key, ts);
    }

    /// <summary>
    /// Gets the last read timestamp for a chat.
    /// </summary>
    /// <param name="chatId">The chat ID.</param>
    /// <returns>The last read timestamp, or null if never read.</returns>
    public DateTimeOffset? GetChatLastRead(string chatId)
    {
        lock (_lock)
        {
            return _state.ChatLastRead.TryGetValue(chatId, out var timestamp) ? timestamp : null;
        }
    }

    /// <summary>
    /// Marks a chat as read up to the specified timestamp.
    /// </summary>
    /// <param name="chatId">The chat ID.</param>
    /// <param name="timestamp">The timestamp to mark as read up to. Defaults to now.</param>
    public void MarkChatAsRead(string chatId, DateTimeOffset? timestamp = null)
    {
        var ts = timestamp ?? DateTimeOffset.UtcNow;
        lock (_lock)
        {
            _state.ChatLastRead[chatId] = ts;
        }
        _logger?.LogDebug("Marked chat {ChatId} as read up to {Timestamp}", chatId, ts);
    }

    /// <summary>
    /// Checks if a channel message is unread based on its timestamp.
    /// </summary>
    /// <param name="teamId">The team ID.</param>
    /// <param name="channelId">The channel ID.</param>
    /// <param name="messageTimestamp">The message timestamp.</param>
    /// <returns>True if the message is unread.</returns>
    public bool IsChannelMessageUnread(string teamId, string channelId, DateTimeOffset? messageTimestamp)
    {
        if (messageTimestamp == null) return false;
        
        var lastRead = GetChannelLastRead(teamId, channelId);
        if (lastRead == null) return true; // Never read = all messages are unread
        
        return messageTimestamp > lastRead;
    }

    /// <summary>
    /// Checks if a chat message is unread based on its timestamp.
    /// </summary>
    /// <param name="chatId">The chat ID.</param>
    /// <param name="messageTimestamp">The message timestamp.</param>
    /// <returns>True if the message is unread.</returns>
    public bool IsChatMessageUnread(string chatId, DateTimeOffset? messageTimestamp)
    {
        if (messageTimestamp == null) return false;
        
        var lastRead = GetChatLastRead(chatId);
        if (lastRead == null) return true; // Never read = all messages are unread
        
        return messageTimestamp > lastRead;
    }

    /// <summary>
    /// Gets all tracked channel keys.
    /// </summary>
    public IEnumerable<string> GetTrackedChannelKeys()
    {
        lock (_lock)
        {
            return _state.ChannelLastRead.Keys.ToList();
        }
    }

    /// <summary>
    /// Gets all tracked chat IDs.
    /// </summary>
    public IEnumerable<string> GetTrackedChatIds()
    {
        lock (_lock)
        {
            return _state.ChatLastRead.Keys.ToList();
        }
    }

    /// <summary>
    /// Clears all read state.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _state = new ReadState();
        }
        _logger?.LogInformation("Cleared all read state");
    }
}
