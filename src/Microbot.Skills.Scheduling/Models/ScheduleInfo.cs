using Microbot.Skills.Scheduling.Database.Entities;

namespace Microbot.Skills.Scheduling.Models;

/// <summary>
/// Data transfer object for schedule information display.
/// </summary>
public record ScheduleInfo
{
    /// <summary>
    /// The schedule ID.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// The schedule name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// The command to execute.
    /// </summary>
    public string Command { get; init; } = string.Empty;

    /// <summary>
    /// Type of schedule (Recurring or Once).
    /// </summary>
    public ScheduleType Type { get; init; }

    /// <summary>
    /// The schedule expression (cron or original expression).
    /// </summary>
    public string Schedule { get; init; } = string.Empty;

    /// <summary>
    /// Optional description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Whether the schedule is enabled.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Whether the schedule has completed (for one-time schedules).
    /// </summary>
    public bool IsCompleted { get; init; }

    /// <summary>
    /// When the schedule was created.
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// When the schedule last ran.
    /// </summary>
    public DateTime? LastRunAt { get; init; }

    /// <summary>
    /// When the schedule will next run.
    /// </summary>
    public DateTime? NextRunAt { get; init; }

    /// <summary>
    /// Number of times the schedule has run.
    /// </summary>
    public int RunCount { get; init; }

    /// <summary>
    /// Gets the display status for the schedule.
    /// </summary>
    public string Status
    {
        get
        {
            if (Type == ScheduleType.Once)
            {
                return IsCompleted ? "Completed" : "Pending";
            }
            return Enabled ? "Enabled" : "Disabled";
        }
    }

    /// <summary>
    /// Creates a ScheduleInfo from a Schedule entity.
    /// </summary>
    public static ScheduleInfo FromEntity(Schedule schedule)
    {
        return new ScheduleInfo
        {
            Id = schedule.Id,
            Name = schedule.Name,
            Command = schedule.Command,
            Type = schedule.Type,
            Schedule = schedule.OriginalExpression ?? schedule.CronExpression ?? 
                       (schedule.RunAt.HasValue ? schedule.RunAt.Value.ToString("yyyy-MM-dd HH:mm") : ""),
            Description = schedule.Description,
            Enabled = schedule.Enabled,
            IsCompleted = schedule.IsCompleted,
            CreatedAt = schedule.CreatedAt,
            LastRunAt = schedule.LastRunAt,
            NextRunAt = schedule.NextRunAt,
            RunCount = schedule.RunCount
        };
    }
}
