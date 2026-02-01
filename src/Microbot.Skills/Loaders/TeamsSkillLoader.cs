namespace Microbot.Skills.Loaders;

using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microbot.Core.Interfaces;
using Microbot.Core.Models;
using Microbot.Skills.Teams;

/// <summary>
/// Loads the Teams skill if configured and enabled.
/// </summary>
public class TeamsSkillLoader : ISkillLoader
{
    private readonly TeamsSkillConfig _config;
    private readonly ILogger<TeamsSkillLoader>? _logger;
    private readonly Action<string>? _deviceCodeCallback;
    private TeamsSkill? _teamsSkill;

    /// <inheritdoc />
    public string LoaderName => "Teams";

    /// <summary>
    /// Creates a new TeamsSkillLoader instance.
    /// </summary>
    /// <param name="config">Teams skill configuration.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="deviceCodeCallback">Callback for device code authentication messages.</param>
    public TeamsSkillLoader(
        TeamsSkillConfig config,
        ILogger<TeamsSkillLoader>? logger = null,
        Action<string>? deviceCodeCallback = null)
    {
        _config = config;
        _logger = logger;
        _deviceCodeCallback = deviceCodeCallback;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<KernelPlugin>> LoadSkillsAsync(
        CancellationToken cancellationToken = default)
    {
        var plugins = new List<KernelPlugin>();

        if (!_config.Enabled)
        {
            _logger?.LogInformation("Teams skill is disabled");
            return plugins;
        }

        if (string.IsNullOrEmpty(_config.ClientId))
        {
            _logger?.LogWarning("Teams skill is enabled but ClientId is not configured. " +
                "Please configure the ClientId in your Microbot.config file.");
            return plugins;
        }

        try
        {
            _logger?.LogInformation("Loading Teams skill in {Mode} mode with TenantId: {TenantId}", 
                _config.Mode, _config.TenantId);

            _teamsSkill = new TeamsSkill(
                _config,
                _logger as ILogger<TeamsSkill>);

            // Initialize the skill (loads read state and authenticates)
            await _teamsSkill.InitializeAsync(_deviceCodeCallback, cancellationToken);

            var plugin = KernelPluginFactory.CreateFromObject(_teamsSkill, "Teams");
            plugins.Add(plugin);

            _logger?.LogInformation(
                "Teams skill loaded successfully with {Count} functions in {Mode} mode",
                plugin.Count(),
                _config.Mode);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load Teams skill");
        }

        return plugins;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
