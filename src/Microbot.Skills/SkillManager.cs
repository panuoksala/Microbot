namespace Microbot.Skills;

using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microbot.Core.Interfaces;
using Microbot.Core.Models;
using Microbot.Skills.Loaders;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Manages the loading and registration of all skills (MCP, NuGet, and built-in skills like Outlook, Teams, Slack, YouTrack, and Browser).
/// </summary>
public class SkillManager : IAsyncDisposable
{
    private readonly SkillsConfig _config;
    private readonly ILogger<SkillManager>? _logger;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly McpSkillLoader _mcpLoader;
    private readonly NuGetSkillLoader _nugetLoader;
    private readonly OutlookSkillLoader? _outlookLoader;
    private readonly TeamsSkillLoader? _teamsLoader;
    private readonly SlackSkillLoader? _slackLoader;
    private readonly YouTrackSkillLoader? _youTrackLoader;
    private readonly BrowserSkillLoader? _browserLoader;
    private readonly List<KernelPlugin> _loadedPlugins = [];
    private readonly Action<string>? _deviceCodeCallback;
    private bool _disposed;

    /// <summary>
    /// Gets the list of loaded plugins.
    /// </summary>
    public IReadOnlyList<KernelPlugin> LoadedPlugins => _loadedPlugins.AsReadOnly();

    /// <summary>
    /// Creates a new SkillManager instance.
    /// </summary>
    /// <param name="config">Skills configuration.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <param name="deviceCodeCallback">Optional callback for device code authentication messages.</param>
    public SkillManager(
        SkillsConfig config,
        ILoggerFactory? loggerFactory = null,
        Action<string>? deviceCodeCallback = null)
    {
        _config = config;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory?.CreateLogger<SkillManager>();
        _deviceCodeCallback = deviceCodeCallback;

        // Initialize loaders
        _mcpLoader = new McpSkillLoader(config, loggerFactory?.CreateLogger<McpSkillLoader>());
        _nugetLoader = new NuGetSkillLoader(config, loggerFactory?.CreateLogger<NuGetSkillLoader>());

        // Initialize Outlook loader if configured
        if (config.Outlook?.Enabled == true)
        {
            _outlookLoader = new OutlookSkillLoader(
                config.Outlook,
                loggerFactory?.CreateLogger<OutlookSkillLoader>(),
                deviceCodeCallback);
        }

        // Initialize Teams loader if configured
        if (config.Teams?.Enabled == true)
        {
            _teamsLoader = new TeamsSkillLoader(
                config.Teams,
                loggerFactory?.CreateLogger<TeamsSkillLoader>(),
                deviceCodeCallback);
        }

        // Initialize Slack loader if configured
        if (config.Slack?.Enabled == true)
        {
            _slackLoader = new SlackSkillLoader(
                config.Slack,
                loggerFactory?.CreateLogger<SlackSkillLoader>());
        }

        // Initialize YouTrack loader if configured
        if (config.YouTrack?.Enabled == true)
        {
            _youTrackLoader = new YouTrackSkillLoader(
                config.YouTrack,
                loggerFactory?.CreateLogger<YouTrackSkillLoader>());
        }

        // Initialize Browser loader if configured (enabled by default)
        if (config.Browser?.Enabled == true)
        {
            _browserLoader = new BrowserSkillLoader(
                config.Browser,
                loggerFactory?.CreateLogger<BrowserSkillLoader>());
        }
    }

