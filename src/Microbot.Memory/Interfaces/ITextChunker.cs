namespace Microbot.Memory.Interfaces;

/// <summary>
/// Interface for text chunking implementations.
/// </summary>
public interface ITextChunker
{
    /// <summary>
    /// Splits text into chunks suitable for embedding.
    /// </summary>
    /// <param name="text">The text to chunk.</param>
    /// <param name="filePath">The source file path (for context).</param>
    /// <returns>A list of text chunks with line information.</returns>
    IReadOnlyList<TextChunk> ChunkText(string text, string filePath);
}

/// <summary>
/// Represents a chunk of text with its location information.
/// </summary>
public class TextChunk
{
    /// <summary>
    /// The text content of the chunk.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Starting line number (1-based).
    /// </summary>
    public int StartLine { get; set; }

    /// <summary>
    /// Ending line number (1-based).
    /// </summary>
    public int EndLine { get; set; }

    /// <summary>
    /// Hash of the chunk content.
    /// </summary>
    public string Hash { get; set; } = string.Empty;

    /// <summary>
    /// Estimated token count.
    /// </summary>
    public int TokenCount { get; set; }
}
