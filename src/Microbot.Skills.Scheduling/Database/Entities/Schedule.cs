namespace Microbot.Skills.Scheduling.Database.Entities;

/// <summary>
/// Represents a scheduled task in the database.
/// </summary>
public class Schedule
{
    /// <summary>
    /// Auto-generated unique identifier for the schedule.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// User-friendly name for the schedule.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The command/task to execute when the schedule runs.
    /// </summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>
    /// Type of schedule: Recurring or Once.
    /// </summary>
    public ScheduleType Type { get; set; } = ScheduleType.Recurring;

    /// <summary>
    /// Cron expression for recurring schedules. Null for one-time schedules.
    /// </summary>
    public string? CronExpression { get; set; }

    /// <summary>
    /// The original schedule expression as entered by the user.
    /// </summary>
    public string? OriginalExpression { get; set; }

    /// <summary>
    /// Specific date/time for one-time schedules. Null for recurring schedules.
    /// </summary>
    public DateTime? RunAt { get; set; }

    /// <summary>
    /// Optional description of what this schedule does.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether the schedule is enabled and will run.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// When the schedule was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the schedule was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// When the schedule last ran successfully.
    /// </summary>
    public DateTime? LastRunAt { get; set; }

    /// <summary>
    /// When the schedule is next due to run.
    /// </summary>
    public DateTime? NextRunAt { get; set; }

    /// <summary>
    /// Number of times this schedule has run.
    /// </summary>
    public int RunCount { get; set; }

    /// <summary>
    /// Collection of execution history records.
    /// </summary>
    public ICollection<ScheduleExecution> Executions { get; set; } = [];

    /// <summary>
    /// Returns true if this is a one-time schedule that has already run.
    /// </summary>
    public bool IsCompleted => Type == ScheduleType.Once && RunCount > 0;
}

/// <summary>
/// Type of schedule.
/// </summary>
public enum ScheduleType
{
    /// <summary>
    /// Recurring schedule using cron expression. Runs repeatedly.
    /// </summary>
    Recurring,

    /// <summary>
    /// One-time schedule that runs once at a specific date/time.
    /// </summary>
    Once
}
