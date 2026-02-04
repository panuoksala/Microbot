using System.ComponentModel;
using System.Text;
using Microsoft.SemanticKernel;
using Microbot.Skills.Scheduling.Database.Entities;
using Microbot.Skills.Scheduling.Services;

namespace Microbot.Skills.Scheduling;

/// <summary>
/// Semantic Kernel skill for managing scheduled tasks.
/// </summary>
public class ScheduleSkill
{
    private readonly IScheduleService _scheduleService;
    private readonly TimeZoneInfo _timeZone;

    /// <summary>
    /// Creates a new ScheduleSkill.
    /// </summary>
    /// <param name="scheduleService">The schedule service.</param>
    /// <param name="timeZone">Time zone for display.</param>
    public ScheduleSkill(IScheduleService scheduleService, TimeZoneInfo? timeZone = null)
    {
        _scheduleService = scheduleService;
        _timeZone = timeZone ?? TimeZoneInfo.Local;
    }

    /// <summary>
    /// Lists all scheduled tasks.
    /// </summary>
    [KernelFunction("list_schedules")]
    [Description("List all scheduled tasks, both recurring and one-time. Returns a formatted list of schedules with their IDs, names, types, and next run times.")]
    public async Task<string> ListSchedulesAsync(
        [Description("Filter by status: 'all', 'enabled', 'disabled', 'pending' (one-time not yet run), 'completed' (one-time already run)")] string? status = "all",
        [Description("Filter by type: 'all', 'recurring', 'once'")] string? type = "all",
        CancellationToken cancellationToken = default)
    {
        var schedules = await _scheduleService.GetAllSchedulesAsync(includeCompleted: true, cancellationToken);

        // Apply filters
        var filtered = schedules.AsEnumerable();

        if (!string.IsNullOrEmpty(status) && status != "all")
        {
            filtered = status.ToLowerInvariant() switch
            {
                "enabled" => filtered.Where(s => s.Enabled && !s.IsCompleted),
                "disabled" => filtered.Where(s => !s.Enabled),
                "pending" => filtered.Where(s => s.Type == ScheduleType.Once && !s.IsCompleted),
                "completed" => filtered.Where(s => s.Type == ScheduleType.Once && s.IsCompleted),
                _ => filtered
            };
        }

        if (!string.IsNullOrEmpty(type) && type != "all")
        {
            filtered = type.ToLowerInvariant() switch
            {
                "recurring" => filtered.Where(s => s.Type == ScheduleType.Recurring),
                "once" or "onetime" or "one-time" => filtered.Where(s => s.Type == ScheduleType.Once),
                _ => filtered
            };
        }

        var list = filtered.ToList();

        if (list.Count == 0)
        {
            return "No scheduled tasks found matching the criteria.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Found {list.Count} scheduled task(s):\n");

        foreach (var schedule in list)
        {
            sb.AppendLine($"**ID {schedule.Id}**: {schedule.Name}");
            sb.AppendLine($"  - Type: {schedule.Type}");
            sb.AppendLine($"  - Schedule: {schedule.Schedule}");
            sb.AppendLine($"  - Status: {schedule.Status}");
            if (schedule.NextRunAt.HasValue)
            {
                sb.AppendLine($"  - Next Run: {schedule.NextRunAt:yyyy-MM-dd HH:mm}");
            }
            if (schedule.LastRunAt.HasValue)
            {
                sb.AppendLine($"  - Last Run: {schedule.LastRunAt:yyyy-MM-dd HH:mm}");
            }
            sb.AppendLine($"  - Run Count: {schedule.RunCount}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Creates a recurring scheduled task.
    /// </summary>
    [KernelFunction("create_recurring_schedule")]
    [Description("Create a recurring scheduled task that runs repeatedly at specified times. Use expressions like 'every day at 9am', 'every monday at 10:30', 'every hour', or cron expressions like '0 9 * * *'.")]
    public async Task<string> CreateRecurringScheduleAsync(
        [Description("Name for the schedule (e.g., 'Daily Email Summary', 'Weekly Report')")] string name,
        [Description("When to run repeatedly (e.g., 'every day at 9am', 'every monday at 10:30', 'every hour', or cron expression like '0 9 * * *')")] string schedule,
        [Description("The command/task to execute when the schedule runs")] string command,
        [Description("Optional description of what this schedule does")] string? description = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Ensure it's a recurring expression
            var expression = schedule.Trim();
            if (!expression.StartsWith("every ", StringComparison.OrdinalIgnoreCase) &&
                !IsLikelyCronExpression(expression))
            {
                expression = "every " + expression;
            }

            var created = await _scheduleService.CreateScheduleAsync(name, expression, command, description, cancellationToken);

            var sb = new StringBuilder();
            sb.AppendLine($"✅ Recurring schedule created successfully!");
            sb.AppendLine();
            sb.AppendLine($"**ID**: {created.Id}");
            sb.AppendLine($"**Name**: {created.Name}");
            sb.AppendLine($"**Type**: Recurring");
            sb.AppendLine($"**Schedule**: {created.Schedule}");
            if (created.NextRunAt.HasValue)
            {
                sb.AppendLine($"**Next Run**: {created.NextRunAt:yyyy-MM-dd HH:mm}");
            }
            sb.AppendLine();
            sb.AppendLine("You can manage this schedule using:");
            sb.AppendLine($"- `/schedule disable {created.Id}` - Temporarily disable");
            sb.AppendLine($"- `/schedule remove {created.Id}` - Remove completely");

            return sb.ToString();
        }
        catch (ArgumentException ex)
        {
            return $"❌ Failed to create schedule: {ex.Message}";
        }
    }

    /// <summary>
    /// Creates a one-time scheduled task.
    /// </summary>
    [KernelFunction("create_onetime_schedule")]
    [Description("Create a one-time scheduled task that runs once at a specific date/time. Use expressions like 'tomorrow at 9am', 'in 2 hours', '2024-02-15 14:00', 'on friday at 3pm'.")]
    public async Task<string> CreateOneTimeScheduleAsync(
        [Description("Name for the schedule (e.g., 'Call John', 'Deploy Release')")] string name,
        [Description("When to run once (e.g., 'tomorrow at 9am', 'in 2 hours', '2024-02-15 14:00', 'on friday at 3pm')")] string when,
        [Description("The command/task to execute when the schedule runs")] string command,
        [Description("Optional description of what this schedule does")] string? description = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Ensure it's a one-time expression
            var expression = when.Trim();
            if (!expression.StartsWith("once ", StringComparison.OrdinalIgnoreCase))
            {
                expression = "once " + expression;
            }

            var created = await _scheduleService.CreateScheduleAsync(name, expression, command, description, cancellationToken);

            var sb = new StringBuilder();
            sb.AppendLine($"✅ One-time schedule created successfully!");
            sb.AppendLine();
            sb.AppendLine($"**ID**: {created.Id}");
            sb.AppendLine($"**Name**: {created.Name}");
            sb.AppendLine($"**Type**: One-time");
            if (created.NextRunAt.HasValue)
            {
                sb.AppendLine($"**Scheduled For**: {created.NextRunAt:yyyy-MM-dd HH:mm}");

                // Calculate time until
                var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone);
                var timeUntil = created.NextRunAt.Value - now;
                if (timeUntil.TotalMinutes > 0)
                {
                    sb.AppendLine($"**Time Until**: {FormatTimeSpan(timeUntil)}");
                }
            }
            sb.AppendLine();
            sb.AppendLine("After it runs, the schedule will be marked as completed.");

            return sb.ToString();
        }
        catch (ArgumentException ex)
        {
            return $"❌ Failed to create schedule: {ex.Message}";
        }
    }

