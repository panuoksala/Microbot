namespace Microbot.Memory;

/// <summary>
/// Represents the current status of the memory index.
/// </summary>
public class MemoryStatus
{
    /// <summary>
    /// Total number of indexed files.
    /// </summary>
    public int TotalFiles { get; set; }

    /// <summary>
    /// Total number of text chunks.
    /// </summary>
    public int TotalChunks { get; set; }

    /// <summary>
    /// Whether there are pending changes to sync.
    /// </summary>
    public bool IsDirty { get; set; }

    /// <summary>
    /// When the last sync occurred.
    /// </summary>
    public DateTime? LastSyncAt { get; set; }

    /// <summary>
    /// The embedding model being used.
    /// </summary>
    public string? EmbeddingModel { get; set; }

    /// <summary>
    /// The embedding provider being used.
    /// </summary>
    public string? EmbeddingProvider { get; set; }

    /// <summary>
    /// Number of cached embeddings.
    /// </summary>
    public int CachedEmbeddings { get; set; }

    /// <summary>
    /// Database file size in bytes.
    /// </summary>
    public long DatabaseSizeBytes { get; set; }

    /// <summary>
    /// Number of session files indexed.
    /// </summary>
    public int SessionFiles { get; set; }

    /// <summary>
    /// Number of memory files indexed.
    /// </summary>
    public int MemoryFiles { get; set; }
}
