namespace Microbot.Core.Models.McpRegistry;

using System.Text.Json.Serialization;

/// <summary>
/// Response wrapper for the MCP Registry list servers API.
/// </summary>
public class McpRegistryListResponse
{
    /// <summary>
    /// List of servers with their full details.
    /// </summary>
    [JsonPropertyName("servers")]
    public List<McpRegistryServerWrapper> Servers { get; set; } = [];

    /// <summary>
    /// Pagination metadata.
    /// </summary>
    [JsonPropertyName("metadata")]
    public McpRegistryMetadata? Metadata { get; set; }
}

/// <summary>
/// Wrapper for server in list response (contains nested "server" object).
/// </summary>
public class McpRegistryServerWrapper
{
    /// <summary>
    /// The server details.
    /// </summary>
    [JsonPropertyName("server")]
    public McpRegistryServer Server { get; set; } = new();
}

/// <summary>
/// Pagination metadata for registry responses.
/// </summary>
public class McpRegistryMetadata
{
    /// <summary>
    /// Cursor for the next page of results.
    /// </summary>
    [JsonPropertyName("nextCursor")]
    public string? NextCursor { get; set; }

    /// <summary>
    /// Number of items in the current response.
    /// </summary>
    [JsonPropertyName("count")]
    public int Count { get; set; }
}

/// <summary>
/// Response for getting a specific server version.
/// </summary>
public class McpRegistryServerResponse
{
    /// <summary>
    /// The server details.
    /// </summary>
    [JsonPropertyName("server")]
    public McpRegistryServer Server { get; set; } = new();
}

/// <summary>
/// Response for getting all versions of a server.
/// </summary>
public class McpRegistryVersionsResponse
{
    /// <summary>
    /// List of available versions.
    /// </summary>
    [JsonPropertyName("versions")]
    public List<McpRegistryServerWrapper> Versions { get; set; } = [];
}
