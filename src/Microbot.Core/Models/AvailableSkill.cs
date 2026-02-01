namespace Microbot.Core.Models;

/// <summary>
/// Represents an available skill that can be configured.
/// </summary>
public class AvailableSkill
{
    /// <summary>
    /// Unique identifier for the skill (e.g., "outlook", "mcp", "nuget").
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Display name for the skill.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Description of what the skill provides.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Whether the skill is currently enabled in the configuration.
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Whether the skill is properly configured (has all required settings).
    /// </summary>
    public bool IsConfigured { get; set; }

    /// <summary>
    /// Current configuration summary (e.g., "Mode: ReadOnly").
    /// </summary>
    public string? ConfigurationSummary { get; set; }

    /// <summary>
    /// The type of skill (BuiltIn, MCP, NuGet).
    /// </summary>
    public SkillType Type { get; set; }
}

/// <summary>
/// Types of skills supported by Microbot.
/// </summary>
public enum SkillType
{
    /// <summary>
    /// Built-in skills like Outlook.
    /// </summary>
    BuiltIn,

    /// <summary>
    /// MCP server-based skills.
    /// </summary>
    MCP,

    /// <summary>
    /// NuGet package-based skills.
    /// </summary>
    NuGet
}
