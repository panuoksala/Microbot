namespace Microbot.Memory.Chunking;

using System.IO.Hashing;
using System.Text;
using Markdig;
using Markdig.Syntax;
using Microsoft.ML.Tokenizers;
using Microbot.Memory.Interfaces;

/// <summary>
/// Markdown-aware text chunker that respects document structure.
/// </summary>
public class MarkdownChunker : ITextChunker
{
    private readonly ChunkingOptions _options;
    private readonly Tokenizer _tokenizer;

    /// <summary>
    /// Creates a new MarkdownChunker with the specified options.
    /// </summary>
    public MarkdownChunker(ChunkingOptions? options = null)
    {
        _options = options ?? new ChunkingOptions();
        // Use cl100k_base tokenizer (GPT-4/GPT-3.5 tokenizer)
        _tokenizer = TiktokenTokenizer.CreateForModel("gpt-4");
    }

    /// <inheritdoc />
    public IReadOnlyList<TextChunk> ChunkText(string text, string filePath)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var chunks = new List<TextChunk>();
        var lines = text.Split('\n');

        if (_options.MarkdownAware && IsMarkdown(filePath))
        {
            chunks.AddRange(ChunkMarkdown(text, lines));
        }
        else
        {
            chunks.AddRange(ChunkPlainText(text, lines));
        }

        // Merge small chunks
        chunks = MergeSmallChunks(chunks);

