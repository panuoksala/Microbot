namespace Microbot.Skills.Loaders;

using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microbot.Core.Interfaces;
using Microbot.Core.Models;
using Microbot.Skills.Slack;

/// <summary>
/// Loads the Slack skill if configured and enabled.
/// </summary>
public class SlackSkillLoader : ISkillLoader
{
    private readonly SlackSkillConfig _config;
    private readonly ILogger<SlackSkillLoader>? _logger;
    private SlackSkill? _slackSkill;

    /// <inheritdoc />
    public string LoaderName => "Slack";

    /// <summary>
    /// Creates a new SlackSkillLoader instance.
    /// </summary>
    /// <param name="config">Slack skill configuration.</param>
    /// <param name="logger">Optional logger.</param>
    public SlackSkillLoader(
        SlackSkillConfig config,
        ILogger<SlackSkillLoader>? logger = null)
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
            _logger?.LogInformation("Slack skill is disabled");
            return plugins;
        }

        if (string.IsNullOrEmpty(_config.BotToken))
        {
            _logger?.LogWarning("Slack skill is enabled but BotToken is not configured. " +
                "Please configure the BotToken in your Microbot.config file.");
            return plugins;
        }

        try
        {
            _logger?.LogInformation("Loading Slack skill in {Mode} mode", _config.Mode);

            _slackSkill = new SlackSkill(
                _config,
                _logger as ILogger<SlackSkill>);

            // Initialize the skill (loads read state and authenticates)
            await _slackSkill.InitializeAsync(cancellationToken);

            var plugin = KernelPluginFactory.CreateFromObject(_slackSkill, "Slack");
            plugins.Add(plugin);

            _logger?.LogInformation(
                "Slack skill loaded successfully with {Count} functions in {Mode} mode",
                plugin.Count(),
                _config.Mode);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load Slack skill");
        }

        return plugins;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