    /// <summary>
    /// Removes a scheduled task.
    /// </summary>
    [KernelFunction("remove_schedule")]
    [Description("Remove a scheduled task by its ID. This permanently deletes the schedule and its execution history.")]
    public async Task<string> RemoveScheduleAsync(
        [Description("The ID of the schedule to remove")] int scheduleId,
        CancellationToken cancellationToken = default)
    {
        var schedule = await _scheduleService.GetScheduleAsync(scheduleId, cancellationToken);
        if (schedule == null)
        {
            return $"❌ Schedule with ID {scheduleId} not found.";
        }

        var removed = await _scheduleService.RemoveScheduleAsync(scheduleId, cancellationToken);
        if (removed)
        {
            return $"✅ Schedule '{schedule.Name}' (ID: {scheduleId}) has been removed.";
        }

        return $"❌ Failed to remove schedule with ID {scheduleId}.";
    }

    /// <summary>
    /// Enables a disabled scheduled task.
    /// </summary>
    [KernelFunction("enable_schedule")]
    [Description("Enable a disabled scheduled task so it will run at its scheduled times.")]
    public async Task<string> EnableScheduleAsync(
        [Description("The ID of the schedule to enable")] int scheduleId,
        CancellationToken cancellationToken = default)
    {
        var schedule = await _scheduleService.EnableScheduleAsync(scheduleId, cancellationToken);
        if (schedule == null)
        {
            return $"❌ Schedule with ID {scheduleId} not found.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"✅ Schedule '{schedule.Name}' (ID: {scheduleId}) has been enabled.");
        if (schedule.NextRunAt.HasValue)
        {
            sb.AppendLine($"**Next Run**: {schedule.NextRunAt:yyyy-MM-dd HH:mm}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Disables a scheduled task.
    /// </summary>
    [KernelFunction("disable_schedule")]
    [Description("Disable a scheduled task without removing it. The schedule will not run until re-enabled.")]
    public async Task<string> DisableScheduleAsync(
        [Description("The ID of the schedule to disable")] int scheduleId,
        CancellationToken cancellationToken = default)
    {
        var schedule = await _scheduleService.DisableScheduleAsync(scheduleId, cancellationToken);
        if (schedule == null)
        {
            return $"❌ Schedule with ID {scheduleId} not found.";
        }

        return $"✅ Schedule '{schedule.Name}' (ID: {scheduleId}) has been disabled.";
    }

    /// <summary>
    /// Gets execution history for a scheduled task.
    /// </summary>
    [KernelFunction("get_schedule_history")]
    [Description("Get the execution history for a scheduled task, showing when it ran and whether it succeeded or failed.")]
    public async Task<string> GetScheduleHistoryAsync(
        [Description("The ID of the schedule")] int scheduleId,
        [Description("Maximum number of history entries to return")] int maxEntries = 10,
        CancellationToken cancellationToken = default)
    {
        var schedule = await _scheduleService.GetScheduleAsync(scheduleId, cancellationToken);
        if (schedule == null)
        {
            return $"❌ Schedule with ID {scheduleId} not found.";
        }

        var history = await _scheduleService.GetExecutionHistoryAsync(scheduleId, maxEntries, cancellationToken);

        if (history.Count == 0)
        {
            return $"No execution history found for schedule '{schedule.Name}' (ID: {scheduleId}).";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Execution history for '{schedule.Name}' (ID: {scheduleId}):");
        sb.AppendLine();

        foreach (var execution in history)
        {
            var statusIcon = execution.Status switch
            {
                ExecutionStatus.Completed => "✅",
                ExecutionStatus.Failed => "❌",
                ExecutionStatus.Timeout => "⏱️",
                ExecutionStatus.Running => "🔄",
                _ => "❓"
            };

            sb.AppendLine($"{statusIcon} **{execution.StartedAt:yyyy-MM-dd HH:mm}** - {execution.StatusDisplay}");
            if (execution.Duration.HasValue)
            {
                sb.AppendLine($"   Duration: {execution.Duration.Value.TotalSeconds:F1}s");
            }
            if (!string.IsNullOrEmpty(execution.ErrorMessage))
            {
                sb.AppendLine($"   Error: {execution.ErrorMessage}");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Gets details about a specific schedule.
    /// </summary>
    [KernelFunction("get_schedule")]
    [Description("Get detailed information about a specific scheduled task.")]
    public async Task<string> GetScheduleAsync(
        [Description("The ID of the schedule")] int scheduleId,
        CancellationToken cancellationToken = default)
    {
        var schedule = await _scheduleService.GetScheduleAsync(scheduleId, cancellationToken);
        if (schedule == null)
        {
            return $"❌ Schedule with ID {scheduleId} not found.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"**Schedule Details** (ID: {schedule.Id})");
        sb.AppendLine();
        sb.AppendLine($"**Name**: {schedule.Name}");
        sb.AppendLine($"**Type**: {schedule.Type}");
        sb.AppendLine($"**Schedule**: {schedule.Schedule}");
        sb.AppendLine($"**Status**: {schedule.Status}");
        sb.AppendLine($"**Command**: {schedule.Command}");
        if (!string.IsNullOrEmpty(schedule.Description))
        {
            sb.AppendLine($"**Description**: {schedule.Description}");
        }
        sb.AppendLine($"**Created**: {schedule.CreatedAt:yyyy-MM-dd HH:mm}");
        if (schedule.NextRunAt.HasValue)
        {
            sb.AppendLine($"**Next Run**: {schedule.NextRunAt:yyyy-MM-dd HH:mm}");
        }
        if (schedule.LastRunAt.HasValue)
        {
            sb.AppendLine($"**Last Run**: {schedule.LastRunAt:yyyy-MM-dd HH:mm}");
        }
        sb.AppendLine($"**Run Count**: {schedule.RunCount}");

        return sb.ToString();
    }

    private static bool IsLikelyCronExpression(string expression)
    {
        var parts = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 5 && parts.Length <= 6;
    }

    private static string FormatTimeSpan(TimeSpan timeSpan)
    {
        if (timeSpan.TotalDays >= 1)
        {
            return $"{(int)timeSpan.TotalDays} day(s), {timeSpan.Hours} hour(s)";
        }
        if (timeSpan.TotalHours >= 1)
        {
            return $"{(int)timeSpan.TotalHours} hour(s), {timeSpan.Minutes} minute(s)";
        }
        return $"{(int)timeSpan.TotalMinutes} minute(s)";
    }
}
