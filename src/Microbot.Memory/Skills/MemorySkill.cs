namespace Microbot.Memory.Skills;

using System.ComponentModel;
using System.Text;
using Microsoft.SemanticKernel;
using Microbot.Memory.Interfaces;

/// <summary>
/// Semantic Kernel skill for memory operations.
/// </summary>
public class MemorySkill
{
    private readonly IMemoryManager _memoryManager;

    /// <summary>
    /// Creates a new MemorySkill.
    /// </summary>
    public MemorySkill(IMemoryManager memoryManager)
    {
        _memoryManager = memoryManager;
    }

    /// <summary>
    /// Searches memory for relevant information.
    /// </summary>
    [KernelFunction("search_memory")]
    [Description("Search long-term memory for relevant information based on a query. Returns relevant text snippets from past conversations and stored documents.")]
    public async Task<string> SearchMemoryAsync(
        [Description("The search query to find relevant information")] string query,
        [Description("Maximum number of results to return (default: 5)")] int maxResults = 5,
        [Description("Whether to include past session transcripts (default: true)")] bool includeSessions = true,
        [Description("Whether to include memory files (default: true)")] bool includeMemoryFiles = true,
        CancellationToken cancellationToken = default)
    {
        var options = new MemorySearchOptions
        {
            MaxResults = maxResults,
            IncludeSessions = includeSessions,
            IncludeMemoryFiles = includeMemoryFiles,
            MinScore = 0.5f
        };

        var results = await _memoryManager.SearchAsync(query, options, cancellationToken);

        if (results.Count == 0)
        {
            return "No relevant information found in memory.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Found {results.Count} relevant memory entries:");
        sb.AppendLine();

        foreach (var result in results)
        {
            var sourceLabel = result.Source == MemorySource.Sessions ? "Session" : "Memory";
            sb.AppendLine($"**[{sourceLabel}] {result.Path}** (Score: {result.Score:F2})");
            sb.AppendLine($"Lines {result.StartLine}-{result.EndLine}:");
            sb.AppendLine("```");
            sb.AppendLine(result.Snippet);
            sb.AppendLine("```");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Saves information to long-term memory.
    /// </summary>
    [KernelFunction("save_to_memory")]
    [Description("Save important information to long-term memory for future reference. Use this to remember facts, preferences, or important details the user wants to keep.")]
    public async Task<string> SaveToMemoryAsync(
        [Description("The information to save to memory")] string content,
        [Description("Optional filename for the memory (without extension)")] string? filename = null,
        CancellationToken cancellationToken = default)
    {
        var path = filename != null ? $"{filename}.md" : null;
        
        await _memoryManager.AddMemoryAsync(content, MemorySource.Memory, path, cancellationToken);

        return $"Information saved to memory{(path != null ? $" as '{path}'" : "")}.";
    }

    /// <summary>
    /// Gets the current memory status.
    /// </summary>
    [KernelFunction("get_memory_status")]
    [Description("Get the current status of the memory system, including number of indexed files and chunks.")]
    public string GetMemoryStatus()
    {
        var status = _memoryManager.GetStatus();

        var sb = new StringBuilder();
        sb.AppendLine("**Memory System Status**");
        sb.AppendLine($"- Total files indexed: {status.TotalFiles}");
        sb.AppendLine($"- Total text chunks: {status.TotalChunks}");
        sb.AppendLine($"- Memory files: {status.MemoryFiles}");
        sb.AppendLine($"- Session files: {status.SessionFiles}");
        sb.AppendLine($"- Pending changes: {(status.IsDirty ? "Yes" : "No")}");
        
        if (status.LastSyncAt.HasValue)
        {
            sb.AppendLine($"- Last sync: {status.LastSyncAt.Value:yyyy-MM-dd HH:mm:ss} UTC");
        }

        if (!string.IsNullOrEmpty(status.EmbeddingModel))
        {
            sb.AppendLine($"- Embedding model: {status.EmbeddingModel}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Lists recent sessions.
    /// </summary>
    [KernelFunction("list_sessions")]
    [Description("List recent conversation sessions stored in memory.")]
    public async Task<string> ListSessionsAsync(
        [Description("Maximum number of sessions to list (default: 10)")] int maxSessions = 10,
        CancellationToken cancellationToken = default)
    {
        var sessions = await _memoryManager.ListSessionsAsync(cancellationToken);

        if (sessions.Count == 0)
        {
            return "No sessions found in memory.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"**Recent Sessions** ({Math.Min(sessions.Count, maxSessions)} of {sessions.Count}):");
        sb.AppendLine();

        foreach (var session in sessions.Take(maxSessions))
        {
            sb.AppendLine($"- **{session.SessionKey}**");
            if (!string.IsNullOrEmpty(session.Title))
            {
                sb.AppendLine($"  Title: {session.Title}");
            }
            sb.AppendLine($"  Started: {session.StartedAt:yyyy-MM-dd HH:mm}");
            sb.AppendLine($"  Messages: {session.MessageCount}");
            if (!string.IsNullOrEmpty(session.Summary))
            {
                sb.AppendLine($"  Summary: {session.Summary}");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Triggers a memory sync.
    /// </summary>
    [KernelFunction("sync_memory")]
    [Description("Synchronize the memory index with source files. Use this if you've added new files to the memory folder.")]
    public async Task<string> SyncMemoryAsync(
        [Description("Force full re-index even if files haven't changed")] bool force = false,
        CancellationToken cancellationToken = default)
    {
        var options = new SyncOptions
        {
            Reason = "User requested sync",
            Force = force
        };

        var progress = new SyncProgress();
        await _memoryManager.SyncAsync(options, new Progress<SyncProgress>(p => progress = p), cancellationToken);

        return $"Memory sync complete: {progress.FilesProcessed} files processed, {progress.ChunksCreated} chunks created.";
    }
}
