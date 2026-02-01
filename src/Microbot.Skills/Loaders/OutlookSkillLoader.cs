namespace Microbot.Skills.Loaders;

using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microbot.Core.Interfaces;
using Microbot.Core.Models;
using Microbot.Skills.Outlook;

/// <summary>
/// Loads the Outlook skill if configured and enabled.
/// </summary>
public class OutlookSkillLoader : ISkillLoader
{
    private readonly OutlookSkillConfig _config;
    private readonly ILogger<OutlookSkillLoader>? _logger;
    private readonly Action<string>? _deviceCodeCallback;

    /// <inheritdoc />
    public string LoaderName => "Outlook";

    /// <summary>
    /// Creates a new OutlookSkillLoader instance.
    /// </summary>
    /// <param name="config">Outlook skill configuration.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="deviceCodeCallback">Callback for device code authentication messages.</param>
    public OutlookSkillLoader(
        OutlookSkillConfig config,
        ILogger<OutlookSkillLoader>? logger = null,
        Action<string>? deviceCodeCallback = null)
    {
        _config = config;
        _logger = logger;
        _deviceCodeCallback = deviceCodeCallback;
    }

    /// <inheritdoc />
    public Task<IEnumerable<KernelPlugin>> LoadSkillsAsync(
        CancellationToken cancellationToken = default)
    {
        var plugins = new List<KernelPlugin>();

        if (!_config.Enabled)
        {
            _logger?.LogInformation("Outlook skill is disabled");
            return Task.FromResult<IEnumerable<KernelPlugin>>(plugins);
        }

        if (string.IsNullOrEmpty(_config.ClientId))
        {
            _logger?.LogWarning("Outlook skill is enabled but ClientId is not configured. " +
                "Please configure the ClientId in your Microbot.config file.");
            return Task.FromResult<IEnumerable<KernelPlugin>>(plugins);
        }

        try
        {
            _logger?.LogInformation("Loading Outlook skill in {Mode} mode", _config.Mode);

            var outlookSkill = new OutlookSkill(
                _config,
                _logger as ILogger<OutlookSkill>,
                _deviceCodeCallback);

            var plugin = KernelPluginFactory.CreateFromObject(outlookSkill, "Outlook");
            plugins.Add(plugin);

            _logger?.LogInformation(
                "Outlook skill loaded successfully with {Count} functions in {Mode} mode",
                plugin.Count(),
                _config.Mode);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load Outlook skill");
        }

        return Task.FromResult<IEnumerable<KernelPlugin>>(plugins);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
