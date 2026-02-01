namespace Microbot.Memory.Sessions;

using System.Text.Json.Serialization;

/// <summary>
/// Represents a conversation session transcript.
/// </summary>
public class SessionTranscript
{
    /// <summary>
    /// Unique session identifier.
    /// </summary>
    [JsonPropertyName("sessionKey")]
    public string SessionKey { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Session start time.
    /// </summary>
    [JsonPropertyName("startedAt")]
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Session end time (if ended).
    /// </summary>
    [JsonPropertyName("endedAt")]
    public DateTime? EndedAt { get; set; }

    /// <summary>
    /// Session title (auto-generated or user-provided).
    /// </summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>
    /// Session summary (auto-generated).
    /// </summary>
    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    /// <summary>
    /// Transcript entries.
    /// </summary>
    [JsonPropertyName("entries")]
    public List<TranscriptEntry> Entries { get; set; } = [];

    /// <summary>
    /// Session metadata.
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, string> Metadata { get; set; } = [];

    /// <summary>
    /// Gets the total message count.
    /// </summary>
    [JsonIgnore]
    public int MessageCount => Entries.Count;

    /// <summary>
    /// Gets the total token count (if available).
    /// </summary>
    [JsonIgnore]
    public int? TotalTokenCount => Entries.Sum(e => e.TokenCount);

    /// <summary>
    /// Gets the session duration.
    /// </summary>
    [JsonIgnore]
    public TimeSpan? Duration => EndedAt.HasValue ? EndedAt.Value - StartedAt : null;

    /// <summary>
    /// Adds a user message to the transcript.
    /// </summary>
    public void AddUserMessage(string content, int? tokenCount = null)
    {
        Entries.Add(new TranscriptEntry
        {
            Role = "user",
            Content = content,
            TokenCount = tokenCount
        });
    }

    /// <summary>
    /// Adds an assistant message to the transcript.
    /// </summary>
    public void AddAssistantMessage(string content, int? tokenCount = null, List<ToolCallEntry>? toolCalls = null)
    {
        Entries.Add(new TranscriptEntry
        {
            Role = "assistant",
            Content = content,
            TokenCount = tokenCount,
            ToolCalls = toolCalls
        });
    }

    /// <summary>
    /// Adds a system message to the transcript.
    /// </summary>
    public void AddSystemMessage(string content)
    {
        Entries.Add(new TranscriptEntry
        {
            Role = "system",
            Content = content
        });
    }

    /// <summary>
    /// Ends the session.
    /// </summary>
    public void End()
    {
        EndedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Converts the transcript to a plain text format for indexing.
    /// </summary>
    public string ToPlainText()
    {
        var sb = new System.Text.StringBuilder();
        
        if (!string.IsNullOrEmpty(Title))
        {
            sb.AppendLine($"# {Title}");
            sb.AppendLine();
        }

        sb.AppendLine($"Session: {SessionKey}");
        sb.AppendLine($"Started: {StartedAt:yyyy-MM-dd HH:mm:ss}");
        if (EndedAt.HasValue)
        {
            sb.AppendLine($"Ended: {EndedAt:yyyy-MM-dd HH:mm:ss}");
        }
        sb.AppendLine();

        foreach (var entry in Entries)
        {
            var role = entry.Role switch
            {
                "user" => "User",
                "assistant" => "Assistant",
                "system" => "System",
                "tool" => "Tool",
                _ => entry.Role
            };

            sb.AppendLine($"**{role}** ({entry.Timestamp:HH:mm:ss}):");
            sb.AppendLine(entry.Content);
            
            if (entry.ToolCalls?.Count > 0)
            {
                sb.AppendLine("Tool calls:");
                foreach (var tool in entry.ToolCalls)
                {
                    sb.AppendLine($"  - {tool.ToolName}: {(tool.Success ? "Success" : "Failed")}");
                }
            }
            
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
