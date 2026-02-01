namespace Microbot.Memory.Database.Entities;

/// <summary>
/// Caches embeddings to avoid redundant API calls.
/// </summary>
public class EmbeddingCache
{
    /// <summary>
    /// Primary key.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Embedding provider name.
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// Embedding model name.
    /// </summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// Hash of the text that was embedded.
    /// </summary>
    public string TextHash { get; set; } = string.Empty;

    /// <summary>
    /// The embedding vector (serialized as byte array).
    /// </summary>
    public byte[] Embedding { get; set; } = [];

    /// <summary>
    /// When the cache entry was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Gets the embedding as a float array.
    /// </summary>
    public float[] GetEmbeddingVector()
    {
        if (Embedding.Length == 0)
            return [];

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
