namespace Microbot.Memory.Interfaces;

using Microbot.Memory.Sessions;

/// <summary>
/// Main interface for the memory system.
/// </summary>
public interface IMemoryManager : IAsyncDisposable
{
    /// <summary>
    /// Searches memory for relevant content.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="options">Search options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Search results ordered by relevance.</returns>
    Task<IReadOnlyList<MemorySearchResult>> SearchAsync(
        string query,
        MemorySearchOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Synchronizes memory index with source files.
    /// </summary>
    /// <param name="options">Sync options.</param>
    /// <param name="progress">Progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SyncAsync(
        SyncOptions? options = null,
        IProgress<SyncProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Warms up the memory index for a session.
    /// </summary>
    /// <param name="sessionKey">Optional session key to prioritize.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task WarmSessionAsync(
        string? sessionKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current status of the memory index.
    /// </summary>
    MemoryStatus GetStatus();

    /// <summary>
    /// Saves a session transcript.
    /// </summary>
    /// <param name="session">The session to save.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveSessionAsync(
        SessionTranscript session,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a session transcript.
    /// </summary>
    /// <param name="sessionKey">The session key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The session transcript, or null if not found.</returns>
    Task<SessionTranscript?> LoadSessionAsync(
        string sessionKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all available sessions.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of session summaries.</returns>
    Task<IReadOnlyList<SessionSummary>> ListSessionsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the memory index.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a memory entry directly (for programmatic use).
    /// </summary>
    /// <param name="text">The text to add.</param>
    /// <param name="source">The source type.</param>
    /// <param name="path">Optional path identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddMemoryAsync(
        string text,
        MemorySource source,
        string? path = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Options for memory search.
/// </summary>
public class MemorySearchOptions
{
    /// <summary>
    /// Maximum number of results to return.
    /// </summary>
    public int MaxResults { get; set; } = 10;

    /// <summary>
    /// Minimum similarity score (0.0 to 1.0).
    /// </summary>
    public float MinScore { get; set; } = 0.5f;

    /// <summary>
    /// Optional session key to prioritize.
    /// </summary>
    public string? SessionKey { get; set; }

    /// <summary>
    /// Whether to include session transcripts.
    /// </summary>
    public bool IncludeSessions { get; set; } = true;

    /// <summary>
    /// Whether to include memory files.
    /// </summary>
    public bool IncludeMemoryFiles { get; set; } = true;

    /// <summary>
    /// Weight for vector search (0.0 to 1.0).
    /// </summary>
    public float VectorWeight { get; set; } = 0.7f;

    /// <summary>
    /// Weight for text search (0.0 to 1.0).
    /// </summary>
    public float TextWeight { get; set; } = 0.3f;
}

/// <summary>
/// Options for memory synchronization.
/// </summary>
public class SyncOptions
{
    /// <summary>
    /// Reason for the sync (for logging).
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Force full re-index even if files haven't changed.
    /// </summary>
    public bool Force { get; set; }

    /// <summary>
    /// Only sync specific sources.
    /// </summary>
    public MemorySource[]? Sources { get; set; }
}

/// <summary>
/// Progress information for sync operations.
/// </summary>
public class SyncProgress
{
    /// <summary>
    /// Current phase of the sync.
    /// </summary>
    public string Phase { get; set; } = string.Empty;

    /// <summary>
    /// Current file being processed.
    /// </summary>
    public string? CurrentFile { get; set; }

    /// <summary>
    /// Number of files processed.
    /// </summary>
    public int FilesProcessed { get; set; }

    /// <summary>
    /// Total number of files to process.
    /// </summary>
    public int TotalFiles { get; set; }

    /// <summary>
    /// Number of chunks created.
    /// </summary>
    public int ChunksCreated { get; set; }

    /// <summary>
    /// Number of embeddings generated.
    /// </summary>
    public int EmbeddingsGenerated { get; set; }

    /// <summary>
    /// Progress percentage (0-100).
    /// </summary>
    public int ProgressPercent => TotalFiles > 0 ? (FilesProcessed * 100) / TotalFiles : 0;
}

/// <summary>
/// Summary information about a session.
/// </summary>
public class SessionSummary
{
    /// <summary>
    /// Session key.
    /// </summary>
    public string SessionKey { get; set; } = string.Empty;

    /// <summary>
    /// Session title.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// When the session started.
    /// </summary>
    public DateTime StartedAt { get; set; }

    /// <summary>
    /// When the session ended.
    /// </summary>
    public DateTime? EndedAt { get; set; }

    /// <summary>
    /// Number of messages in the session.
    /// </summary>
    public int MessageCount { get; set; }

    /// <summary>
    /// Session summary text.
    /// </summary>
    public string? Summary { get; set; }
}
