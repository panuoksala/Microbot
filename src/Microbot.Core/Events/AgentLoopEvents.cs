namespace Microbot.Core.Events;

/// <summary>
/// Events emitted during agent loop execution.
/// Inspired by OpenClaw's lifecycle event system.
/// </summary>
public interface IAgentLoopEvents
{
    /// <summary>
    /// Raised when agent loop starts processing a user message.
    /// </summary>
    event EventHandler<AgentLoopStartedEventArgs>? LoopStarted;

    /// <summary>
    /// Raised when a function is about to be invoked.
    /// </summary>
    event EventHandler<AgentFunctionInvokingEventArgs>? FunctionInvoking;

    /// <summary>
    /// Raised when a function has completed (success or failure).
    /// </summary>
    event EventHandler<AgentFunctionInvokedEventArgs>? FunctionInvoked;

    /// <summary>
    /// Raised when agent loop completes successfully.
    /// </summary>
    event EventHandler<AgentLoopCompletedEventArgs>? LoopCompleted;

    /// <summary>
    /// Raised when agent loop fails or is terminated.
    /// </summary>
    event EventHandler<AgentLoopErrorEventArgs>? LoopError;

    /// <summary>
    /// Raised when a safety limit is reached (iterations or function calls).
    /// </summary>
    event EventHandler<SafetyLimitReachedEventArgs>? SafetyLimitReached;
}

/// <summary>
/// Event args for when the agent loop starts.
/// </summary>
public record AgentLoopStartedEventArgs(
    string SessionId,
    string UserMessage,
    DateTime StartedAt);

/// <summary>
/// Event args for when a function is about to be invoked.
/// Named with "Agent" prefix to avoid conflict with Semantic Kernel's FunctionInvokingEventArgs.
/// </summary>
public record AgentFunctionInvokingEventArgs(
    string SessionId,
    string FunctionName,
    string PluginName,
    int IterationIndex,
    int FunctionIndex,
    int TotalFunctionsInIteration,
    object? Arguments);

/// <summary>
/// Event args for when a function has completed.
/// Named with "Agent" prefix to avoid conflict with Semantic Kernel's FunctionInvokedEventArgs.
/// </summary>
public record AgentFunctionInvokedEventArgs(
    string SessionId,
    string FunctionName,
    string PluginName,
    TimeSpan Duration,
    bool Success,
    string? ErrorMessage,
    string? Result);

/// <summary>
/// Event args for when the agent loop completes successfully.
/// </summary>
public record AgentLoopCompletedEventArgs(
    string SessionId,
    DateTime CompletedAt,
    TimeSpan TotalDuration,
    int TotalIterations,
    int TotalFunctionCalls,
    string Response);

/// <summary>
/// Event args for when the agent loop fails or is terminated.
/// </summary>
public record AgentLoopErrorEventArgs(
    string SessionId,
    DateTime ErrorAt,
    AgentLoopErrorType ErrorType,
    string ErrorMessage,
    int IterationsCompleted,
    int FunctionCallsCompleted);

/// <summary>
/// Event args for when a safety limit is reached.
/// </summary>
public record SafetyLimitReachedEventArgs(
    string SessionId,
    SafetyLimitType LimitType,
    int CurrentValue,
    int MaxValue,
    string Message);

/// <summary>
/// Event args for when rate limit is encountered and waiting begins.
/// </summary>
public record RateLimitWaitEventArgs(
    string SessionId,
    int WaitSeconds,
    int RetryAttempt,
    int MaxRetries,
    string Message);

/// <summary>
/// Types of errors that can occur in the agent loop.
/// </summary>
public enum AgentLoopErrorType
{
    /// <summary>
    /// Maximum iterations limit reached.
    /// </summary>
    MaxIterationsReached,

    /// <summary>
    /// Maximum function calls limit reached.
    /// </summary>
    MaxFunctionCallsReached,

    /// <summary>
    /// Request timeout exceeded.
    /// </summary>
    RequestTimeout,

    /// <summary>
    /// Function execution timeout.
    /// </summary>
    FunctionTimeout,

    /// <summary>
    /// User cancelled the operation.
    /// </summary>
    UserCancelled,

    /// <summary>
    /// Unexpected exception occurred.
    /// </summary>
    UnexpectedException,

    /// <summary>
    /// Rate limit exceeded and max retries exhausted.
    /// </summary>
    RateLimitExceeded
}

/// <summary>
/// Types of safety limits that can be reached.
/// </summary>
public enum SafetyLimitType
{
    /// <summary>
    /// Maximum iterations limit.
    /// </summary>
    MaxIterations,

    /// <summary>
    /// Maximum function calls limit.
    /// </summary>
    MaxFunctionCalls,

    /// <summary>
    /// Request timeout limit.
    /// </summary>
    RequestTimeout,

    /// <summary>
    /// Function timeout limit.
    /// </summary>
    FunctionTimeout
}
