using Microsoft.Extensions.Logging;
using Microbot.Skills.Scheduling.Database.Entities;

namespace Microbot.Skills.Scheduling.Services;

/// <summary>
/// Background service that executes scheduled tasks.
/// </summary>
public class ScheduleExecutorService : IDisposable
{
    private readonly IScheduleService _scheduleService;
    private readonly Func<string, CancellationToken, Task<string>> _executeCommand;
    private readonly ILogger<ScheduleExecutorService>? _logger;
    private readonly Timer _timer;
    private readonly TimeZoneInfo _timeZone;
    private readonly int _executionTimeoutSeconds;
    private readonly SemaphoreSlim _executionLock = new(1, 1);
    private bool _isRunning;
    private bool _disposed;

    /// <summary>
    /// Event raised when a schedule starts executing.
    /// </summary>
    public event EventHandler<ScheduleExecutionEventArgs>? ScheduleStarted;

    /// <summary>
    /// Event raised when a schedule completes execution.
    /// </summary>
    public event EventHandler<ScheduleExecutionEventArgs>? ScheduleCompleted;

    /// <summary>
    /// Event raised when a schedule execution fails.
    /// </summary>
    public event EventHandler<ScheduleExecutionEventArgs>? ScheduleFailed;

    /// <summary>
    /// Creates a new ScheduleExecutorService.
    /// </summary>
    /// <param name="scheduleService">The schedule service.</param>
    /// <param name="executeCommand">Function to execute a command and return the result.</param>
    /// <param name="checkIntervalSeconds">How often to check for due schedules (default: 60 seconds).</param>
    /// <param name="executionTimeoutSeconds">Maximum execution time per schedule (default: 600 seconds).</param>
    /// <param name="timeZone">Time zone for schedule calculations.</param>
    /// <param name="logger">Optional logger.</param>
    public ScheduleExecutorService(
        IScheduleService scheduleService,
        Func<string, CancellationToken, Task<string>> executeCommand,
        int checkIntervalSeconds = 60,
        int executionTimeoutSeconds = 600,
        TimeZoneInfo? timeZone = null,
        ILogger<ScheduleExecutorService>? logger = null)
    {
        _scheduleService = scheduleService;
        _executeCommand = executeCommand;
        _timeZone = timeZone ?? TimeZoneInfo.Local;
        _executionTimeoutSeconds = executionTimeoutSeconds;
        _logger = logger;

        // Start the timer
        _timer = new Timer(
            CheckSchedulesCallback,
            null,
            TimeSpan.FromSeconds(10), // Initial delay
            TimeSpan.FromSeconds(checkIntervalSeconds));

        _logger?.LogInformation("Schedule executor started with {Interval}s check interval", checkIntervalSeconds);
    }

    /// <summary>
    /// Manually triggers a check for due schedules.
    /// </summary>
    public async Task CheckSchedulesAsync(CancellationToken cancellationToken = default)
    {
        await CheckAndExecuteSchedulesAsync(cancellationToken);
    }

    /// <summary>
    /// Manually runs a specific schedule immediately.
    /// </summary>
    /// <param name="scheduleId">The schedule ID to run.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The execution result, or null if the schedule was not found.</returns>
    public async Task<string?> RunScheduleNowAsync(int scheduleId, CancellationToken cancellationToken = default)
    {
        var scheduleInfo = await _scheduleService.GetScheduleAsync(scheduleId, cancellationToken);
        if (scheduleInfo == null)
        {
            _logger?.LogWarning("Schedule {Id} not found for manual execution", scheduleId);
            return null;
        }

        // Create a temporary schedule object for execution
        var schedule = new Schedule
        {
            Id = scheduleInfo.Id,
            Name = scheduleInfo.Name,
            Command = scheduleInfo.Command,
            Type = scheduleInfo.Type
        };

        return await ExecuteScheduleAsync(schedule, cancellationToken);
    }

    private async void CheckSchedulesCallback(object? state)
    {
        if (_disposed) return;

        try
        {
            await CheckAndExecuteSchedulesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error in schedule check callback");
        }
    }

