namespace Microbot.Memory.Search;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microbot.Memory.Database;
using Microbot.Memory.Interfaces;

/// <summary>
/// Hybrid search combining vector similarity and full-text search.
/// </summary>
public class HybridSearch
{
    private readonly MemoryDbContext _dbContext;
    private readonly VectorSearch _vectorSearch;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly ILogger<HybridSearch>? _logger;

    /// <summary>
    /// Creates a new HybridSearch instance.
    /// </summary>
    public HybridSearch(
        MemoryDbContext dbContext,
        VectorSearch vectorSearch,
        IEmbeddingProvider embeddingProvider,
        ILogger<HybridSearch>? logger = null)
    {
        _dbContext = dbContext;
        _vectorSearch = vectorSearch;
        _embeddingProvider = embeddingProvider;
        _logger = logger;
    }

    /// <summary>
    /// Performs a hybrid search combining vector and text search.
    /// </summary>
    public async Task<IReadOnlyList<MemorySearchResult>> SearchAsync(
        string query,
        MemorySearchOptions options,
        CancellationToken cancellationToken = default)
    {
        _logger?.LogDebug("Performing hybrid search for: {Query}", query);

        // Get vector search results
        var vectorResults = new List<(int ChunkId, float Score)>();
        if (options.VectorWeight > 0)
        {
            var queryEmbedding = await _embeddingProvider.GenerateEmbeddingAsync(query, cancellationToken);
            vectorResults = (await _vectorSearch.SearchAsync(
                queryEmbedding,
                options.MaxResults * 2, // Get more results for merging
                0.0f, // We'll filter by score later
                cancellationToken)).ToList();
        }

        // Get full-text search results
        var textResults = new List<(int ChunkId, double Score)>();
        if (options.TextWeight > 0)
        {
            textResults = await _dbContext.FullTextSearchAsync(
                query,
                options.MaxResults * 2,
                cancellationToken);
        }

        // Normalize and combine scores
        var combinedScores = CombineScores(
            vectorResults,
            textResults,
            options.VectorWeight,
            options.TextWeight);

        // Filter by minimum score and take top results
        var topChunkIds = combinedScores
            .Where(s => s.Score >= options.MinScore)
            .OrderByDescending(s => s.Score)
            .Take(options.MaxResults)
            .Select(s => s.ChunkId)
            .ToList();

        if (topChunkIds.Count == 0)
        {
            _logger?.LogDebug("No results found above minimum score {MinScore}", options.MinScore);
            return [];
        }

        // Load chunk details
        var chunks = await _dbContext.Chunks
            .Where(c => topChunkIds.Contains(c.Id))
            .ToListAsync(cancellationToken);

        // Build results with scores
        var scoreMap = combinedScores.ToDictionary(s => s.ChunkId, s => s.Score);
        var results = chunks
            .Where(c => scoreMap.ContainsKey(c.Id))
            .Select(c => new MemorySearchResult
            {
                ChunkId = c.Id,
                Path = c.Path,
                StartLine = c.StartLine,
                EndLine = c.EndLine,
                Score = scoreMap[c.Id],
                Snippet = TruncateSnippet(c.Text, 500),
                Source = c.Source == "sessions" ? MemorySource.Sessions : MemorySource.Memory,
                IndexedAt = c.UpdatedAt
            })
            .OrderByDescending(r => r.Score)
            .ToList();

        // Apply source filters
        if (!options.IncludeSessions)
        {
            results = results.Where(r => r.Source != MemorySource.Sessions).ToList();
        }
        if (!options.IncludeMemoryFiles)
        {
            results = results.Where(r => r.Source != MemorySource.Memory).ToList();
        }

        _logger?.LogDebug("Hybrid search returned {Count} results", results.Count);

        return results;
    }

    /// <summary>
    /// Combines vector and text search scores using weighted average.
    /// </summary>
    private static List<(int ChunkId, float Score)> CombineScores(
        List<(int ChunkId, float Score)> vectorResults,
        List<(int ChunkId, double Score)> textResults,
        float vectorWeight,
        float textWeight)
    {
        var allChunkIds = vectorResults.Select(v => v.ChunkId)
            .Union(textResults.Select(t => t.ChunkId))
            .ToHashSet();

        // Normalize vector scores (already 0-1 for cosine similarity)
        var vectorScores = vectorResults.ToDictionary(v => v.ChunkId, v => v.Score);

        // Normalize text scores (BM25 scores need normalization)
        var maxTextScore = textResults.Count > 0 ? textResults.Max(t => t.Score) : 1.0;
        var textScores = textResults.ToDictionary(
            t => t.ChunkId,
            t => maxTextScore > 0 ? (float)(t.Score / maxTextScore) : 0f);

        var combined = new List<(int ChunkId, float Score)>();
        foreach (var chunkId in allChunkIds)
        {
            var vectorScore = vectorScores.GetValueOrDefault(chunkId, 0f);
            var textScore = textScores.GetValueOrDefault(chunkId, 0f);

            // Weighted combination
            var combinedScore = (vectorScore * vectorWeight) + (textScore * textWeight);
            combined.Add((chunkId, combinedScore));
        }

        return combined;
    }

    /// <summary>
    /// Truncates a snippet to the specified length.
    /// </summary>
    private static string TruncateSnippet(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
        {
            return text;
        }

        // Try to truncate at a word boundary
        var truncated = text[..maxLength];
        var lastSpace = truncated.LastIndexOf(' ');
        if (lastSpace > maxLength * 0.8)
        {
            truncated = truncated[..lastSpace];
        }

        return truncated + "...";
    }
}
