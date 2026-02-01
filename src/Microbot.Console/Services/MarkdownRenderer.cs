namespace Microbot.Console.Services;

using Spectre.Console;
using Spectre.Console.Rendering;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Renders markdown text as Spectre.Console formatted output.
/// Supports headers, bold, italic, code blocks, inline code, lists, and links.
/// </summary>
public partial class MarkdownRenderer
{
    // Regex patterns for markdown elements
    [GeneratedRegex(@"^(#{1,6})\s+(.+)$", RegexOptions.Multiline)]
    private static partial Regex HeaderRegex();

    [GeneratedRegex(@"```(\w*)\r?\n([\s\S]*?)```", RegexOptions.Multiline)]
    private static partial Regex CodeBlockRegex();

    [GeneratedRegex(@"`([^`]+)`")]
    private static partial Regex InlineCodeRegex();

    [GeneratedRegex(@"\*\*\*(.+?)\*\*\*")]
    private static partial Regex BoldItalicRegex();

    [GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial Regex BoldRegex();

    [GeneratedRegex(@"__(.+?)__")]
    private static partial Regex BoldUnderscoreRegex();

    [GeneratedRegex(@"\*(.+?)\*")]
    private static partial Regex ItalicRegex();

    [GeneratedRegex(@"_(.+?)_")]
    private static partial Regex ItalicUnderscoreRegex();

    [GeneratedRegex(@"~~(.+?)~~")]
    private static partial Regex StrikethroughRegex();

    [GeneratedRegex(@"\[([^\]]+)\]\(([^)]+)\)")]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"^(\s*)[-*+]\s+(.+)$", RegexOptions.Multiline)]
    private static partial Regex UnorderedListRegex();

    [GeneratedRegex(@"^(\s*)\d+\.\s+(.+)$", RegexOptions.Multiline)]
    private static partial Regex OrderedListRegex();

    [GeneratedRegex(@"^>\s*(.*)$", RegexOptions.Multiline)]
    private static partial Regex BlockquoteRegex();

    [GeneratedRegex(@"^---+$|^\*\*\*+$|^___+$", RegexOptions.Multiline)]
    private static partial Regex HorizontalRuleRegex();

    /// <summary>
    /// Renders markdown text to the console using Spectre.Console formatting.
    /// </summary>
    /// <param name="markdown">The markdown text to render.</param>
    public void Render(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return;
        }

        var renderables = ParseMarkdown(markdown);
        foreach (var renderable in renderables)
        {
            AnsiConsole.Write(renderable);
        }
    }

    /// <summary>
    /// Parses markdown text and returns a list of Spectre.Console renderables.
    /// </summary>
    /// <param name="markdown">The markdown text to parse.</param>
    /// <returns>A list of renderables.</returns>
    public List<IRenderable> ParseMarkdown(string markdown)
    {
        var renderables = new List<IRenderable>();
        
        // Normalize line endings
        markdown = markdown.Replace("\r\n", "\n");
        
        // Process code blocks first (to avoid processing markdown inside them)
        var segments = ExtractCodeBlocks(markdown);
        
        foreach (var segment in segments)
        {
            if (segment.IsCodeBlock)
            {
                renderables.Add(CreateCodeBlockPanel(segment.Content, segment.Language));
            }
            else
            {
                renderables.AddRange(ProcessTextSegment(segment.Content));
            }
        }

        return renderables;
    }

    /// <summary>
    /// Extracts code blocks from markdown, returning segments of code and non-code content.
    /// </summary>
    private List<MarkdownSegment> ExtractCodeBlocks(string markdown)
    {
        var segments = new List<MarkdownSegment>();
        var matches = CodeBlockRegex().Matches(markdown);
        
        if (matches.Count == 0)
        {
            segments.Add(new MarkdownSegment { Content = markdown, IsCodeBlock = false });
            return segments;
        }

        int lastIndex = 0;
        foreach (Match match in matches)
        {
            // Add text before the code block
            if (match.Index > lastIndex)
            {
                var textBefore = markdown.Substring(lastIndex, match.Index - lastIndex);
                if (!string.IsNullOrWhiteSpace(textBefore))
                {
                    segments.Add(new MarkdownSegment { Content = textBefore, IsCodeBlock = false });
                }
            }

            // Add the code block
            segments.Add(new MarkdownSegment
            {
                Content = match.Groups[2].Value.TrimEnd(),
                Language = match.Groups[1].Value,
                IsCodeBlock = true
            });

            lastIndex = match.Index + match.Length;
        }

        // Add remaining text after the last code block
        if (lastIndex < markdown.Length)
        {
            var textAfter = markdown.Substring(lastIndex);
            if (!string.IsNullOrWhiteSpace(textAfter))
            {
                segments.Add(new MarkdownSegment { Content = textAfter, IsCodeBlock = false });
            }
        }

        return segments;
    }

    /// <summary>
    /// Creates a panel for a code block with syntax highlighting indication.
    /// </summary>
    private Panel CreateCodeBlockPanel(string code, string language)
    {
        var header = string.IsNullOrEmpty(language) ? "Code" : language;
        
        // Escape any Spectre markup in the code
        var escapedCode = Markup.Escape(code);
        
        return new Panel(new Text(code))
        {
            Header = new PanelHeader($"[grey]{header}[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Grey),
            Padding = new Padding(1, 0)
        };
    }

    /// <summary>
    /// Processes a text segment (non-code block) and returns renderables.
    /// </summary>
    private List<IRenderable> ProcessTextSegment(string text)
    {
        var renderables = new List<IRenderable>();
        var lines = text.Split('\n');
        var currentParagraph = new StringBuilder();
        var inBlockquote = false;
        var blockquoteContent = new StringBuilder();

        foreach (var line in lines)
        {
            // Check for horizontal rule
            if (HorizontalRuleRegex().IsMatch(line))
            {
                FlushParagraph(renderables, currentParagraph);
                FlushBlockquote(renderables, blockquoteContent, ref inBlockquote);
                renderables.Add(new Rule().RuleStyle("grey"));
                continue;
            }

            // Check for headers
            var headerMatch = HeaderRegex().Match(line);
            if (headerMatch.Success)
            {
                FlushParagraph(renderables, currentParagraph);
                FlushBlockquote(renderables, blockquoteContent, ref inBlockquote);
                renderables.Add(CreateHeader(headerMatch.Groups[1].Value.Length, headerMatch.Groups[2].Value));
                continue;
            }

            // Check for blockquote
            var blockquoteMatch = BlockquoteRegex().Match(line);
            if (blockquoteMatch.Success)
            {
                FlushParagraph(renderables, currentParagraph);
                inBlockquote = true;
                blockquoteContent.AppendLine(blockquoteMatch.Groups[1].Value);
                continue;
            }
            else if (inBlockquote)
            {
                FlushBlockquote(renderables, blockquoteContent, ref inBlockquote);
            }

            // Check for unordered list
            var unorderedListMatch = UnorderedListRegex().Match(line);
            if (unorderedListMatch.Success)
            {
                FlushParagraph(renderables, currentParagraph);
                var indent = unorderedListMatch.Groups[1].Value.Length / 2;
                var content = FormatInlineMarkdown(unorderedListMatch.Groups[2].Value);
                var bullet = indent > 0 ? "◦" : "•";
                var indentStr = new string(' ', indent * 2);
                renderables.Add(new Markup($"{indentStr}[cyan]{bullet}[/] {content}\n"));
                continue;
            }

            // Check for ordered list
            var orderedListMatch = OrderedListRegex().Match(line);
            if (orderedListMatch.Success)
            {
                FlushParagraph(renderables, currentParagraph);
                var indent = orderedListMatch.Groups[1].Value.Length / 2;
                var content = FormatInlineMarkdown(orderedListMatch.Groups[2].Value);
                var indentStr = new string(' ', indent * 2);
                // Extract the number from the original line
                var numberMatch = Regex.Match(line.TrimStart(), @"^(\d+)\.");
                var number = numberMatch.Success ? numberMatch.Groups[1].Value : "1";
                renderables.Add(new Markup($"{indentStr}[cyan]{number}.[/] {content}\n"));
                continue;
            }

            // Empty line - flush paragraph
            if (string.IsNullOrWhiteSpace(line))
            {
                FlushParagraph(renderables, currentParagraph);
                continue;
            }

            // Regular text - add to current paragraph
            if (currentParagraph.Length > 0)
            {
                currentParagraph.Append(' ');
            }
            currentParagraph.Append(line.Trim());
        }

        // Flush any remaining content
        FlushParagraph(renderables, currentParagraph);
        FlushBlockquote(renderables, blockquoteContent, ref inBlockquote);

        return renderables;
    }

    /// <summary>
    /// Flushes the current paragraph to renderables.
    /// </summary>
    private void FlushParagraph(List<IRenderable> renderables, StringBuilder paragraph)
    {
        if (paragraph.Length > 0)
        {
            var formatted = FormatInlineMarkdown(paragraph.ToString());
            renderables.Add(new Markup(formatted + "\n"));
            paragraph.Clear();
        }
    }

    /// <summary>
    /// Flushes blockquote content to renderables.
    /// </summary>
    private void FlushBlockquote(List<IRenderable> renderables, StringBuilder content, ref bool inBlockquote)
    {
        if (inBlockquote && content.Length > 0)
        {
            var formatted = FormatInlineMarkdown(content.ToString().TrimEnd());
            renderables.Add(new Panel(new Markup(formatted))
            {
                Border = BoxBorder.None,
                BorderStyle = new Style(Color.Grey),
                Padding = new Padding(2, 0, 0, 0)
            });
            renderables.Add(new Text("\n"));
            content.Clear();
        }
        inBlockquote = false;
    }

    /// <summary>
    /// Creates a header renderable based on the level.
    /// </summary>
    private IRenderable CreateHeader(int level, string text)
    {
        var formattedText = FormatInlineMarkdown(text);
        
        return level switch
        {
            1 => new Markup($"[bold cyan]{formattedText}[/]\n"),
            2 => new Markup($"[bold blue]{formattedText}[/]\n"),
            3 => new Markup($"[bold white]{formattedText}[/]\n"),
            4 => new Markup($"[bold grey]{formattedText}[/]\n"),
            5 => new Markup($"[grey]{formattedText}[/]\n"),
            6 => new Markup($"[dim grey]{formattedText}[/]\n"),
            _ => new Markup($"[bold]{formattedText}[/]\n")
        };
    }

    /// <summary>
    /// Formats inline markdown elements (bold, italic, code, links, etc.).
    /// </summary>
    private string FormatInlineMarkdown(string text)
    {
        // First, escape any existing Spectre markup characters that aren't part of our markdown
        // We need to be careful here - escape [ and ] that aren't part of markdown links
        
        // Process inline code first (to avoid processing markdown inside code)
        var codeSegments = new List<(int Start, int End, string Replacement)>();
        foreach (Match match in InlineCodeRegex().Matches(text))
        {
            var code = Markup.Escape(match.Groups[1].Value);
            codeSegments.Add((match.Index, match.Index + match.Length, $"[grey on grey23]{code}[/]"));
        }

        // Apply code replacements in reverse order to maintain indices
        var result = new StringBuilder(text);
        for (int i = codeSegments.Count - 1; i >= 0; i--)
        {
            var segment = codeSegments[i];
            result.Remove(segment.Start, segment.End - segment.Start);
            result.Insert(segment.Start, segment.Replacement);
        }
        text = result.ToString();

        // Process links: [text](url)
        text = LinkRegex().Replace(text, match =>
        {
            var linkText = Markup.Escape(match.Groups[1].Value);
            var url = match.Groups[2].Value;
            return $"[link={url}][blue underline]{linkText}[/][/]";
        });

        // Process bold+italic: ***text***
        text = BoldItalicRegex().Replace(text, match =>
        {
            var content = Markup.Escape(match.Groups[1].Value);
            return $"[bold italic]{content}[/]";
        });

        // Process bold: **text** or __text__
        text = BoldRegex().Replace(text, match =>
        {
            var content = Markup.Escape(match.Groups[1].Value);
            return $"[bold]{content}[/]";
        });
        text = BoldUnderscoreRegex().Replace(text, match =>
        {
            var content = Markup.Escape(match.Groups[1].Value);
            return $"[bold]{content}[/]";
        });

        // Process italic: *text* or _text_
        text = ItalicRegex().Replace(text, match =>
        {
            var content = Markup.Escape(match.Groups[1].Value);
            return $"[italic]{content}[/]";
        });
        text = ItalicUnderscoreRegex().Replace(text, match =>
        {
            var content = Markup.Escape(match.Groups[1].Value);
            return $"[italic]{content}[/]";
        });

        // Process strikethrough: ~~text~~
        text = StrikethroughRegex().Replace(text, match =>
        {
            var content = Markup.Escape(match.Groups[1].Value);
            return $"[strikethrough]{content}[/]";
        });

        return text;
    }

    /// <summary>
    /// Represents a segment of markdown content.
    /// </summary>
    private class MarkdownSegment
    {
        public string Content { get; set; } = string.Empty;
        public string Language { get; set; } = string.Empty;
        public bool IsCodeBlock { get; set; }
    }
}
