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

    /// <summary>
    /// Agent loop configuration for safety mechanisms.
    /// </summary>
    public AgentLoopConfig AgentLoop { get; set; } = new();

    /// <summary>
    /// Long-term memory system configuration.
    /// </summary>
    public MemoryConfig Memory { get; set; } = new();
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

    /// <summary>
    /// Slack skill configuration.
    /// </summary>
    public SlackSkillConfig Slack { get; set; } = new();

    /// <summary>
    /// YouTrack skill configuration.
    /// </summary>
    public YouTrackSkillConfig YouTrack { get; set; } = new();

    /// <summary>
    /// Scheduling skill configuration.
    /// </summary>
    public SchedulingSkillConfig Scheduling { get; set; } = new();

    /// <summary>
    /// Browser skill configuration (Playwright MCP).
    /// </summary>
    public BrowserSkillConfig Browser { get; set; } = new();
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
/// Configuration for the Slack skill.
/// </summary>
public class SlackSkillConfig
{
    /// <summary>
    /// Whether the Slack skill is enabled.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// The permission mode for the Slack skill: "ReadOnly" or "Full".
    /// </summary>
    public string Mode { get; set; } = "ReadOnly";

    /// <summary>
    /// Slack Bot User OAuth Token (starts with xoxb-).
    /// </summary>
    public string? BotToken { get; set; }

    /// <summary>
    /// Optional: App-level token for Socket Mode (starts with xapp-).
    /// Not required for basic API access.
    /// </summary>
    public string? AppToken { get; set; }

    /// <summary>
    /// Path to store read state (last read timestamps).
    /// </summary>
    public string ReadStatePath { get; set; } = "./slack-read-state.json";
}

/// <summary>
/// Configuration for the YouTrack skill.
/// </summary>
public class YouTrackSkillConfig
{
    /// <summary>
    /// Whether the YouTrack skill is enabled.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// The permission mode for the YouTrack skill: "ReadOnly" or "FullControl".
    /// </summary>
    public string Mode { get; set; } = "ReadOnly";

    /// <summary>
    /// The YouTrack server base URL (e.g., "https://youtrack.example.com" or "https://example.youtrack.cloud").
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// The permanent token for authentication.
    /// Generate this in YouTrack: Profile -> Account Security -> Tokens -> New Token.
    /// </summary>
    public string? PermanentToken { get; set; }
}

/// <summary>
/// Configuration for the Scheduling skill.
/// </summary>
public class SchedulingSkillConfig
{
    /// <summary>
    /// Whether the Scheduling skill is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Path to the SQLite database file for schedules.
    /// </summary>
    public string DatabasePath { get; set; } = "./schedules.db";

    /// <summary>
    /// How often to check for due schedules (in seconds).
    /// </summary>
    public int CheckIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Maximum execution time for a scheduled task (in seconds).
    /// </summary>
    public int ExecutionTimeoutSeconds { get; set; } = 600;

    /// <summary>
    /// Maximum number of execution history entries to keep per schedule.
    /// </summary>
    public int MaxHistoryEntries { get; set; } = 100;

    /// <summary>
    /// Whether to run missed schedules on startup.
    /// </summary>
    public bool RunMissedOnStartup { get; set; } = false;
}

/// <summary>
/// Configuration for the built-in Browser skill using Playwright MCP.
/// </summary>
public class BrowserSkillConfig
{
    /// <summary>
    /// Whether the Browser skill is enabled.
    /// Default: true (enabled by default as a core feature)
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Browser to use: "chromium", "firefox", or "webkit".
    /// Default: "chromium"
    /// </summary>
    public string Browser { get; set; } = "chromium";

    /// <summary>
    /// Whether to run the browser in headless mode.
    /// Default: true (headless for server/automation scenarios)
    /// </summary>
    public bool Headless { get; set; } = true;

    /// <summary>
    /// Browser viewport width in pixels.
    /// Default: 1280
    /// </summary>
    public int ViewportWidth { get; set; } = 1280;

