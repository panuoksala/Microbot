namespace Microbot.Memory.Embeddings;

using Microsoft.Extensions.Logging;
using Microbot.Core.Models;
using Microbot.Memory.Interfaces;

/// <summary>
/// Factory for creating embedding providers based on configuration.
/// </summary>
public static class EmbeddingProviderFactory
{
    /// <summary>
    /// Creates an embedding provider based on the configuration.
    /// </summary>
    public static IEmbeddingProvider Create(
        EmbeddingConfig embeddingConfig,
        AiProviderConfig aiProviderConfig,
        ILoggerFactory? loggerFactory = null)
    {
        // Determine which provider to use
        var provider = embeddingConfig.Provider ?? aiProviderConfig.Provider;
        var apiKey = embeddingConfig.ApiKey ?? aiProviderConfig.ApiKey;
        var endpoint = embeddingConfig.Endpoint ?? aiProviderConfig.Endpoint;

        return provider.ToLowerInvariant() switch
        {
            "openai" => new OpenAIEmbeddingProvider(
                apiKey ?? throw new InvalidOperationException("OpenAI API key is required for embeddings"),
                embeddingConfig.ModelId,
                embeddingConfig.Dimensions,
                endpoint,
                loggerFactory?.CreateLogger<OpenAIEmbeddingProvider>()),

            "azure" or "azureopenai" => new AzureOpenAIEmbeddingProvider(
                endpoint ?? throw new InvalidOperationException("Azure OpenAI endpoint is required for embeddings"),
                apiKey ?? throw new InvalidOperationException("Azure OpenAI API key is required for embeddings"),
                embeddingConfig.ModelId,
                embeddingConfig.Dimensions,
                logger: loggerFactory?.CreateLogger<AzureOpenAIEmbeddingProvider>()),

            "ollama" => new OllamaEmbeddingProvider(
                embeddingConfig.ModelId,
                endpoint ?? "http://localhost:11434",
                loggerFactory?.CreateLogger<OllamaEmbeddingProvider>()),

            _ => throw new InvalidOperationException($"Unsupported embedding provider: {provider}")
        };
    }
}
