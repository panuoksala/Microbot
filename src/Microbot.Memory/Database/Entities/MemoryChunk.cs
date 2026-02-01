namespace Microbot.Memory.Database.Entities;

/// <summary>
/// Represents a text chunk with its embedding in the memory system.
/// </summary>
public class MemoryChunk
{
    /// <summary>
    /// Primary key.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Foreign key to the source file.
    /// </summary>
    public int FileId { get; set; }

    /// <summary>
    /// Path to the source file.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Source type: "memory" or "sessions".
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Starting line number in the source file.
    /// </summary>
    public int StartLine { get; set; }

    /// <summary>
    /// Ending line number in the source file.
    /// </summary>
    public int EndLine { get; set; }

    /// <summary>
    /// Content hash for deduplication.
    /// </summary>
    public string Hash { get; set; } = string.Empty;

    /// <summary>
    /// Embedding model used.
    /// </summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// The text content of the chunk.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// The embedding vector (serialized as byte array).
    /// </summary>
    public byte[]? Embedding { get; set; }

    /// <summary>
    /// When the chunk was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Navigation property to the source file.
    /// </summary>
    public MemoryFile? File { get; set; }

    /// <summary>
    /// Gets the embedding as a float array.
    /// </summary>
    public float[]? GetEmbeddingVector()
    {
        if (Embedding == null || Embedding.Length == 0)
            return null;

        var floatCount = Embedding.Length / sizeof(float);
        var result = new float[floatCount];
        Buffer.BlockCopy(Embedding, 0, result, 0, Embedding.Length);
        return result;
    }

    /// <summary>
    /// Sets the embedding from a float array.
    /// </summary>
    public void SetEmbeddingVector(float[] vector)
    {
        Embedding = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, Embedding, 0, Embedding.Length);
    }
}
