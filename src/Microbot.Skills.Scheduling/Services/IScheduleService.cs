using Microbot.Skills.Scheduling.Database.Entities;
using Microbot.Skills.Scheduling.Models;

namespace Microbot.Skills.Scheduling.Services;

/// <summary>
/// Interface for schedule management operations.
/// </summary>
public interface IScheduleService
{
    /// <summary>
    /// Gets all schedules.
    /// </summary>
    /// <param name="includeCompleted">Whether to include completed one-time schedules.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<ScheduleInfo>> GetAllSchedulesAsync(bool includeCompleted = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a schedule by ID.
    /// </summary>
    /// <param name="id">The schedule ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ScheduleInfo?> GetScheduleAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new schedule.
    /// </summary>
    /// <param name="name">The schedule name.</param>
    /// <param name="expression">The schedule expression (cron or natural language).</param>
    /// <param name="command">The command to execute.</param>
    /// <param name="description">Optional description.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created schedule info.</returns>
    Task<ScheduleInfo> CreateScheduleAsync(string name, string expression, string command, string? description = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a schedule by ID.
    /// </summary>
    /// <param name="id">The schedule ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the schedule was removed, false if not found.</returns>
    Task<bool> RemoveScheduleAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enables a schedule.
    /// </summary>
    /// <param name="id">The schedule ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated schedule info, or null if not found.</returns>
    Task<ScheduleInfo?> EnableScheduleAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disables a schedule.
    /// </summary>
    /// <param name="id">The schedule ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated schedule info, or null if not found.</returns>
    Task<ScheduleInfo?> DisableScheduleAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets schedules that are due to run.
    /// </summary>
    /// <param name="asOf">The time to check against.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<Schedule>> GetDueSchedulesAsync(DateTime asOf, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts an execution for a schedule.
    /// </summary>
    /// <param name="scheduleId">The schedule ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ScheduleExecution> StartExecutionAsync(int scheduleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes an execution successfully.
    /// </summary>
    /// <param name="executionId">The execution ID.</param>
    /// <param name="result">The execution result.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CompleteExecutionAsync(int executionId, string? result, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fails an execution.
    /// </summary>
    /// <param name="executionId">The execution ID.</param>
    /// <param name="status">The failure status.</param>
    /// <param name="errorMessage">The error message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task FailExecutionAsync(int executionId, ExecutionStatus status, string errorMessage, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets execution history for a schedule.
    /// </summary>
    /// <param name="scheduleId">The schedule ID.</param>
    /// <param name="maxEntries">Maximum number of entries to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<ExecutionInfo>> GetExecutionHistoryAsync(int scheduleId, int maxEntries = 10, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the next run time for a schedule.
    /// </summary>
    /// <param name="scheduleId">The schedule ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpdateNextRunTimeAsync(int scheduleId, CancellationToken cancellationToken = default);
}