    /// <summary>
    /// Browser viewport height in pixels.
    /// Default: 720
    /// </summary>
    public int ViewportHeight { get; set; } = 720;

    /// <summary>
    /// Timeout for browser actions in milliseconds.
    /// Default: 30000 (30 seconds)
    /// </summary>
    public int ActionTimeoutMs { get; set; } = 30000;

    /// <summary>
    /// Timeout for navigation in milliseconds.
    /// Default: 60000 (60 seconds)
    /// </summary>
    public int NavigationTimeoutMs { get; set; } = 60000;

    /// <summary>
    /// Whether to use isolated browser sessions (no persistent profile).
    /// Default: true
    /// </summary>
    public bool Isolated { get; set; } = true;

    /// <summary>
    /// Path to user data directory for persistent browser profile.
    /// Only used when Isolated is false.
    /// </summary>
    public string? UserDataDir { get; set; }

    /// <summary>
    /// Optional capabilities to enable: "pdf", "vision", "testing", "tracing".
    /// Default: empty (core capabilities only)
    /// </summary>
    public List<string> Capabilities { get; set; } = [];

    /// <summary>
    /// Output directory for screenshots, PDFs, and other browser outputs.
    /// Default: "./browser-outputs"
    /// </summary>
    public string OutputDir { get; set; } = "./browser-outputs";

    /// <summary>
    /// Optional proxy server URL.
    /// </summary>
    public string? ProxyServer { get; set; }

    /// <summary>
    /// Origins to block (e.g., ad servers).
    /// </summary>
    public List<string> BlockedOrigins { get; set; } = [];

    /// <summary>
    /// Device to emulate (e.g., "iPhone 15", "Pixel 7").
    /// </summary>
    public string? Device { get; set; }
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

/// <summary>
/// Configuration for the agentic loop safety mechanisms.
/// Inspired by OpenClaw's agent loop architecture.
/// </summary>
public class AgentLoopConfig
{
    /// <summary>
    /// Maximum number of LLM request iterations per user message.
    /// Each iteration may include multiple function calls.
    /// Default: 10
    /// </summary>
    public int MaxIterations { get; set; } = 10;

    /// <summary>
    /// Maximum total function calls per user message across all iterations.
    /// Default: 50
    /// </summary>
    public int MaxTotalFunctionCalls { get; set; } = 50;

    /// <summary>
    /// Timeout in seconds for waiting on initial LLM response.
    /// Similar to OpenClaw's agent.wait timeout.
    /// Default: 30 seconds
    /// </summary>
    public int WaitTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Total runtime timeout in seconds for the entire agent execution.
    /// Similar to OpenClaw's agents.defaults.timeoutSeconds.
    /// Default: 600 seconds (10 minutes)
    /// </summary>
    public int RuntimeTimeoutSeconds { get; set; } = 600;

    /// <summary>
    /// Timeout in seconds for individual function calls.
    /// Default: 30
    /// </summary>
    public int FunctionTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Whether to allow concurrent function invocation when the model
    /// requests multiple functions in parallel.
    /// Default: false (sequential for safety)
    /// </summary>
    public bool AllowConcurrentInvocation { get; set; } = false;

    /// <summary>
    /// Whether to show function call progress in the console.
    /// Default: true
    /// </summary>
    public bool ShowFunctionCallProgress { get; set; } = true;

    /// <summary>
    /// Whether to enable lifecycle event emission for observability.
    /// Default: true
    /// </summary>
    public bool EnableLifecycleEvents { get; set; } = true;

    /// <summary>
    /// Whether to enable automatic compaction of chat history
    /// when it exceeds a certain length.
    /// Default: false
    /// </summary>
    public bool EnableAutoCompaction { get; set; } = false;

    /// <summary>
    /// Maximum chat history messages before compaction is triggered.
    /// Default: 100
    /// </summary>
    public int CompactionThreshold { get; set; } = 100;
}

/// <summary>
/// Configuration for the long-term memory system.
/// </summary>
public class MemoryConfig
{
    /// <summary>
    /// Whether the memory system is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Path to the SQLite database file.
    /// </summary>
    public string DatabasePath { get; set; } = "./memory/microbot-memory.db";

