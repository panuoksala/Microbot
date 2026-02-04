namespace Microbot.Skills.Scheduling.Database.Entities;

/// <summary>
/// Represents a single execution of a scheduled task.
/// </summary>
public class ScheduleExecution
{
    /// <summary>
    /// Auto-generated unique identifier for the execution.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// The ID of the schedule that was executed.
    /// </summary>
    public int ScheduleId { get; set; }

    /// <summary>
    /// When the execution started.
    /// </summary>
    public DateTime StartedAt { get; set; }

    /// <summary>
    /// When the execution completed (null if still running).
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// The status of the execution.
    /// </summary>
    public ExecutionStatus Status { get; set; }

    /// <summary>
    /// The result/output of the execution (if successful).
    /// </summary>
    public string? Result { get; set; }

    /// <summary>
    /// Error message if the execution failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Navigation property to the parent schedule.
    /// </summary>
    public Schedule Schedule { get; set; } = null!;
}

/// <summary>
/// Status of a schedule execution.
/// </summary>
public enum ExecutionStatus
{
    /// <summary>
    /// The execution is currently running.
    /// </summary>
    Running,

    /// <summary>
    /// The execution completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// The execution failed with an error.
    /// </summary>
    Failed,

    /// <summary>
    /// The execution timed out.
    /// </summary>
    Timeout
}
