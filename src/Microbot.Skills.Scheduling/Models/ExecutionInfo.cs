using Microbot.Skills.Scheduling.Database.Entities;

namespace Microbot.Skills.Scheduling.Models;

/// <summary>
/// Data transfer object for execution history display.
/// </summary>
public record ExecutionInfo
{
    /// <summary>
    /// The execution ID.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// The schedule ID.
    /// </summary>
    public int ScheduleId { get; init; }

    /// <summary>
    /// The schedule name.
    /// </summary>
    public string ScheduleName { get; init; } = string.Empty;

    /// <summary>
    /// When the execution started.
    /// </summary>
    public DateTime StartedAt { get; init; }

    /// <summary>
    /// When the execution completed.
    /// </summary>
    public DateTime? CompletedAt { get; init; }

    /// <summary>
    /// The execution status.
    /// </summary>
    public ExecutionStatus Status { get; init; }

    /// <summary>
    /// The result of the execution.
    /// </summary>
    public string? Result { get; init; }

    /// <summary>
    /// Error message if failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Duration of the execution.
    /// </summary>
    public TimeSpan? Duration => CompletedAt.HasValue 
        ? CompletedAt.Value - StartedAt 
        : null;

    /// <summary>
    /// Gets the display status string.
    /// </summary>
    public string StatusDisplay => Status switch
    {
        ExecutionStatus.Running => "Running",
        ExecutionStatus.Completed => "Success",
        ExecutionStatus.Failed => "Failed",
        ExecutionStatus.Timeout => "Timeout",
        _ => "Unknown"
    };

    /// <summary>
    /// Creates an ExecutionInfo from a ScheduleExecution entity.
    /// </summary>
    public static ExecutionInfo FromEntity(ScheduleExecution execution)
    {
        return new ExecutionInfo
        {
            Id = execution.Id,
            ScheduleId = execution.ScheduleId,
            ScheduleName = execution.Schedule?.Name ?? "",
            StartedAt = execution.StartedAt,
            CompletedAt = execution.CompletedAt,
            Status = execution.Status,
            Result = execution.Result,
            ErrorMessage = execution.ErrorMessage
        };
    }
}