    /// <summary>
    /// Path to the memory folder for MEMORY.md files.
    /// </summary>
    public string MemoryFolder { get; set; } = "./memory";

    /// <summary>
    /// Path to store session transcripts.
    /// </summary>
    public string SessionsFolder { get; set; } = "./memory/sessions";

    /// <summary>
    /// Embedding provider configuration.
    /// </summary>
    public EmbeddingConfig Embedding { get; set; } = new();

    /// <summary>
    /// Chunking configuration.
    /// </summary>
    public ChunkingConfig Chunking { get; set; } = new();

    /// <summary>
    /// Search configuration.
    /// </summary>
    public MemorySearchConfig Search { get; set; } = new();

    /// <summary>
    /// Sync configuration.
    /// </summary>
    public MemorySyncConfig Sync { get; set; } = new();
}

/// <summary>
/// Configuration for embedding generation.
/// </summary>
public class EmbeddingConfig
{
    /// <summary>
    /// Embedding provider: "OpenAI", "AzureOpenAI", "Ollama".
    /// If not specified, uses the same provider as the AI provider.
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// Embedding model ID.
    /// </summary>
    public string ModelId { get; set; } = "text-embedding-3-small";

    /// <summary>
    /// Embedding dimensions (for models that support it).
    /// </summary>
    public int? Dimensions { get; set; }

    /// <summary>
    /// API endpoint (for Azure OpenAI or Ollama).
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// API key (if different from main AI provider).
    /// </summary>
    public string? ApiKey { get; set; }
}

/// <summary>
/// Configuration for text chunking.
/// </summary>
public class ChunkingConfig
{
    /// <summary>
    /// Maximum tokens per chunk.
    /// </summary>
    public int MaxTokens { get; set; } = 512;

    /// <summary>
    /// Overlap tokens between chunks.
    /// </summary>
    public int OverlapTokens { get; set; } = 50;

    /// <summary>
    /// Whether to use markdown-aware chunking.
    /// </summary>
    public bool MarkdownAware { get; set; } = true;
}

/// <summary>
/// Configuration for memory search.
/// </summary>
public class MemorySearchConfig
{
    /// <summary>
    /// Maximum results to return from search.
    /// </summary>
    public int MaxResults { get; set; } = 10;

    /// <summary>
    /// Minimum similarity score (0.0 to 1.0).
    /// </summary>
    public float MinScore { get; set; } = 0.5f;

    /// <summary>
    /// Weight for vector search results (0.0 to 1.0).
    /// </summary>
    public float VectorWeight { get; set; } = 0.7f;

    /// <summary>
    /// Weight for full-text search results (0.0 to 1.0).
    /// </summary>
    public float TextWeight { get; set; } = 0.3f;

    /// <summary>
    /// Whether to include session transcripts in search.
    /// </summary>
    public bool IncludeSessions { get; set; } = true;

    /// <summary>
    /// Whether to include memory files in search.
    /// </summary>
    public bool IncludeMemoryFiles { get; set; } = true;
}

/// <summary>
/// Configuration for memory synchronization.
/// </summary>
public class MemorySyncConfig
{
    /// <summary>
    /// Whether to enable file watching for automatic sync.
    /// </summary>
    public bool EnableFileWatching { get; set; } = true;

    /// <summary>
    /// Debounce interval in milliseconds for file changes.
    /// </summary>
    public int DebounceMs { get; set; } = 1000;

    /// <summary>
    /// Interval in seconds for periodic sync.
    /// </summary>
    public int SyncIntervalSeconds { get; set; } = 300;

    /// <summary>
    /// Whether to sync sessions automatically.
    /// </summary>
    public bool AutoSyncSessions { get; set; } = true;

    /// <summary>
    /// Minimum bytes changed before session sync.
    /// </summary>
    public int SessionSyncBytesThreshold { get; set; } = 1024;

    /// <summary>
    /// Minimum messages added before session sync.
    /// </summary>
    public int SessionSyncMessagesThreshold { get; set; } = 5;
}
