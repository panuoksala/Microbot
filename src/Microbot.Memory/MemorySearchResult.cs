namespace Microbot.Memory;

/// <summary>
/// Represents a result from memory search.
/// </summary>
public class MemorySearchResult
{
    /// <summary>
    /// Path to the source file.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Starting line number in the source file.
    /// </summary>
    public int StartLine { get; set; }

    /// <summary>
    /// Ending line number in the source file.
    /// </summary>
    public int EndLine { get; set; }

    /// <summary>
    /// Relevance score (0.0 to 1.0).
    /// </summary>
    public float Score { get; set; }

    /// <summary>
    /// Text snippet from the matched chunk.
    /// </summary>
    public string Snippet { get; set; } = string.Empty;

    /// <summary>
    /// Source type of the memory.
    /// </summary>
    public MemorySource Source { get; set; }

    /// <summary>
    /// When this chunk was indexed.
    /// </summary>
    public DateTime IndexedAt { get; set; }

    /// <summary>
    /// The chunk ID in the database.
    /// </summary>
    public int ChunkId { get; set; }
}
