namespace Microbot.Core.Models.McpRegistry;

using System.Text.Json.Serialization;

/// <summary>
/// Represents an MCP server from the official MCP Registry.
/// </summary>
public class McpRegistryServer
{
    /// <summary>
    /// JSON schema URL for the server definition.
    /// </summary>
    [JsonPropertyName("$schema")]
    public string? Schema { get; set; }

    /// <summary>
    /// Unique name/identifier for the server (e.g., "io.github/github-mcp").
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable title for the server.
    /// </summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>
    /// Description of what the server provides.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Repository information.
    /// </summary>
    [JsonPropertyName("repository")]
    public McpRepository? Repository { get; set; }

    /// <summary>
    /// Current version of the server.
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Available packages for this server (npm, oci, etc.).
    /// </summary>
    [JsonPropertyName("packages")]
    public List<McpRegistryPackage> Packages { get; set; } = [];

    /// <summary>
    /// Remote server configurations (for streamable-http transport).
    /// </summary>
    [JsonPropertyName("remotes")]
    public List<McpRemote>? Remotes { get; set; }

    /// <summary>
    /// Environment variables required or supported by this server.
    /// </summary>
    [JsonPropertyName("environmentVariables")]
    public List<McpEnvironmentVariable>? EnvironmentVariables { get; set; }

    /// <summary>
    /// HTTP headers required by the server.
    /// </summary>
    [JsonPropertyName("headers")]
    public List<McpEnvironmentVariable>? Headers { get; set; }

    /// <summary>
    /// Runtime arguments for the server.
    /// </summary>
    [JsonPropertyName("runtimeArguments")]
    public List<McpRuntimeArgument>? RuntimeArguments { get; set; }

    /// <summary>
    /// Package-specific arguments.
    /// </summary>
    [JsonPropertyName("packageArguments")]
    public List<McpRuntimeArgument>? PackageArguments { get; set; }

    /// <summary>
    /// Usage examples.
    /// </summary>
    [JsonPropertyName("examples")]
    public List<McpExample>? Examples { get; set; }

    /// <summary>
    /// Documentation URL.
    /// </summary>
    [JsonPropertyName("documentation")]
    public string? Documentation { get; set; }

    /// <summary>
    /// Website URL.
    /// </summary>
    [JsonPropertyName("websiteUrl")]
    public string? WebsiteUrl { get; set; }

    /// <summary>
    /// Publisher information.
    /// </summary>
    [JsonPropertyName("publisher")]
    public string? Publisher { get; set; }

    /// <summary>
    /// Keywords/tags for the server.
    /// </summary>
    [JsonPropertyName("keywords")]
    public List<string>? Keywords { get; set; }

    /// <summary>
    /// License information.
    /// </summary>
    [JsonPropertyName("license")]
    public string? License { get; set; }

    /// <summary>
    /// Additional notes about the server.
    /// </summary>
    [JsonPropertyName("notes")]
    public List<string>? Notes { get; set; }

    /// <summary>
    /// Registry metadata.
    /// </summary>
    [JsonPropertyName("_meta")]
    public McpRegistryMeta? Meta { get; set; }

    /// <summary>
    /// Gets the display name for the server.
    /// </summary>
    public string DisplayName => Title ?? Name;

    /// <summary>
    /// Gets the preferred package for installation (prefers npm over oci).
    /// </summary>
    public McpRegistryPackage? PreferredPackage =>
        Packages.FirstOrDefault(p => p.RegistryType == "npm") ??
        Packages.FirstOrDefault();
}

/// <summary>
/// Repository information for an MCP server.
/// </summary>
public class McpRepository
{
    /// <summary>
    /// Repository URL.
    /// </summary>
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Source type (e.g., "github").
    /// </summary>
    [JsonPropertyName("source")]
    public string? Source { get; set; }
}

/// <summary>
/// Remote server configuration for streamable-http transport.
/// </summary>
public class McpRemote
{
    /// <summary>
    /// Transport type (e.g., "streamable-http").
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Remote URL.
    /// </summary>
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;
}

/// <summary>
/// Usage example for an MCP server.
/// </summary>
public class McpExample
{
    /// <summary>
    /// Example configuration type.
    /// </summary>
    [JsonPropertyName("config")]
    public string? Config { get; set; }

    /// <summary>
    /// Example description.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Example name.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

/// <summary>
/// Registry metadata for an MCP server.
/// </summary>
public class McpRegistryMeta
{
    /// <summary>
    /// Server status (e.g., "active").
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>
    /// When the server was published.
    /// </summary>
    [JsonPropertyName("publishedAt")]
    public string? PublishedAt { get; set; }

    /// <summary>
    /// When the server was last updated.
    /// </summary>
    [JsonPropertyName("updatedAt")]
    public string? UpdatedAt { get; set; }

    /// <summary>
    /// Whether this is the latest version.
    /// </summary>
    [JsonPropertyName("isLatest")]
    public bool IsLatest { get; set; }
}
