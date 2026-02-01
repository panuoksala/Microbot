namespace Microbot.Memory;

using Microsoft.Extensions.Logging;
using Microbot.Core.Models;
using Microbot.Memory.Chunking;
using Microbot.Memory.Embeddings;
using Microbot.Memory.Interfaces;

/// <summary>
/// Factory for creating MemoryManager instances.
/// </summary>
public static class MemoryManagerFactory
{
    /// <summary>
    /// Creates a MemoryManager with OpenAI embeddings.
    /// </summary>
    public static MemoryManager CreateWithOpenAI(
        string dataFolder,
        string apiKey,
        string model = "text-embedding-3-small",
        int? dimensions = null,
        ChunkingOptions? chunkingOptions = null,
        ILogger<MemoryManager>? logger = null)
    {
        var embeddingProvider = new OpenAIEmbeddingProvider(apiKey, model, dimensions);
        return new MemoryManager(dataFolder, embeddingProvider, chunkingOptions, logger);
    }

    /// <summary>
    /// Creates a MemoryManager with Azure OpenAI embeddings.
    /// </summary>
    public static MemoryManager CreateWithAzureOpenAI(
        string dataFolder,
        string endpoint,
        string apiKey,
        string deploymentName,
        int? dimensions = null,
        ChunkingOptions? chunkingOptions = null,
        ILogger<MemoryManager>? logger = null)
    {
        var embeddingProvider = new AzureOpenAIEmbeddingProvider(endpoint, apiKey, deploymentName, dimensions);
        return new MemoryManager(dataFolder, embeddingProvider, chunkingOptions, logger);
    }

    /// <summary>
    /// Creates a MemoryManager with Ollama embeddings.
    /// </summary>
    public static MemoryManager CreateWithOllama(
        string dataFolder,
        string model = "nomic-embed-text",
        string endpoint = "http://localhost:11434",
        ChunkingOptions? chunkingOptions = null,
        ILogger<MemoryManager>? logger = null)
    {
        var embeddingProvider = new OllamaEmbeddingProvider(model, endpoint);
        return new MemoryManager(dataFolder, embeddingProvider, chunkingOptions, logger);
    }

    /// <summary>
    /// Creates a MemoryManager with a custom embedding provider.
    /// </summary>
    public static MemoryManager CreateWithProvider(
        string dataFolder,
        IEmbeddingProvider embeddingProvider,
        ChunkingOptions? chunkingOptions = null,
        ILogger<MemoryManager>? logger = null)
    {
        return new MemoryManager(dataFolder, embeddingProvider, chunkingOptions, logger);
    }

    /// <summary>
    /// Creates a MemoryManager using Microbot configuration.
    /// </summary>
    public static MemoryManager CreateFromConfig(
        string dataFolder,
        EmbeddingConfig embeddingConfig,
        AiProviderConfig aiProviderConfig,
        ChunkingOptions? chunkingOptions = null,
        ILoggerFactory? loggerFactory = null)
    {
        var embeddingProvider = EmbeddingProviderFactory.Create(embeddingConfig, aiProviderConfig, loggerFactory);
        return new MemoryManager(
            dataFolder, 
            embeddingProvider, 
            chunkingOptions, 
            loggerFactory?.CreateLogger<MemoryManager>());
    }

    /// <summary>
    /// Creates a MemoryManager using the full Microbot configuration.
    /// </summary>
    public static MemoryManager CreateFromMicrobotConfig(
        MicrobotConfig config,
        ILoggerFactory? loggerFactory = null)
    {
        var memoryConfig = config.Memory ?? new MemoryConfig();
        
        // Use MemoryFolder as the data folder (it contains memory.db, memory/, sessions/)
        var dataFolder = memoryConfig.MemoryFolder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microbot",
            "memory");

        var chunkingOptions = memoryConfig.Chunking != null
            ? new ChunkingOptions
            {
                MaxTokens = memoryConfig.Chunking.MaxTokens,
                OverlapTokens = memoryConfig.Chunking.OverlapTokens,
                MarkdownAware = memoryConfig.Chunking.MarkdownAware
            }
            : null;

        var embeddingConfig = memoryConfig.Embedding ?? new EmbeddingConfig();
        var aiProviderConfig = config.AiProvider ?? new AiProviderConfig();

        return CreateFromConfig(dataFolder, embeddingConfig, aiProviderConfig, chunkingOptions, loggerFactory);
    }
}
