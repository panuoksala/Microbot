namespace Microbot.Core.Models.McpRegistry;

using System.Text.Json.Serialization;

/// <summary>
/// Represents a package distribution for an MCP server.
/// </summary>
public class McpRegistryPackage
{
    /// <summary>
    /// Registry type: "npm" for npm packages, "oci" for Docker/OCI images.
    /// </summary>
    [JsonPropertyName("registryType")]
    public string RegistryType { get; set; } = string.Empty;

    /// <summary>
    /// Package identifier (e.g., "@scope/package" for npm, "docker.io/image" for oci).
    /// </summary>
    [JsonPropertyName("identifier")]
    public string Identifier { get; set; } = string.Empty;

    /// <summary>
    /// Package version.
    /// </summary>
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    /// <summary>
    /// Transport configuration.
    /// </summary>
    [JsonPropertyName("transport")]
    public McpTransport? Transport { get; set; }

    /// <summary>
    /// Runtime hint (e.g., "docker").
    /// </summary>
    [JsonPropertyName("runtimeHint")]
    public string? RuntimeHint { get; set; }

    /// <summary>
    /// Gets the command to run this package.
    /// </summary>
    public string GetCommand()
    {
        return RegistryType switch
        {
            "npm" => "npx",
            "oci" => "docker",
            _ => throw new NotSupportedException($"Unsupported registry type: {RegistryType}")
        };
    }

    /// <summary>
    /// Gets the arguments to run this package.
    /// </summary>
    public List<string> GetArguments()
    {
        return RegistryType switch
        {
            "npm" => ["-y", Identifier],
            "oci" => ["run", "-i", "--rm", Identifier],
            _ => throw new NotSupportedException($"Unsupported registry type: {RegistryType}")
        };
    }
}

/// <summary>
/// Transport configuration for an MCP package.
/// </summary>
public class McpTransport
{
    /// <summary>
    /// Transport type: "stdio" or "streamable-http".
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "stdio";

    /// <summary>
    /// URL for streamable-http transport.
    /// </summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }
}
