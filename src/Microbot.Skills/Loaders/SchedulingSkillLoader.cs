namespace Microbot.Skills.Loaders;

using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microbot.Core.Interfaces;
using Microbot.Core.Models;
using Microbot.Skills.Scheduling;
using Microbot.Skills.Scheduling.Database;
using Microbot.Skills.Scheduling.Services;

/// <summary>
/// Loads the Scheduling skill if configured and enabled.
/// </summary>
public class SchedulingSkillLoader : ISkillLoader
{
    private readonly SchedulingSkillConfig _config;
    private readonly ILogger<SchedulingSkillLoader>? _logger;
    private ScheduleDbContext? _dbContext;
    private ScheduleService? _scheduleService;
    private ScheduleExecutorService? _executorService;
    private ScheduleSkill? _scheduleSkill;

    /// <inheritdoc />
    public string LoaderName => "Scheduling";

    /// <summary>
    /// Gets the schedule service for external access (e.g., console commands).
    /// </summary>
    public IScheduleService? ScheduleService => _scheduleService;

    /// <summary>
    /// Gets the executor service for external access.
    /// </summary>
    public ScheduleExecutorService? ExecutorService => _executorService;

    /// <summary>
    /// Creates a new SchedulingSkillLoader instance.
    /// </summary>
    /// <param name="config">Scheduling skill configuration.</param>
    /// <param name="logger">Optional logger.</param>
    public SchedulingSkillLoader(
        SchedulingSkillConfig config,
        ILogger<SchedulingSkillLoader>? logger = null)
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
            _logger?.LogInformation("Scheduling skill is disabled");
            return plugins;
        }

        try
        {
            _logger?.LogInformation("Loading Scheduling skill with database at {Path}", _config.DatabasePath);

            // Initialize database
            _dbContext = new ScheduleDbContext(_config.DatabasePath);
            await _dbContext.InitializeAsync(cancellationToken);

            // Create schedule service
            _scheduleService = new ScheduleService(
                _dbContext,
                TimeZoneInfo.Local,
                _logger as ILogger<ScheduleService>);

            // Create the skill
            _scheduleSkill = new ScheduleSkill(_scheduleService, TimeZoneInfo.Local);

            var plugin = KernelPluginFactory.CreateFromObject(_scheduleSkill, "Scheduling");
            plugins.Add(plugin);

            _logger?.LogInformation(
                "Scheduling skill loaded successfully with {Count} functions",
                plugin.Count());
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load Scheduling skill");
        }

        return plugins;
    }

    /// <summary>
    /// Initializes the executor service with a command execution function.
    /// This should be called after the agent is ready to execute commands.
    /// </summary>
    /// <param name="executeCommand">Function to execute a command and return the result.</param>
    public void InitializeExecutor(Func<string, CancellationToken, Task<string>> executeCommand)
    {
        if (_scheduleService == null)
        {
            _logger?.LogWarning("Cannot initialize executor - schedule service not loaded");
            return;
        }

        _executorService = new ScheduleExecutorService(
            _scheduleService,
            executeCommand,
            _config.CheckIntervalSeconds,
            _config.ExecutionTimeoutSeconds,
            TimeZoneInfo.Local,
            _logger as ILogger<ScheduleExecutorService>);

        _logger?.LogInformation(
            "Schedule executor initialized with {Interval}s check interval",
            _config.CheckIntervalSeconds);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _executorService?.Dispose();
        
        if (_dbContext != null)
        {
            await _dbContext.DisposeAsync();
        }
    }
}
