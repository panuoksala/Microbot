namespace Microbot.Memory.Interfaces;

/// <summary>
/// Interface for embedding generation providers.
/// </summary>
public interface IEmbeddingProvider
{
    /// <summary>
    /// Gets the provider name.
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Gets the model name.
    /// </summary>
    string ModelName { get; }

    /// <summary>
    /// Gets the embedding dimensions.
    /// </summary>
    int Dimensions { get; }

    /// <summary>
    /// Generates an embedding for the given text.
    /// </summary>
    /// <param name="text">The text to embed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The embedding vector.</returns>
    Task<float[]> GenerateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates embeddings for multiple texts.
    /// </summary>
    /// <param name="texts">The texts to embed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The embedding vectors.</returns>
    Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
        IEnumerable<string> texts,
        CancellationToken cancellationToken = default);
}
