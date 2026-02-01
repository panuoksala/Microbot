namespace Microbot.Memory.Search;

using Microsoft.Extensions.Logging;
using Microbot.Memory.Database;
using Microbot.Memory.Database.Entities;

/// <summary>
/// In-memory vector search using cosine similarity.
/// </summary>
public class VectorSearch
{
    private readonly MemoryDbContext _dbContext;
    private readonly ILogger<VectorSearch>? _logger;
    private List<(int ChunkId, float[] Embedding)>? _vectorIndex;
    private bool _indexLoaded;

    /// <summary>
    /// Creates a new VectorSearch instance.
    /// </summary>
    public VectorSearch(MemoryDbContext dbContext, ILogger<VectorSearch>? logger = null)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Loads the vector index into memory.
    /// </summary>
    public async Task LoadIndexAsync(CancellationToken cancellationToken = default)
    {
        _logger?.LogDebug("Loading vector index into memory...");

        var chunks = _dbContext.Chunks
            .Where(c => c.Embedding != null)
            .Select(c => new { c.Id, c.Embedding })
            .ToList();

        _vectorIndex = chunks
            .Where(c => c.Embedding != null)
            .Select(c => (c.Id, GetEmbeddingVector(c.Embedding!)))
            .Where(c => c.Item2 != null)
            .Select(c => (c.Id, c.Item2!))
            .ToList();

        _indexLoaded = true;

        _logger?.LogDebug("Loaded {Count} vectors into memory", _vectorIndex.Count);
    }

    /// <summary>
    /// Searches for similar vectors.
    /// </summary>
    public async Task<IReadOnlyList<(int ChunkId, float Score)>> SearchAsync(
        float[] queryEmbedding,
        int maxResults = 10,
        float minScore = 0.0f,
        CancellationToken cancellationToken = default)
    {
        if (!_indexLoaded || _vectorIndex == null)
        {
            await LoadIndexAsync(cancellationToken);
        }

        if (_vectorIndex == null || _vectorIndex.Count == 0)
        {
            return [];
        }

        _logger?.LogDebug("Searching {Count} vectors for similar content", _vectorIndex.Count);

        var results = _vectorIndex
            .Select(v => (v.ChunkId, Score: CosineSimilarity(queryEmbedding, v.Embedding)))
            .Where(r => r.Score >= minScore)
            .OrderByDescending(r => r.Score)
            .Take(maxResults)
            .ToList();

        _logger?.LogDebug("Found {Count} results above minimum score {MinScore}", results.Count, minScore);

        return results;
    }

    /// <summary>
    /// Adds a vector to the index.
    /// </summary>
    public void AddToIndex(int chunkId, float[] embedding)
    {
        if (_vectorIndex == null)
        {
            _vectorIndex = [];
        }

        // Remove existing entry if present
        _vectorIndex.RemoveAll(v => v.ChunkId == chunkId);
        _vectorIndex.Add((chunkId, embedding));
    }

    /// <summary>
    /// Removes a vector from the index.
    /// </summary>
    public void RemoveFromIndex(int chunkId)
    {
        _vectorIndex?.RemoveAll(v => v.ChunkId == chunkId);
    }

    /// <summary>
    /// Clears the vector index.
    /// </summary>
    public void ClearIndex()
    {
        _vectorIndex?.Clear();
        _indexLoaded = false;
    }

    /// <summary>
    /// Computes cosine similarity between two vectors.
    /// </summary>
    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
        {
            throw new ArgumentException("Vectors must have the same length");
        }

        float dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denominator = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        if (denominator == 0)
        {
            return 0;
        }

        return dot / denominator;
    }

    /// <summary>
    /// Converts a byte array to a float array.
    /// </summary>
    private static float[]? GetEmbeddingVector(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return null;
        }

        var floatCount = bytes.Length / sizeof(float);
        var result = new float[floatCount];
        Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
        return result;
    }
}
