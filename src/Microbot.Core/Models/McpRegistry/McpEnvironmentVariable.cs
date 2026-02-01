namespace Microbot.Core.Models.McpRegistry;

using System.Text.Json.Serialization;

/// <summary>
/// Represents an environment variable definition for an MCP server.
/// </summary>
public class McpEnvironmentVariable
{
    /// <summary>
    /// Name of the environment variable.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Description of what the variable is used for.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Expected format/type of the value (e.g., "string", "url", "path").
    /// </summary>
    [JsonPropertyName("format")]
    public string? Format { get; set; }

    /// <summary>
    /// Whether this variable contains sensitive data.
    /// </summary>
    [JsonPropertyName("isSecret")]
    public bool IsSecret { get; set; }

    /// <summary>
    /// Whether this variable is required for the server to function.
    /// </summary>
    [JsonPropertyName("isRequired")]
    public bool IsRequired { get; set; }

    /// <summary>
    /// Default value if not provided.
    /// </summary>
    [JsonPropertyName("default")]
    public string? Default { get; set; }
}

/// <summary>
/// Represents a runtime argument for an MCP server.
/// </summary>
public class McpRuntimeArgument
{
    /// <summary>
    /// Argument name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Description of the argument.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Expected format/type of the value.
    /// </summary>
    [JsonPropertyName("format")]
    public string? Format { get; set; }

    /// <summary>
    /// Argument type (e.g., "named", "positional").
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>
    /// Whether this argument is required.
    /// </summary>
    [JsonPropertyName("isRequired")]
    public bool IsRequired { get; set; }

    /// <summary>
    /// Default value if not provided.
    /// </summary>
    [JsonPropertyName("default")]
    public string? Default { get; set; }

    /// <summary>
    /// Hint for the value (e.g., "port", "volume").
    /// </summary>
    [JsonPropertyName("valueHint")]
    public string? ValueHint { get; set; }

    /// <summary>
    /// Whether this argument can be repeated.
    /// </summary>
    [JsonPropertyName("isRepeated")]
    public bool IsRepeated { get; set; }
}
