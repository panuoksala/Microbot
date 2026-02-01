namespace Microbot.Memory.Embeddings;

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microbot.Memory.Interfaces;

/// <summary>
/// Azure OpenAI embedding provider.
/// </summary>
public class AzureOpenAIEmbeddingProvider : IEmbeddingProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _deploymentName;
    private readonly int? _dimensions;
    private readonly ILogger<AzureOpenAIEmbeddingProvider>? _logger;

    /// <inheritdoc />
    public string ProviderName => "AzureOpenAI";

    /// <inheritdoc />
    public string ModelName => _deploymentName;

    /// <inheritdoc />
    public int Dimensions => _dimensions ?? 1536;

    /// <summary>
    /// Creates a new Azure OpenAI embedding provider.
    /// </summary>
    public AzureOpenAIEmbeddingProvider(
        string endpoint,
        string apiKey,
        string deploymentName,
        int? dimensions = null,
        string apiVersion = "2024-02-01",
        ILogger<AzureOpenAIEmbeddingProvider>? logger = null)
    {
        _deploymentName = deploymentName;
        _dimensions = dimensions;
        _logger = logger;

        // Ensure endpoint ends with /
        if (!endpoint.EndsWith('/'))
        {
            endpoint += "/";
        }

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(endpoint)
        };
        _httpClient.DefaultRequestHeaders.Add("api-key", apiKey);
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

        _logger?.LogDebug("Generating embeddings for {Count} texts using Azure OpenAI deployment {Deployment}",
            textList.Count, _deploymentName);

        var request = new EmbeddingRequest
        {
            Input = textList
        };

        if (_dimensions.HasValue)
        {
            request.Dimensions = _dimensions.Value;
        }

        var response = await _httpClient.PostAsJsonAsync(
            $"openai/deployments/{_deploymentName}/embeddings?api-version=2024-02-01",
            request,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(
            cancellationToken: cancellationToken);

        if (result?.Data == null)
        {
            throw new InvalidOperationException("Failed to get embeddings from Azure OpenAI API");
        }

        _logger?.LogDebug("Generated {Count} embeddings, usage: {Tokens} tokens",
            result.Data.Count, result.Usage?.TotalTokens);

        return result.Data
            .OrderBy(d => d.Index)
            .Select(d => d.Embedding)
            .ToList();
    }

    private class EmbeddingRequest
    {
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
