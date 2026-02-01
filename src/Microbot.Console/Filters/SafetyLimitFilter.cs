using Microsoft.SemanticKernel;
using Microbot.Core.Events;
using System.Diagnostics;

namespace Microbot.Console.Filters;

/// <summary>
/// Filter that enforces safety limits on automatic function invocation.
/// Prevents runaway agent loops by limiting iterations and total function calls.
/// Inspired by OpenClaw's agent loop safety mechanisms.
/// </summary>
public class SafetyLimitFilter : IAutoFunctionInvocationFilter, IAgentLoopEvents
{
    private readonly int _maxIterations;
    private readonly int _maxTotalFunctionCalls;
    private int _totalFunctionCalls;
    private string _sessionId = string.Empty;
    private DateTime _loopStartedAt;
    private readonly Stopwatch _functionStopwatch = new();

    public SafetyLimitFilter(
        int maxIterations = 10,
        int maxTotalFunctionCalls = 50)
    {
        _maxIterations = maxIterations;
        _maxTotalFunctionCalls = maxTotalFunctionCalls;
    }

    #region IAgentLoopEvents Implementation

    public event EventHandler<AgentLoopStartedEventArgs>? LoopStarted;
    public event EventHandler<AgentFunctionInvokingEventArgs>? FunctionInvoking;
    public event EventHandler<AgentFunctionInvokedEventArgs>? FunctionInvoked;
    public event EventHandler<AgentLoopCompletedEventArgs>? LoopCompleted;
    public event EventHandler<AgentLoopErrorEventArgs>? LoopError;
    public event EventHandler<SafetyLimitReachedEventArgs>? SafetyLimitReached;

    #endregion

    /// <summary>
    /// Resets the filter state for a new request.
    /// Should be called at the start of each user message processing.
    /// </summary>
    /// <param name="sessionId">Unique identifier for this session/request.</param>
    /// <param name="userMessage">The user's message being processed.</param>
    public void StartNewRequest(string sessionId, string userMessage)
    {
        _totalFunctionCalls = 0;
        _sessionId = sessionId;
        _loopStartedAt = DateTime.UtcNow;

        LoopStarted?.Invoke(this, new AgentLoopStartedEventArgs(
            _sessionId,
            userMessage,
            _loopStartedAt));
    }

    /// <summary>
    /// Signals that the agent loop has completed successfully.
    /// </summary>
    /// <param name="response">The final response from the agent.</param>
    /// <param name="totalIterations">Total number of iterations completed.</param>
    public void CompleteRequest(string response, int totalIterations)
    {
        var completedAt = DateTime.UtcNow;
        var duration = completedAt - _loopStartedAt;

        LoopCompleted?.Invoke(this, new AgentLoopCompletedEventArgs(
            _sessionId,
            completedAt,
            duration,
            totalIterations,
            _totalFunctionCalls,
            response));
    }

    /// <summary>
    /// Signals that the agent loop has failed or was terminated.
    /// </summary>
    /// <param name="errorType">The type of error that occurred.</param>
    /// <param name="errorMessage">Description of the error.</param>
    /// <param name="iterationsCompleted">Number of iterations completed before the error.</param>
    public void FailRequest(AgentLoopErrorType errorType, string errorMessage, int iterationsCompleted)
    {
        LoopError?.Invoke(this, new AgentLoopErrorEventArgs(
            _sessionId,
            DateTime.UtcNow,
            errorType,
            errorMessage,
            iterationsCompleted,
            _totalFunctionCalls));
    }

    /// <summary>
    /// Gets the current total function call count.
    /// </summary>
    public int TotalFunctionCalls => _totalFunctionCalls;

    /// <summary>
    /// Gets the maximum allowed iterations.
    /// </summary>
    public int MaxIterations => _maxIterations;

    /// <summary>
    /// Gets the maximum allowed function calls.
    /// </summary>
    public int MaxTotalFunctionCalls => _maxTotalFunctionCalls;

    public async Task OnAutoFunctionInvocationAsync(
        AutoFunctionInvocationContext context,
        Func<AutoFunctionInvocationContext, Task> next)
    {
        var functionName = context.Function.Name;
        var pluginName = context.Function.PluginName ?? "Unknown";
        var fullFunctionName = $"{pluginName}.{functionName}";

        // Check iteration limit
        if (context.RequestSequenceIndex >= _maxIterations)
        {
            var message = $"Maximum iterations ({_maxIterations}) reached. Terminating agent loop.";
            
            SafetyLimitReached?.Invoke(this, new SafetyLimitReachedEventArgs(
                _sessionId,
                SafetyLimitType.MaxIterations,
                context.RequestSequenceIndex + 1,
                _maxIterations,
                message));

            FailRequest(
                AgentLoopErrorType.MaxIterationsReached,
                message,
                context.RequestSequenceIndex);

            context.Terminate = true;
            return;
        }

        // Check total function calls limit
        _totalFunctionCalls++;
        if (_totalFunctionCalls > _maxTotalFunctionCalls)
        {
            var message = $"Maximum function calls ({_maxTotalFunctionCalls}) reached. Terminating agent loop.";
            
            SafetyLimitReached?.Invoke(this, new SafetyLimitReachedEventArgs(
                _sessionId,
                SafetyLimitType.MaxFunctionCalls,
                _totalFunctionCalls,
                _maxTotalFunctionCalls,
                message));

            FailRequest(
                AgentLoopErrorType.MaxFunctionCallsReached,
                message,
                context.RequestSequenceIndex);

            context.Terminate = true;
            return;
        }

        // Emit function invoking event
        FunctionInvoking?.Invoke(this, new AgentFunctionInvokingEventArgs(
            _sessionId,
            functionName,
            pluginName,
            context.RequestSequenceIndex,
            context.FunctionSequenceIndex,
            context.FunctionCount,
            context.Arguments));

        // Execute the function and track timing
        _functionStopwatch.Restart();
        string? errorMessage = null;
        bool success = true;
        string? result = null;

        try
        {
            await next(context);
            result = context.Result?.ToString();
        }
        catch (Exception ex)
        {
            success = false;
            errorMessage = ex.Message;
            throw;
        }
        finally
        {
            _functionStopwatch.Stop();

            // Emit function invoked event
            FunctionInvoked?.Invoke(this, new AgentFunctionInvokedEventArgs(
                _sessionId,
                functionName,
                pluginName,
                _functionStopwatch.Elapsed,
                success,
                errorMessage,
                result));
        }
    }
}
