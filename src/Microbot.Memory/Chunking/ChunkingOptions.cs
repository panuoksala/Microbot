namespace Microbot.Memory.Chunking;

/// <summary>
/// Options for text chunking.
/// </summary>
public class ChunkingOptions
{
    /// <summary>
    /// Maximum tokens per chunk.
    /// </summary>
    public int MaxTokens { get; set; } = 512;

    /// <summary>
    /// Overlap tokens between chunks.
    /// </summary>
    public int OverlapTokens { get; set; } = 50;

    /// <summary>
    /// Whether to use markdown-aware chunking.
    /// </summary>
    public bool MarkdownAware { get; set; } = true;

    /// <summary>
    /// Minimum chunk size in tokens (chunks smaller than this will be merged).
    /// </summary>
    public int MinTokens { get; set; } = 50;
}