    private async Task CheckAndExecuteSchedulesAsync(CancellationToken cancellationToken)
    {
        // Prevent concurrent execution
        if (!await _executionLock.WaitAsync(0, cancellationToken))
        {
            _logger?.LogDebug("Schedule check skipped - previous check still running");
            return;
        }

        try
        {
            if (_isRunning) return;
            _isRunning = true;

            var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone);
            var dueSchedules = await _scheduleService.GetDueSchedulesAsync(now, cancellationToken);

            if (dueSchedules.Count > 0)
            {
                _logger?.LogInformation("Found {Count} due schedule(s)", dueSchedules.Count);
            }

            foreach (var schedule in dueSchedules)
            {
                if (cancellationToken.IsCancellationRequested) break;

                try
                {
                    await ExecuteScheduleAsync(schedule, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error executing schedule {Id}: {Name}", schedule.Id, schedule.Name);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error checking schedules");
        }
        finally
        {
            _isRunning = false;
            _executionLock.Release();
        }
    }

    private async Task<string?> ExecuteScheduleAsync(Schedule schedule, CancellationToken cancellationToken)
    {
        var execution = await _scheduleService.StartExecutionAsync(schedule.Id, cancellationToken);

        _logger?.LogInformation("Executing schedule {Id}: {Name}", schedule.Id, schedule.Name);

        ScheduleStarted?.Invoke(this, new ScheduleExecutionEventArgs(schedule.Id, schedule.Name, schedule.Command));

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(_executionTimeoutSeconds));

            var result = await _executeCommand(schedule.Command, cts.Token);

            await _scheduleService.CompleteExecutionAsync(execution.Id, result, cancellationToken);

            _logger?.LogInformation("Schedule {Id} completed successfully", schedule.Id);

            ScheduleCompleted?.Invoke(this, new ScheduleExecutionEventArgs(schedule.Id, schedule.Name, schedule.Command, result));

            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout
            await _scheduleService.FailExecutionAsync(execution.Id, ExecutionStatus.Timeout, "Execution timed out", cancellationToken);

            _logger?.LogWarning("Schedule {Id} timed out after {Timeout}s", schedule.Id, _executionTimeoutSeconds);

            ScheduleFailed?.Invoke(this, new ScheduleExecutionEventArgs(schedule.Id, schedule.Name, schedule.Command, null, "Execution timed out"));

            return null;
        }
        catch (Exception ex)
        {
            await _scheduleService.FailExecutionAsync(execution.Id, ExecutionStatus.Failed, ex.Message, cancellationToken);

            _logger?.LogError(ex, "Schedule {Id} failed", schedule.Id);

            ScheduleFailed?.Invoke(this, new ScheduleExecutionEventArgs(schedule.Id, schedule.Name, schedule.Command, null, ex.Message));

            return null;
        }
    }

    /// <summary>
    /// Stops the scheduler.
    /// </summary>
    public void Stop()
    {
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        _logger?.LogInformation("Schedule executor stopped");
    }

    /// <summary>
    /// Starts the scheduler.
    /// </summary>
    /// <param name="checkIntervalSeconds">Check interval in seconds.</param>
    public void Start(int checkIntervalSeconds = 60)
    {
        _timer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(checkIntervalSeconds));
        _logger?.LogInformation("Schedule executor started");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _timer.Dispose();
        _executionLock.Dispose();

        _logger?.LogInformation("Schedule executor disposed");
    }
}

/// <summary>
/// Event arguments for schedule execution events.
/// </summary>
public class ScheduleExecutionEventArgs : EventArgs
{
    /// <summary>
    /// The schedule ID.
    /// </summary>
    public int ScheduleId { get; }

    /// <summary>
    /// The schedule name.
    /// </summary>
    public string ScheduleName { get; }

    /// <summary>
    /// The command that was executed.
    /// </summary>
    public string Command { get; }

    /// <summary>
    /// The execution result (if successful).
    /// </summary>
    public string? Result { get; }

    /// <summary>
    /// The error message (if failed).
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// Creates a new ScheduleExecutionEventArgs.
    /// </summary>
    public ScheduleExecutionEventArgs(int scheduleId, string scheduleName, string command, string? result = null, string? errorMessage = null)
    {
        ScheduleId = scheduleId;
        ScheduleName = scheduleName;
        Command = command;
        Result = result;
        ErrorMessage = errorMessage;
    }
}
