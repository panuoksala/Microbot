using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microbot.Skills.Scheduling.Database;
using Microbot.Skills.Scheduling.Database.Entities;
using Microbot.Skills.Scheduling.Models;

namespace Microbot.Skills.Scheduling.Services;

/// <summary>
/// Service for managing schedules and their executions.
/// </summary>
public class ScheduleService : IScheduleService
{
    private readonly ScheduleDbContext _dbContext;
    private readonly TimeZoneInfo _timeZone;
    private readonly ILogger<ScheduleService>? _logger;

    /// <summary>
    /// Creates a new ScheduleService.
    /// </summary>
    /// <param name="dbContext">The database context.</param>
    /// <param name="timeZone">The time zone for schedule calculations.</param>
    /// <param name="logger">Optional logger.</param>
    public ScheduleService(ScheduleDbContext dbContext, TimeZoneInfo? timeZone = null, ILogger<ScheduleService>? logger = null)
    {
        _dbContext = dbContext;
        _timeZone = timeZone ?? TimeZoneInfo.Local;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ScheduleInfo>> GetAllSchedulesAsync(bool includeCompleted = true, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Schedules.AsQueryable();

        if (!includeCompleted)
        {
            // Exclude completed one-time schedules
            query = query.Where(s => s.Type != ScheduleType.Once || s.RunCount == 0);
        }

        var schedules = await query
            .OrderBy(s => s.NextRunAt ?? DateTime.MaxValue)
            .ThenBy(s => s.Name)
            .ToListAsync(cancellationToken);

        return schedules.Select(ScheduleInfo.FromEntity).ToList();
    }

    /// <inheritdoc />
    public async Task<ScheduleInfo?> GetScheduleAsync(int id, CancellationToken cancellationToken = default)
    {
        var schedule = await _dbContext.Schedules.FindAsync([id], cancellationToken);
        return schedule != null ? ScheduleInfo.FromEntity(schedule) : null;
    }

    /// <inheritdoc />
    public async Task<ScheduleInfo> CreateScheduleAsync(string name, string expression, string command, string? description = null, CancellationToken cancellationToken = default)
    {
        // Parse the expression
        var parsed = ScheduleExpressionParser.Parse(expression, _timeZone);

        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone);

        var schedule = new Schedule
        {
            Name = name,
            Command = command,
            Type = parsed.Type,
            CronExpression = parsed.CronExpression,
            OriginalExpression = parsed.OriginalExpression,
            RunAt = parsed.RunAt,
            Description = description,
            Enabled = true,
            CreatedAt = now,
            UpdatedAt = now,
            RunCount = 0
        };

        // Calculate next run time
        schedule.NextRunAt = ScheduleExpressionParser.GetNextOccurrence(schedule, now, _timeZone);

        _dbContext.Schedules.Add(schedule);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger?.LogInformation("Created schedule {Id}: {Name} ({Type})", schedule.Id, schedule.Name, schedule.Type);

        return ScheduleInfo.FromEntity(schedule);
    }

    /// <inheritdoc />
    public async Task<bool> RemoveScheduleAsync(int id, CancellationToken cancellationToken = default)
    {
        var schedule = await _dbContext.Schedules.FindAsync([id], cancellationToken);
        if (schedule == null)
        {
            return false;
        }

        _dbContext.Schedules.Remove(schedule);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger?.LogInformation("Removed schedule {Id}: {Name}", id, schedule.Name);

        return true;
    }

    /// <inheritdoc />
    public async Task<ScheduleInfo?> EnableScheduleAsync(int id, CancellationToken cancellationToken = default)
    {
        var schedule = await _dbContext.Schedules.FindAsync([id], cancellationToken);
        if (schedule == null)
        {
            return null;
        }

        schedule.Enabled = true;
        schedule.UpdatedAt = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone);

