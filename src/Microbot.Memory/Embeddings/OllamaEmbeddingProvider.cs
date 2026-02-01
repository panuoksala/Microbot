namespace Microbot.Memory.Embeddings;

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microbot.Memory.Interfaces;

/// <summary>
/// Ollama embedding provider for local embeddings.
/// </summary>
public class OllamaEmbeddingProvider : IEmbeddingProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _modelId;
    private readonly ILogger<OllamaEmbeddingProvider>? _logger;
    private int? _cachedDimensions;

    /// <inheritdoc />
    public string ProviderName => "Ollama";

    /// <inheritdoc />
    public string ModelName => _modelId;

    /// <inheritdoc />
    public int Dimensions => _cachedDimensions ?? GetDefaultDimensions(_modelId);

    /// <summary>
    /// Creates a new Ollama embedding provider.
    /// </summary>
    public OllamaEmbeddingProvider(
        string modelId = "nomic-embed-text",
        string? endpoint = null,
        ILogger<OllamaEmbeddingProvider>? logger = null)
    {
        _modelId = modelId;
        _logger = logger;

        var baseUrl = endpoint ?? "http://localhost:11434";
        if (!baseUrl.EndsWith('/'))
        {
            baseUrl += "/";
        }

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl)
        };
    }

    /// <inheritdoc />
    public async Task<float[]> GenerateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        _logger?.LogDebug("Generating embedding for text using Ollama model {Model}", _modelId);

        var request = new OllamaEmbeddingRequest
        {
            Model = _modelId,
            Prompt = text
        };

        var response = await _httpClient.PostAsJsonAsync(
            "api/embeddings",
            request,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaEmbeddingResponse>(
            cancellationToken: cancellationToken);

        if (result?.Embedding == null || result.Embedding.Length == 0)
        {
            throw new InvalidOperationException("Failed to get embedding from Ollama API");
        }

        // Cache the dimensions
        _cachedDimensions = result.Embedding.Length;

        _logger?.LogDebug("Generated embedding with {Dimensions} dimensions", result.Embedding.Length);

        return result.Embedding;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
        IEnumerable<string> texts,
        CancellationToken cancellationToken = default)
    {
        var textList = texts.ToList();
        if (textList.Count == 0)
        {
            return [];
        }

        _logger?.LogDebug("Generating embeddings for {Count} texts using Ollama model {Model}",
            textList.Count, _modelId);

        // Ollama doesn't support batch embeddings, so we need to call one at a time
        var results = new List<float[]>();
        foreach (var text in textList)
        {
            var embedding = await GenerateEmbeddingAsync(text, cancellationToken);
            results.Add(embedding);
        }

        return results;
    }

    /// <summary>
    /// Gets the default dimensions for a model.
    /// </summary>
    private static int GetDefaultDimensions(string modelId)
    {
        return modelId switch
        {
            "nomic-embed-text" => 768,
            "mxbai-embed-large" => 1024,
            "all-minilm" => 384,
            "snowflake-arctic-embed" => 1024,
            _ => 768
        };
    }

    private class OllamaEmbeddingRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("prompt")]
        public string Prompt { get; set; } = string.Empty;
    }

    private class OllamaEmbeddingResponse
    {
        [JsonPropertyName("embedding")]
        public float[] Embedding { get; set; } = [];
    }
}
