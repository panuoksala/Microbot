using Microsoft.SemanticKernel;
using Microbot.Core.Events;

namespace Microbot.Console.Filters;

/// <summary>
/// Filter that enforces timeout on individual function calls.
/// Prevents functions from hanging indefinitely.
/// Inspired by OpenClaw's timeout architecture.
/// </summary>
public class TimeoutFilter : IAutoFunctionInvocationFilter
{
    private readonly TimeSpan _functionTimeout;

    /// <summary>
    /// Event raised when a function times out.
    /// </summary>
    public event EventHandler<FunctionTimeoutEventArgs>? FunctionTimedOut;

    public TimeoutFilter(TimeSpan functionTimeout)
    {
        _functionTimeout = functionTimeout;
    }

    public TimeoutFilter(int functionTimeoutSeconds)
        : this(TimeSpan.FromSeconds(functionTimeoutSeconds))
    {
    }

    /// <summary>
    /// Gets the configured function timeout.
    /// </summary>
    public TimeSpan FunctionTimeout => _functionTimeout;

    public async Task OnAutoFunctionInvocationAsync(
        AutoFunctionInvocationContext context,
        Func<AutoFunctionInvocationContext, Task> next)
    {
        var functionName = context.Function.Name;
        var pluginName = context.Function.PluginName ?? "Unknown";
        var fullFunctionName = $"{pluginName}.{functionName}";

        using var cts = new CancellationTokenSource(_functionTimeout);

        try
        {
            // Create a task that completes when the function finishes or times out
            var functionTask = next(context);
            var timeoutTask = Task.Delay(_functionTimeout, cts.Token);

            var completedTask = await Task.WhenAny(functionTask, timeoutTask);

            if (completedTask == timeoutTask)
            {
                // Function timed out
                var message = $"Function {fullFunctionName} timed out after {_functionTimeout.TotalSeconds}s";

                FunctionTimedOut?.Invoke(this, new FunctionTimeoutEventArgs(
                    fullFunctionName,
                    _functionTimeout,
                    message));

                // Set a timeout error as the result so the LLM knows what happened
                context.Result = new FunctionResult(
                    context.Function,
                    $"Function timed out after {_functionTimeout.TotalSeconds} seconds. " +
                    "The operation took too long to complete. Consider breaking it into smaller steps.");
            }
            else
            {
                // Function completed - propagate any exceptions
                await functionTask;
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Timeout occurred via cancellation
            var message = $"Function {fullFunctionName} was cancelled due to timeout after {_functionTimeout.TotalSeconds}s";

            FunctionTimedOut?.Invoke(this, new FunctionTimeoutEventArgs(
                fullFunctionName,
                _functionTimeout,
                message));

            context.Result = new FunctionResult(
                context.Function,
                $"Function was cancelled due to timeout after {_functionTimeout.TotalSeconds} seconds.");
        }
    }
}

/// <summary>
/// Event args for when a function times out.
/// </summary>
public record FunctionTimeoutEventArgs(
    string FunctionName,
    TimeSpan Timeout,
    string Message);
