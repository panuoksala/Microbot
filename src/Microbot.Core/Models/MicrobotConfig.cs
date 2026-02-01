namespace Microbot.Core.Models;

/// <summary>
/// Root configuration for the Microbot application.
/// </summary>
public class MicrobotConfig
{
    /// <summary>
    /// Configuration file version for migration purposes.
    /// </summary>
    public string Version { get; set; } = "1.0";

    /// <summary>
    /// AI provider configuration (OpenAI, Azure OpenAI, etc.).
    /// </summary>
    public AiProviderConfig AiProvider { get; set; } = new();

    /// <summary>
    /// Skills/plugins configuration.
    /// </summary>
    public SkillsConfig Skills { get; set; } = new();

    /// <summary>
    /// User preferences and settings.
    /// </summary>
    public UserPreferences Preferences { get; set; } = new();
}

/// <summary>
/// Configuration for the AI provider (LLM service).
/// </summary>
public class AiProviderConfig
{
    /// <summary>
    /// The AI provider type: "OpenAI", "AzureOpenAI", etc.
    /// </summary>
    public string Provider { get; set; } = "AzureOpenAI";

    /// <summary>
    /// The model/deployment ID to use.
    /// </summary>
    public string ModelId { get; set; } = "gpt-4o";

    /// <summary>
    /// The endpoint URL (required for Azure OpenAI).
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// The API key for authentication.
    /// </summary>
    public string? ApiKey { get; set; }
}

/// <summary>
/// Configuration for skills/plugins loading.
/// </summary>
public class SkillsConfig
{
    /// <summary>
    /// Path to the folder containing MCP server configurations.
    /// </summary>
    public string McpFolder { get; set; } = "./skills/mcp";

    /// <summary>
    /// Path to the folder containing NuGet package DLLs.
    /// </summary>
    public string NuGetFolder { get; set; } = "./skills/nuget";

    /// <summary>
    /// List of MCP server configurations.
    /// </summary>
    public List<McpServerConfig> McpServers { get; set; } = [];

    /// <summary>
    /// List of NuGet skill configurations.
    /// </summary>
    public List<NuGetSkillConfig> NuGetSkills { get; set; } = [];
}

/// <summary>
/// Configuration for an individual MCP server.
/// </summary>
public class McpServerConfig
{
    /// <summary>
    /// Unique name for this MCP server.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Description of what this MCP server provides.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// The command to execute (e.g., "npx", "docker", "dotnet").
    /// </summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>
    /// Arguments to pass to the command.
    /// </summary>
    public List<string> Args { get; set; } = [];

    /// <summary>
    /// Environment variables to set for the process.
    /// </summary>
    public Dictionary<string, string> Env { get; set; } = [];

    /// <summary>
    /// Whether this MCP server is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Configuration for an individual NuGet-based skill.
/// </summary>
public class NuGetSkillConfig
{
    /// <summary>
    /// Unique name for this skill.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Path to the assembly file (relative to NuGetFolder or absolute).
    /// </summary>
    public string AssemblyPath { get; set; } = string.Empty;

    /// <summary>
    /// Optional: Specific type name to load from the assembly.
    /// If not specified, all types with KernelFunction methods will be loaded.
    /// </summary>
    public string? TypeName { get; set; }

    /// <summary>
    /// Whether this skill is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// User preferences and application settings.
/// </summary>
public class UserPreferences
{
    /// <summary>
    /// The name of the AI agent.
    /// </summary>
    public string AgentName { get; set; } = "Microbot";

    /// <summary>
    /// UI theme (for future use).
    /// </summary>
    public string Theme { get; set; } = "default";

    /// <summary>
    /// Enable verbose logging output.
    /// </summary>
    public bool VerboseLogging { get; set; } = false;

    /// <summary>
    /// Maximum number of messages to keep in chat history.
    /// </summary>
    public int MaxHistoryMessages { get; set; } = 100;

    /// <summary>
    /// Whether to use streaming responses.
    /// </summary>
    public bool UseStreaming { get; set; } = true;
}
