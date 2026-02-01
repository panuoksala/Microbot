namespace Microbot.Memory.Embeddings;

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microbot.Memory.Interfaces;

/// <summary>
/// OpenAI embedding provider using the OpenAI API.
/// </summary>
public class OpenAIEmbeddingProvider : IEmbeddingProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _modelId;
    private readonly int? _dimensions;
    private readonly ILogger<OpenAIEmbeddingProvider>? _logger;

    /// <inheritdoc />
    public string ProviderName => "OpenAI";

    /// <inheritdoc />
    public string ModelName => _modelId;

    /// <inheritdoc />
    public int Dimensions => _dimensions ?? GetDefaultDimensions(_modelId);

    /// <summary>
    /// Creates a new OpenAI embedding provider.
    /// </summary>
    public OpenAIEmbeddingProvider(
        string apiKey,
        string modelId = "text-embedding-3-small",
        int? dimensions = null,
        string? endpoint = null,
        ILogger<OpenAIEmbeddingProvider>? logger = null)
    {
        _modelId = modelId;
        _dimensions = dimensions;
        _logger = logger;

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(endpoint ?? "https://api.openai.com/v1/")
        };
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
    }

    /// <inheritdoc />
    public async Task<float[]> GenerateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        var results = await GenerateEmbeddingsAsync([text], cancellationToken);
        return results[0];
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

        _logger?.LogDebug("Generating embeddings for {Count} texts using {Model}", textList.Count, _modelId);

        var request = new EmbeddingRequest
        {
            Model = _modelId,
            Input = textList
        };

        if (_dimensions.HasValue)
        {
            request.Dimensions = _dimensions.Value;
        }

        var response = await _httpClient.PostAsJsonAsync(
            "embeddings",
            request,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(
            cancellationToken: cancellationToken);

        if (result?.Data == null)
        {
            throw new InvalidOperationException("Failed to get embeddings from OpenAI API");
        }

        _logger?.LogDebug("Generated {Count} embeddings, usage: {Tokens} tokens",
            result.Data.Count, result.Usage?.TotalTokens);

        return result.Data
            .OrderBy(d => d.Index)
            .Select(d => d.Embedding)
            .ToList();
    }

    /// <summary>
    /// Gets the default dimensions for a model.
    /// </summary>
    private static int GetDefaultDimensions(string modelId)
    {
        return modelId switch
        {
            "text-embedding-3-small" => 1536,
            "text-embedding-3-large" => 3072,
            "text-embedding-ada-002" => 1536,
            _ => 1536
        };
    }

    private class EmbeddingRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("input")]
        public List<string> Input { get; set; } = [];

        [JsonPropertyName("dimensions")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Dimensions { get; set; }
    }

    private class EmbeddingResponse
    {
        [JsonPropertyName("data")]
        public List<EmbeddingData> Data { get; set; } = [];

        [JsonPropertyName("usage")]
        public EmbeddingUsage? Usage { get; set; }
    }

    private class EmbeddingData
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("embedding")]
        public float[] Embedding { get; set; } = [];
    }

    private class EmbeddingUsage
    {
        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; set; }

        [JsonPropertyName("total_tokens")]
        public int TotalTokens { get; set; }
    }
}
