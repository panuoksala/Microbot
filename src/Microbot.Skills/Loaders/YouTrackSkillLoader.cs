namespace Microbot.Skills.Loaders;

using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microbot.Core.Interfaces;
using Microbot.Core.Models;
using Microbot.Skills.YouTrack;

/// <summary>
/// Loads the YouTrack skill if configured and enabled.
/// </summary>
public class YouTrackSkillLoader : ISkillLoader
{
    private readonly YouTrackSkillConfig _config;
    private readonly ILogger<YouTrackSkillLoader>? _logger;
    private YouTrackSkill? _youTrackSkill;

    /// <inheritdoc />
    public string LoaderName => "YouTrack";

    /// <summary>
    /// Creates a new YouTrackSkillLoader instance.
    /// </summary>
    /// <param name="config">YouTrack skill configuration.</param>
    /// <param name="logger">Optional logger.</param>
    public YouTrackSkillLoader(
        YouTrackSkillConfig config,
        ILogger<YouTrackSkillLoader>? logger = null)
    {
        _config = config;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<KernelPlugin>> LoadSkillsAsync(
        CancellationToken cancellationToken = default)
    {
        var plugins = new List<KernelPlugin>();

        if (!_config.Enabled)
        {
            _logger?.LogInformation("YouTrack skill is disabled");
            return plugins;
        }

        if (string.IsNullOrEmpty(_config.BaseUrl))
        {
            _logger?.LogWarning("YouTrack skill is enabled but BaseUrl is not configured. " +
                "Please configure the BaseUrl in your Microbot.config file.");
            return plugins;
        }

        if (string.IsNullOrEmpty(_config.PermanentToken))
        {
            _logger?.LogWarning("YouTrack skill is enabled but PermanentToken is not configured. " +
                "Please configure the PermanentToken in your Microbot.config file.");
            return plugins;
        }

        try
        {
            _logger?.LogInformation("Loading YouTrack skill in {Mode} mode", _config.Mode);

            _youTrackSkill = new YouTrackSkill(
                _config,
                _logger as ILogger<YouTrackSkill>);

            // Initialize the skill (verifies authentication)
            await _youTrackSkill.InitializeAsync(cancellationToken);

            var plugin = KernelPluginFactory.CreateFromObject(_youTrackSkill, "YouTrack");
            plugins.Add(plugin);

            _logger?.LogInformation(
                "YouTrack skill loaded successfully with {Count} functions in {Mode} mode",
                plugin.Count(),
                _config.Mode);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load YouTrack skill");
        }

        return plugins;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _youTrackSkill?.Dispose();
        return ValueTask.CompletedTask;
    }
}