    /// <summary>
    /// Loads all skills from MCP servers, NuGet packages, and built-in skills.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A collection of all loaded plugins.</returns>
    public async Task<IEnumerable<KernelPlugin>> LoadAllSkillsAsync(
        CancellationToken cancellationToken = default)
    {
        _loadedPlugins.Clear();

        _logger?.LogInformation("Starting skill loading...");

        // Load MCP skills
        try
        {
            var enabledServers = _config.McpServers.Count(s => s.Enabled);
            _logger?.LogInformation("Loading MCP skills from {Count} configured servers...", enabledServers);
            Console.WriteLine($"[Skills] Loading MCP skills from {enabledServers} configured servers...");
            
            var mcpPlugins = await _mcpLoader.LoadSkillsAsync(cancellationToken);
            var mcpPluginsList = mcpPlugins.ToList();
            _loadedPlugins.AddRange(mcpPluginsList);
            _logger?.LogInformation("Loaded {Count} MCP plugins", mcpPluginsList.Count);
            Console.WriteLine($"[Skills] Loaded {mcpPluginsList.Count} MCP plugins");
            
            // Log each loaded plugin
            foreach (var plugin in mcpPluginsList)
            {
                _logger?.LogInformation("  - MCP Plugin: {PluginName} with {FunctionCount} functions",
                    plugin.Name, plugin.Count());
                Console.WriteLine($"[Skills]   - MCP Plugin: {plugin.Name} with {plugin.Count()} functions");
            }
            
            if (mcpPluginsList.Count == 0 && enabledServers > 0)
            {
                Console.WriteLine($"[Skills Warning] No MCP plugins loaded despite {enabledServers} enabled servers!");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error loading MCP skills: {Message}", ex.Message);
            Console.Error.WriteLine($"[Skills Error] Error loading MCP skills: {ex.Message}");
            Console.Error.WriteLine($"[Skills Error] Stack trace: {ex.StackTrace}");
        }

        // Load NuGet skills
        try
        {
            _logger?.LogInformation("Loading NuGet skills...");
            var nugetPlugins = await _nugetLoader.LoadSkillsAsync(cancellationToken);
            _loadedPlugins.AddRange(nugetPlugins);
            _logger?.LogInformation("Loaded {Count} NuGet plugins", nugetPlugins.Count());
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error loading NuGet skills");
        }

        // Load Outlook skill
        if (_outlookLoader != null)
        {
            try
            {
                _logger?.LogInformation("Loading Outlook skill...");
                var outlookPlugins = await _outlookLoader.LoadSkillsAsync(cancellationToken);
                _loadedPlugins.AddRange(outlookPlugins);
                _logger?.LogInformation("Loaded {Count} Outlook plugins", outlookPlugins.Count());
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error loading Outlook skill");
            }
        }

        // Load Teams skill
        if (_teamsLoader != null)
        {
            try
            {
                _logger?.LogInformation("Loading Teams skill...");
                var teamsPlugins = await _teamsLoader.LoadSkillsAsync(cancellationToken);
                _loadedPlugins.AddRange(teamsPlugins);
                _logger?.LogInformation("Loaded {Count} Teams plugins", teamsPlugins.Count());
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error loading Teams skill");
            }
        }

        // Load Slack skill
        if (_slackLoader != null)
        {
            try
            {
                _logger?.LogInformation("Loading Slack skill...");
                var slackPlugins = await _slackLoader.LoadSkillsAsync(cancellationToken);
                _loadedPlugins.AddRange(slackPlugins);
                _logger?.LogInformation("Loaded {Count} Slack plugins", slackPlugins.Count());
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error loading Slack skill");
            }
        }

        // Load YouTrack skill
        if (_youTrackLoader != null)
        {
            try
            {
                _logger?.LogInformation("Loading YouTrack skill...");
                var youTrackPlugins = await _youTrackLoader.LoadSkillsAsync(cancellationToken);
                _loadedPlugins.AddRange(youTrackPlugins);
                _logger?.LogInformation("Loaded {Count} YouTrack plugins", youTrackPlugins.Count());
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error loading YouTrack skill");
            }
        }

        // Load Browser skill (Playwright MCP)
        if (_browserLoader != null)
        {
            try
            {
                _logger?.LogInformation("Loading Browser skill (Playwright MCP)...");
                var browserPlugins = await _browserLoader.LoadSkillsAsync(cancellationToken);
                _loadedPlugins.AddRange(browserPlugins);
                _logger?.LogInformation("Loaded {Count} Browser plugins", browserPlugins.Count());
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error loading Browser skill");
            }
        }

        _logger?.LogInformation("Total plugins loaded: {Count}", _loadedPlugins.Count);

        return _loadedPlugins;
    }

    /// <summary>
    /// Registers all loaded plugins with the specified Kernel.
    /// </summary>
    /// <param name="kernel">The Semantic Kernel instance to register plugins with.</param>
    public void RegisterPluginsWithKernel(Kernel kernel)
    {
        foreach (var plugin in _loadedPlugins)
        {
            kernel.Plugins.Add(plugin);
            _logger?.LogDebug("Registered plugin: {PluginName}", plugin.Name);
        }

        _logger?.LogInformation("Registered {Count} plugins with kernel", _loadedPlugins.Count);
    }

    /// <summary>
    /// Gets a summary of all loaded skills.
    /// </summary>
    /// <returns>A list of skill summaries.</returns>
    public IEnumerable<SkillSummary> GetSkillSummaries()
    {
        return _loadedPlugins.Select(p => new SkillSummary
        {
            Name = p.Name,
            Description = p.Description ?? string.Empty,
            FunctionCount = p.Count()
        });
    }

    /// <summary>
    /// Gets a list of all available skills with their current status.
    /// </summary>
    /// <returns>List of available skills.</returns>
    public IEnumerable<AvailableSkill> GetAvailableSkills()
    {
        var skills = new List<AvailableSkill>();

        // Built-in: Outlook skill
        skills.Add(new AvailableSkill
        {
            Id = "outlook",
            Name = "Outlook",
            Description = "Microsoft Outlook integration for email and calendar via Microsoft Graph API",
            Type = SkillType.BuiltIn,
            IsEnabled = _config.Outlook?.Enabled ?? false,
            IsConfigured = !string.IsNullOrEmpty(_config.Outlook?.ClientId),
            ConfigurationSummary = _config.Outlook?.Enabled == true
                ? $"Mode: {_config.Outlook.Mode}, Auth: {_config.Outlook.AuthenticationMethod}"
                : _config.Outlook?.ClientId != null
                    ? $"Mode: {_config.Outlook?.Mode} (disabled)"
                    : null
        });

        // Built-in: Teams skill
        skills.Add(new AvailableSkill
        {
            Id = "teams",
            Name = "Teams",
            Description = "Microsoft Teams integration for channels, chats, and messages via Microsoft Graph API (multi-tenant support)",
            Type = SkillType.BuiltIn,
            IsEnabled = _config.Teams?.Enabled ?? false,
            IsConfigured = !string.IsNullOrEmpty(_config.Teams?.ClientId),
            ConfigurationSummary = _config.Teams?.Enabled == true
                ? $"Mode: {_config.Teams.Mode}, Auth: {_config.Teams.AuthenticationMethod}, Tenant: {_config.Teams.TenantId}"
                : _config.Teams?.ClientId != null
                    ? $"Mode: {_config.Teams?.Mode} (disabled)"
                    : null
        });

        // Built-in: Slack skill
        skills.Add(new AvailableSkill
        {
            Id = "slack",
            Name = "Slack",
            Description = "Slack integration for channels, direct messages, and messaging via Slack Web API",
            Type = SkillType.BuiltIn,
            IsEnabled = _config.Slack?.Enabled ?? false,
            IsConfigured = !string.IsNullOrEmpty(_config.Slack?.BotToken),
            ConfigurationSummary = _config.Slack?.Enabled == true
                ? $"Mode: {_config.Slack.Mode}"
                : _config.Slack?.BotToken != null
                    ? $"Mode: {_config.Slack?.Mode} (disabled)"
                    : null
        });

        // Built-in: YouTrack skill
        skills.Add(new AvailableSkill
        {
            Id = "youtrack",
            Name = "YouTrack",
            Description = "JetBrains YouTrack integration for issue tracking, project management, and comments",
            Type = SkillType.BuiltIn,
            IsEnabled = _config.YouTrack?.Enabled ?? false,
            IsConfigured = !string.IsNullOrEmpty(_config.YouTrack?.BaseUrl) && !string.IsNullOrEmpty(_config.YouTrack?.PermanentToken),
            ConfigurationSummary = _config.YouTrack?.Enabled == true
                ? $"Mode: {_config.YouTrack.Mode}, URL: {_config.YouTrack.BaseUrl}"
                : _config.YouTrack?.BaseUrl != null
                    ? $"Mode: {_config.YouTrack?.Mode} (disabled)"
                    : null
        });

        // Built-in: Browser skill (Playwright MCP)
        skills.Add(new AvailableSkill
        {
            Id = "browser",
            Name = "Browser",
            Description = "Web browser automation using Playwright MCP - navigate pages, click elements, fill forms, take screenshots (requires Node.js)",
            Type = SkillType.BuiltIn,
            IsEnabled = _config.Browser?.Enabled ?? true, // Enabled by default
            IsConfigured = true, // No external configuration required
            ConfigurationSummary = _config.Browser?.Enabled == true
                ? $"Browser: {_config.Browser.Browser}, Headless: {_config.Browser.Headless}, Viewport: {_config.Browser.ViewportWidth}x{_config.Browser.ViewportHeight}"
                : "Disabled"
        });

        return skills;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        await _mcpLoader.DisposeAsync();
        await _nugetLoader.DisposeAsync();

        if (_outlookLoader != null)
        {
            await _outlookLoader.DisposeAsync();
        }

        if (_teamsLoader != null)
        {
            await _teamsLoader.DisposeAsync();
        }

        if (_slackLoader != null)
        {
            await _slackLoader.DisposeAsync();
        }

        if (_youTrackLoader != null)
        {
            await _youTrackLoader.DisposeAsync();
        }

        if (_browserLoader != null)
        {
            await _browserLoader.DisposeAsync();
        }

        _loadedPlugins.Clear();
        _disposed = true;
    }
}

/// <summary>
/// Summary information about a loaded skill.
/// </summary>
public class SkillSummary
{
    /// <summary>
    /// The name of the skill/plugin.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The description of the skill/plugin.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// The number of functions in the skill/plugin.
    /// </summary>
    public int FunctionCount { get; set; }
}
