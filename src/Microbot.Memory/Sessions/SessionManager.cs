namespace Microbot.Memory.Sessions;

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microbot.Memory.Interfaces;

/// <summary>
/// Manages session transcript persistence.
/// </summary>
public class SessionManager
{
    private readonly string _sessionsFolder;
    private readonly ILogger<SessionManager>? _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Creates a new SessionManager.
    /// </summary>
    public SessionManager(string sessionsFolder, ILogger<SessionManager>? logger = null)
    {
        _sessionsFolder = sessionsFolder;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        // Ensure sessions folder exists
        if (!Directory.Exists(_sessionsFolder))
        {
            Directory.CreateDirectory(_sessionsFolder);
        }
    }

    /// <summary>
    /// Saves a session transcript.
    /// </summary>
    public async Task SaveSessionAsync(
        SessionTranscript session,
        CancellationToken cancellationToken = default)
    {
        var filePath = GetSessionFilePath(session.SessionKey);
        
        _logger?.LogDebug("Saving session {SessionKey} to {Path}", session.SessionKey, filePath);

        var json = JsonSerializer.Serialize(session, _jsonOptions);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);

        _logger?.LogDebug("Session {SessionKey} saved with {Count} entries",
            session.SessionKey, session.Entries.Count);
    }

    /// <summary>
    /// Loads a session transcript.
    /// </summary>
    public async Task<SessionTranscript?> LoadSessionAsync(
        string sessionKey,
        CancellationToken cancellationToken = default)
    {
        var filePath = GetSessionFilePath(sessionKey);

        if (!File.Exists(filePath))
        {
            _logger?.LogDebug("Session {SessionKey} not found at {Path}", sessionKey, filePath);
            return null;
        }

        _logger?.LogDebug("Loading session {SessionKey} from {Path}", sessionKey, filePath);

        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        return JsonSerializer.Deserialize<SessionTranscript>(json, _jsonOptions);
    }

    /// <summary>
    /// Lists all available sessions.
    /// </summary>
    public async Task<IReadOnlyList<SessionSummary>> ListSessionsAsync(
        CancellationToken cancellationToken = default)
    {
        var summaries = new List<SessionSummary>();

        if (!Directory.Exists(_sessionsFolder))
        {
            return summaries;
        }

        var files = Directory.GetFiles(_sessionsFolder, "*.json");
        
        foreach (var file in files)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file, cancellationToken);
                var session = JsonSerializer.Deserialize<SessionTranscript>(json, _jsonOptions);
                
                if (session != null)
                {
                    summaries.Add(new SessionSummary
                    {
                        SessionKey = session.SessionKey,
                        Title = session.Title,
                        StartedAt = session.StartedAt,
                        EndedAt = session.EndedAt,
                        MessageCount = session.Entries.Count,
                        Summary = session.Summary
                    });
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to load session from {Path}", file);
            }
        }

        return summaries.OrderByDescending(s => s.StartedAt).ToList();
    }

    /// <summary>
    /// Deletes a session.
    /// </summary>
    public Task DeleteSessionAsync(string sessionKey, CancellationToken cancellationToken = default)
    {
        var filePath = GetSessionFilePath(sessionKey);

        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            _logger?.LogDebug("Deleted session {SessionKey}", sessionKey);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Gets all session file paths for indexing.
    /// </summary>
    public IEnumerable<string> GetSessionFilePaths()
    {
        if (!Directory.Exists(_sessionsFolder))
        {
            return [];
        }

        return Directory.GetFiles(_sessionsFolder, "*.json");
    }

    /// <summary>
    /// Exports a session to a markdown file.
    /// </summary>
    public async Task ExportSessionAsync(
        string sessionKey,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var session = await LoadSessionAsync(sessionKey, cancellationToken);
        if (session == null)
        {
            throw new FileNotFoundException($"Session {sessionKey} not found");
        }

        var markdown = session.ToPlainText();
        await File.WriteAllTextAsync(outputPath, markdown, cancellationToken);

        _logger?.LogDebug("Exported session {SessionKey} to {Path}", sessionKey, outputPath);
    }

    /// <summary>
    /// Gets the file path for a session.
    /// </summary>
    private string GetSessionFilePath(string sessionKey)
    {
        // Sanitize session key for use as filename
        var safeKey = string.Join("_", sessionKey.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_sessionsFolder, $"{safeKey}.json");
    }
}
