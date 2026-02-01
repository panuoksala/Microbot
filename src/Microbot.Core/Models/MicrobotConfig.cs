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

    /// <summary>
    /// Outlook skill configuration.
    /// </summary>
    public OutlookSkillConfig Outlook { get; set; } = new();

    /// <summary>
    /// Teams skill configuration.
    /// </summary>
    public TeamsSkillConfig Teams { get; set; } = new();
}

/// <summary>
/// Configuration for the Outlook skill.
/// </summary>
public class OutlookSkillConfig
{
    /// <summary>
    /// Whether the Outlook skill is enabled.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// The permission mode for the Outlook skill: "ReadOnly", "ReadWriteCalendar", or "Full".
    /// </summary>
    public string Mode { get; set; } = "ReadOnly";

    /// <summary>
    /// Azure AD Application (client) ID.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Azure AD Tenant ID. Use "common" for multi-tenant or personal accounts.
    /// </summary>
    public string TenantId { get; set; } = "common";

    /// <summary>
    /// Authentication method: "DeviceCode" or "InteractiveBrowser".
    /// </summary>
    public string AuthenticationMethod { get; set; } = "DeviceCode";

    /// <summary>
    /// Redirect URI for Interactive Browser authentication.
    /// </summary>
    public string RedirectUri { get; set; } = "http://localhost";
}

/// <summary>
/// Configuration for the Teams skill.
/// </summary>
public class TeamsSkillConfig
{
    /// <summary>
    /// Whether the Teams skill is enabled.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// The permission mode for the Teams skill: "ReadOnly" or "Full".
    /// </summary>
    public string Mode { get; set; } = "ReadOnly";

    /// <summary>
    /// Azure AD Application (client) ID.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Azure AD Tenant ID. Use "common" for multi-tenant access (home + guest tenants).
    /// </summary>
    public string TenantId { get; set; } = "common";

    /// <summary>
    /// Authentication method: "DeviceCode" or "InteractiveBrowser".
    /// </summary>
    public string AuthenticationMethod { get; set; } = "DeviceCode";

    /// <summary>
    /// Redirect URI for Interactive Browser authentication.
    /// </summary>
    public string RedirectUri { get; set; } = "http://localhost";
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
    /// Supports special syntax:
    /// - ${env:VAR_NAME} - Load from system environment variable
    /// - ${secret:key_name} - Load from secrets section
    /// - Plain value - Use as-is
    /// </summary>
    public Dictionary<string, string> Env { get; set; } = [];

    /// <summary>
    /// Whether this MCP server is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Original name from the MCP Registry (e.g., "io.github/github-mcp").
    /// Used to track installed servers and check for updates.
    /// </summary>
    public string? RegistryName { get; set; }

    /// <summary>
    /// Version installed from the MCP Registry.
    /// </summary>
    public string? RegistryVersion { get; set; }

    /// <summary>
    /// Package type from registry: "npm" or "oci".
    /// </summary>
    public string? RegistryPackageType { get; set; }

    /// <summary>
    /// Package identifier from registry (e.g., "@scope/package" or "docker.io/image").
    /// </summary>
    public string? RegistryPackageId { get; set; }

    /// <summary>
    /// Definitions of required/optional environment variables from the registry.
    /// Used to inform users about configuration requirements.
    /// </summary>
    public List<McpEnvVarDefinition>? EnvVarDefinitions { get; set; }
}

/// <summary>
/// Definition of an environment variable for an MCP server.
/// </summary>
public class McpEnvVarDefinition
{
    /// <summary>
    /// Name of the environment variable.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Description of what the variable is used for.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether this variable is required.
    /// </summary>
    public bool IsRequired { get; set; }

    /// <summary>
    /// Whether this variable contains sensitive data.
    /// </summary>
    public bool IsSecret { get; set; }

    /// <summary>
    /// Default value if not provided.
    /// </summary>
    public string? Default { get; set; }
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
