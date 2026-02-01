namespace Microbot.Memory.Database.Entities;

/// <summary>
/// Represents an indexed file in the memory system.
/// </summary>
public class MemoryFile
{
    /// <summary>
    /// Primary key.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Path to the file (relative to memory folder).
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Source type: "memory" or "sessions".
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Content hash for change detection.
    /// </summary>
    public string Hash { get; set; } = string.Empty;

    /// <summary>
    /// Last modified time (Unix timestamp).
    /// </summary>
    public long ModifiedTime { get; set; }

    /// <summary>
    /// File size in bytes.
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// When the file was indexed.
    /// </summary>
    public DateTime IndexedAt { get; set; }

    /// <summary>
    /// Navigation property to chunks.
    /// </summary>
    public ICollection<MemoryChunk> Chunks { get; set; } = [];
}