        // Recalculate next run time
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone);
        schedule.NextRunAt = ScheduleExpressionParser.GetNextOccurrence(schedule, now, _timeZone);

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger?.LogInformation("Enabled schedule {Id}: {Name}", id, schedule.Name);

        return ScheduleInfo.FromEntity(schedule);
    }

    /// <inheritdoc />
    public async Task<ScheduleInfo?> DisableScheduleAsync(int id, CancellationToken cancellationToken = default)
    {
        var schedule = await _dbContext.Schedules.FindAsync([id], cancellationToken);
        if (schedule == null)
        {
            return null;
        }

        schedule.Enabled = false;
        schedule.UpdatedAt = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone);
        schedule.NextRunAt = null;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger?.LogInformation("Disabled schedule {Id}: {Name}", id, schedule.Name);

        return ScheduleInfo.FromEntity(schedule);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Schedule>> GetDueSchedulesAsync(DateTime asOf, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Schedules
            .Where(s => s.Enabled && s.NextRunAt != null && s.NextRunAt <= asOf)
            .Where(s => s.Type != ScheduleType.Once || s.RunCount == 0) // One-time schedules only run once
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ScheduleExecution> StartExecutionAsync(int scheduleId, CancellationToken cancellationToken = default)
    {
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone);

        var execution = new ScheduleExecution
        {
            ScheduleId = scheduleId,
            StartedAt = now,
            Status = ExecutionStatus.Running
        };

        _dbContext.Executions.Add(execution);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger?.LogInformation("Started execution {Id} for schedule {ScheduleId}", execution.Id, scheduleId);

        return execution;
    }

    /// <inheritdoc />
    public async Task CompleteExecutionAsync(int executionId, string? result, CancellationToken cancellationToken = default)
    {
        var execution = await _dbContext.Executions
            .Include(e => e.Schedule)
            .FirstOrDefaultAsync(e => e.Id == executionId, cancellationToken);

        if (execution == null)
        {
            _logger?.LogWarning("Execution {Id} not found", executionId);
            return;
        }

        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone);

        execution.CompletedAt = now;
        execution.Status = ExecutionStatus.Completed;
        execution.Result = result;

        // Update schedule
        var schedule = execution.Schedule;
        schedule.LastRunAt = now;
        schedule.RunCount++;
        schedule.UpdatedAt = now;

        // Calculate next run time (null for completed one-time schedules)
        schedule.NextRunAt = ScheduleExpressionParser.GetNextOccurrence(schedule, now, _timeZone);

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger?.LogInformation("Completed execution {Id} for schedule {ScheduleId}", executionId, schedule.Id);
    }

    /// <inheritdoc />
    public async Task FailExecutionAsync(int executionId, ExecutionStatus status, string errorMessage, CancellationToken cancellationToken = default)
    {
        var execution = await _dbContext.Executions
            .Include(e => e.Schedule)
            .FirstOrDefaultAsync(e => e.Id == executionId, cancellationToken);

        if (execution == null)
        {
            _logger?.LogWarning("Execution {Id} not found", executionId);
            return;
        }

        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone);

        execution.CompletedAt = now;
        execution.Status = status;
        execution.ErrorMessage = errorMessage;

        // Update schedule (still count as a run for one-time schedules)
        var schedule = execution.Schedule;
        schedule.LastRunAt = now;
        schedule.RunCount++;
        schedule.UpdatedAt = now;

        // Calculate next run time
        schedule.NextRunAt = ScheduleExpressionParser.GetNextOccurrence(schedule, now, _timeZone);

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger?.LogWarning("Failed execution {Id} for schedule {ScheduleId}: {Error}", executionId, schedule.Id, errorMessage);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExecutionInfo>> GetExecutionHistoryAsync(int scheduleId, int maxEntries = 10, CancellationToken cancellationToken = default)
    {
        var executions = await _dbContext.Executions
            .Include(e => e.Schedule)
            .Where(e => e.ScheduleId == scheduleId)
            .OrderByDescending(e => e.StartedAt)
            .Take(maxEntries)
            .ToListAsync(cancellationToken);

        return executions.Select(ExecutionInfo.FromEntity).ToList();
    }

    /// <inheritdoc />
    public async Task UpdateNextRunTimeAsync(int scheduleId, CancellationToken cancellationToken = default)
    {
        var schedule = await _dbContext.Schedules.FindAsync([scheduleId], cancellationToken);
        if (schedule == null)
        {
            return;
        }

        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone);
        schedule.NextRunAt = ScheduleExpressionParser.GetNextOccurrence(schedule, now, _timeZone);
        schedule.UpdatedAt = now;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