        return chunks;
    }

    /// <summary>
    /// Chunks markdown text respecting document structure.
    /// </summary>
    private IEnumerable<TextChunk> ChunkMarkdown(string text, string[] lines)
    {
        var pipeline = new MarkdownPipelineBuilder().Build();
        var document = Markdown.Parse(text, pipeline);

        var sections = new List<(int StartLine, int EndLine, string Content)>();
        var currentSection = new StringBuilder();
        var currentStartLine = 1;

        // Group content by headers
        foreach (var block in document)
        {
            if (block is HeadingBlock heading)
            {
                // Save previous section if it has content
                if (currentSection.Length > 0)
                {
                    var endLine = heading.Line;
                    sections.Add((currentStartLine, endLine, currentSection.ToString()));
                    currentSection.Clear();
                }
                currentStartLine = heading.Line + 1;
            }

            // Add block content to current section
            var blockText = GetBlockText(text, block);
            currentSection.AppendLine(blockText);
        }

        // Add final section
        if (currentSection.Length > 0)
        {
            sections.Add((currentStartLine, lines.Length, currentSection.ToString()));
        }

        // If no sections found, treat as plain text
        if (sections.Count == 0)
        {
            foreach (var chunk in ChunkPlainText(text, lines))
            {
                yield return chunk;
            }
            yield break;
        }

        // Chunk each section
        foreach (var section in sections)
        {
            var sectionLines = lines.Skip(section.StartLine - 1)
                                    .Take(section.EndLine - section.StartLine + 1)
                                    .ToArray();

            foreach (var chunk in ChunkSection(section.Content, sectionLines, section.StartLine))
            {
                yield return chunk;
            }
        }
    }

    /// <summary>
    /// Chunks a section of text into token-limited chunks.
    /// </summary>
    private IEnumerable<TextChunk> ChunkSection(string content, string[] lines, int startLineOffset)
    {
        var tokens = _tokenizer.CountTokens(content);

        if (tokens <= _options.MaxTokens)
        {
            // Section fits in one chunk
            yield return CreateChunk(content, startLineOffset, startLineOffset + lines.Length - 1);
            yield break;
        }

        // Need to split the section
        var currentChunk = new StringBuilder();
        var currentStartLine = startLineOffset;
        var currentLineIndex = 0;
        var currentTokens = 0;

        foreach (var line in lines)
        {
            var lineTokens = _tokenizer.CountTokens(line + "\n");

            if (currentTokens + lineTokens > _options.MaxTokens && currentChunk.Length > 0)
            {
                // Emit current chunk
                yield return CreateChunk(
                    currentChunk.ToString().TrimEnd(),
                    currentStartLine,
                    startLineOffset + currentLineIndex - 1);

                // Start new chunk with overlap
                var overlapLines = GetOverlapLines(lines, currentLineIndex, _options.OverlapTokens);
                currentChunk.Clear();
                foreach (var overlapLine in overlapLines)
                {
                    currentChunk.AppendLine(overlapLine);
                }
                currentStartLine = startLineOffset + currentLineIndex - overlapLines.Count;
                currentTokens = _tokenizer.CountTokens(currentChunk.ToString());
            }

            currentChunk.AppendLine(line);
            currentTokens += lineTokens;
            currentLineIndex++;
        }

        // Emit final chunk
        if (currentChunk.Length > 0)
        {
            yield return CreateChunk(
                currentChunk.ToString().TrimEnd(),
                currentStartLine,
                startLineOffset + lines.Length - 1);
        }
    }

    /// <summary>
    /// Chunks plain text without markdown awareness.
    /// </summary>
    private IEnumerable<TextChunk> ChunkPlainText(string text, string[] lines)
    {
        var currentChunk = new StringBuilder();
        var currentStartLine = 1;
        var currentLineIndex = 0;
        var currentTokens = 0;

        foreach (var line in lines)
        {
            var lineTokens = _tokenizer.CountTokens(line + "\n");

            if (currentTokens + lineTokens > _options.MaxTokens && currentChunk.Length > 0)
            {
                // Emit current chunk
                yield return CreateChunk(
                    currentChunk.ToString().TrimEnd(),
                    currentStartLine,
                    currentLineIndex);

                // Start new chunk with overlap
                var overlapLines = GetOverlapLines(lines, currentLineIndex, _options.OverlapTokens);
                currentChunk.Clear();
                foreach (var overlapLine in overlapLines)
                {
                    currentChunk.AppendLine(overlapLine);
                }
                currentStartLine = currentLineIndex + 1 - overlapLines.Count;
                currentTokens = _tokenizer.CountTokens(currentChunk.ToString());
            }

            currentChunk.AppendLine(line);
            currentTokens += lineTokens;
            currentLineIndex++;
        }

        // Emit final chunk
        if (currentChunk.Length > 0)
        {
            yield return CreateChunk(
                currentChunk.ToString().TrimEnd(),
                currentStartLine,
                lines.Length);
        }
    }

    /// <summary>
    /// Gets overlap lines from the previous chunk.
    /// </summary>
    private List<string> GetOverlapLines(string[] lines, int currentIndex, int overlapTokens)
    {
        var overlapLines = new List<string>();
        var tokens = 0;

        for (var i = currentIndex - 1; i >= 0 && tokens < overlapTokens; i--)
        {
            var lineTokens = _tokenizer.CountTokens(lines[i] + "\n");
            if (tokens + lineTokens > overlapTokens && overlapLines.Count > 0)
            {
                break;
            }
            overlapLines.Insert(0, lines[i]);
            tokens += lineTokens;
        }

        return overlapLines;
    }

    /// <summary>
    /// Merges chunks that are too small.
    /// </summary>
    private List<TextChunk> MergeSmallChunks(List<TextChunk> chunks)
    {
        if (chunks.Count <= 1)
        {
            return chunks;
        }

        var result = new List<TextChunk>();
        TextChunk? pending = null;

        foreach (var chunk in chunks)
        {
            if (pending == null)
            {
                if (chunk.TokenCount < _options.MinTokens)
                {
                    pending = chunk;
                }
                else
                {
                    result.Add(chunk);
                }
            }
            else
            {
                // Merge with pending
                var mergedText = pending.Text + "\n\n" + chunk.Text;
                var mergedTokens = _tokenizer.CountTokens(mergedText);

                if (mergedTokens <= _options.MaxTokens)
                {
                    pending = new TextChunk
                    {
                        Text = mergedText,
                        StartLine = pending.StartLine,
                        EndLine = chunk.EndLine,
                        TokenCount = mergedTokens,
                        Hash = ComputeHash(mergedText)
                    };

                    if (mergedTokens >= _options.MinTokens)
                    {
                        result.Add(pending);
                        pending = null;
                    }
                }
                else
                {
                    result.Add(pending);
                    if (chunk.TokenCount < _options.MinTokens)
                    {
                        pending = chunk;
                    }
                    else
                    {
                        result.Add(chunk);
                        pending = null;
                    }
                }
            }
        }

        if (pending != null)
        {
            result.Add(pending);
        }

        return result;
    }

    /// <summary>
    /// Creates a text chunk with computed hash and token count.
    /// </summary>
    private TextChunk CreateChunk(string text, int startLine, int endLine)
    {
        return new TextChunk
        {
            Text = text,
            StartLine = startLine,
            EndLine = endLine,
            TokenCount = _tokenizer.CountTokens(text),
            Hash = ComputeHash(text)
        };
    }

    /// <summary>
    /// Computes a hash for the given text.
    /// </summary>
    private static string ComputeHash(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var hash = XxHash64.Hash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Gets the text content of a markdown block.
    /// </summary>
    private static string GetBlockText(string fullText, Block block)
    {
        if (block.Span.Start >= 0 && block.Span.End <= fullText.Length)
        {
            return fullText.Substring(block.Span.Start, block.Span.Length);
        }
        return string.Empty;
    }

    /// <summary>
    /// Checks if a file is a markdown file.
    /// </summary>
    private static bool IsMarkdown(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext is ".md" or ".markdown" or ".mdown" or ".mkd";
    }
}
