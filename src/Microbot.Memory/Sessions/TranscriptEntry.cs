namespace Microbot.Memory.Sessions;

using System.Text.Json.Serialization;

/// <summary>
/// Individual entry in a session transcript.
/// </summary>
public class TranscriptEntry
{
    /// <summary>
    /// Entry timestamp.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Role: "user", "assistant", "system", "tool".
    /// </summary>
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// Message content.
    /// </summary>
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Tool calls made (if any).
    /// </summary>
    [JsonPropertyName("toolCalls")]
    public List<ToolCallEntry>? ToolCalls { get; set; }

    /// <summary>
    /// Token count for this entry.
    /// </summary>
    [JsonPropertyName("tokenCount")]
    public int? TokenCount { get; set; }
}

/// <summary>
/// Tool call entry in a transcript.
/// </summary>
public class ToolCallEntry
{
    /// <summary>
    /// Name of the tool called.
    /// </summary>
    [JsonPropertyName("toolName")]
    public string ToolName { get; set; } = string.Empty;

    /// <summary>
    /// Arguments passed to the tool (JSON string).
    /// </summary>
    [JsonPropertyName("arguments")]
    public string Arguments { get; set; } = string.Empty;

    /// <summary>
    /// Result from the tool (if available).
    /// </summary>
    [JsonPropertyName("result")]
    public string? Result { get; set; }

    /// <summary>
    /// Whether the tool call succeeded.
    /// </summary>
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    /// <summary>
    /// Duration of the tool call in milliseconds.
    /// </summary>
    [JsonPropertyName("durationMs")]
    public int DurationMs { get; set; }

    /// <summary>
    /// Error message if the tool call failed.
    /// </summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
