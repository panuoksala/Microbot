namespace Microbot.Skills;

using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microbot.Core.Interfaces;
using Microbot.Core.Models;
using Microbot.Skills.Loaders;

/// <summary>
/// Manages the loading and registration of all skills (MCP and NuGet).
/// </summary>
public class SkillManager : IAsyncDisposable
{
    private readonly SkillsConfig _config;
    private readonly ILogger<SkillManager>? _logger;
    private readonly McpSkillLoader _mcpLoader;
    private readonly NuGetSkillLoader _nugetLoader;
    private readonly List<KernelPlugin> _loadedPlugins = [];
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
    public SkillManager(SkillsConfig config, ILoggerFactory? loggerFactory = null)
    {
        _config = config;
        _logger = loggerFactory?.CreateLogger<SkillManager>();
        _mcpLoader = new McpSkillLoader(config, loggerFactory?.CreateLogger<McpSkillLoader>());
        _nugetLoader = new NuGetSkillLoader(config, loggerFactory?.CreateLogger<NuGetSkillLoader>());
    }

    /// <summary>
    /// Loads all skills from both MCP servers and NuGet packages.
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
            _logger?.LogInformation("Loading MCP skills...");
            var mcpPlugins = await _mcpLoader.LoadSkillsAsync(cancellationToken);
            _loadedPlugins.AddRange(mcpPlugins);
            _logger?.LogInformation("Loaded {Count} MCP plugins", mcpPlugins.Count());
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error loading MCP skills");
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

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        await _mcpLoader.DisposeAsync();
        await _nugetLoader.DisposeAsync();

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
