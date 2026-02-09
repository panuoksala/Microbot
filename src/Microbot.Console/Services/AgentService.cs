namespace Microbot.Console.Services;

using System.ClientModel;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microbot.Console.Filters;
using Microbot.Core.Events;
using Microbot.Core.Models;
using Microbot.Memory;
using Microbot.Memory.Interfaces;
using Microbot.Memory.Sessions;
using Microbot.Memory.Skills;
using Microbot.Memory.Sync;
using Microbot.Skills;
using Microbot.Skills.Scheduling;
using Microbot.Skills.Scheduling.Database;
using Microbot.Skills.Scheduling.Services;
using AgentResponseItem = Microsoft.SemanticKernel.Agents.AgentResponseItem<Microsoft.SemanticKernel.StreamingChatMessageContent>;

/// <summary>
/// Service for managing the AI agent using Semantic Kernel.
/// Implements agentic loop with safety mechanisms inspired by OpenClaw.
/// </summary>
public class AgentService : IAsyncDisposable
{
    private readonly MicrobotConfig _config;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly ILogger<AgentService>? _logger;
    private readonly Action<string>? _deviceCodeCallback;
    private Kernel? _kernel;
    private ChatCompletionAgent? _agent;
    private ChatHistory _chatHistory = [];
    private SkillManager? _skillManager;
    private SafetyLimitFilter? _safetyFilter;
    private TimeoutFilter? _timeoutFilter;
    private MemoryManager? _memoryManager;
    private MemorySyncService? _memorySyncService;
    private ScheduleDbContext? _scheduleDbContext;
    private IScheduleService? _scheduleService;
    private ScheduleExecutorService? _scheduleExecutorService;
    private SessionTranscript? _currentSession;
    private string? _currentSessionKey;
    private bool _disposed;
    private int _requestCounter;

    /// <summary>
    /// Gets the current chat history.
    /// </summary>
    public ChatHistory ChatHistory => _chatHistory;

    /// <summary>
    /// Gets the skill manager.
    /// </summary>
    public SkillManager? SkillManager => _skillManager;

    /// <summary>
    /// Gets the memory manager.
    /// </summary>
    public IMemoryManager? MemoryManager => _memoryManager;

    /// <summary>
    /// Gets the schedule service for managing scheduled tasks.
    /// </summary>
    public IScheduleService? ScheduleService => _scheduleService;

    /// <summary>
    /// Gets the safety filter for subscribing to lifecycle events.
    /// </summary>
    public IAgentLoopEvents? AgentLoopEvents => _safetyFilter;

    /// <summary>
    /// Event raised when a function call is about to be made.
    /// </summary>
    public event EventHandler<AgentFunctionInvokingEventArgs>? FunctionInvoking;

    /// <summary>
    /// Event raised when a function call has completed.
    /// </summary>
    public event EventHandler<AgentFunctionInvokedEventArgs>? FunctionInvoked;

    /// <summary>
    /// Event raised when a safety limit is reached.
    /// </summary>
    public event EventHandler<SafetyLimitReachedEventArgs>? SafetyLimitReached;

    /// <summary>
    /// Event raised when a function times out.
    /// </summary>
    public event EventHandler<FunctionTimeoutEventArgs>? FunctionTimedOut;

    /// <summary>
    /// Event raised when rate limit is encountered and waiting begins.
    /// </summary>
    public event EventHandler<RateLimitWaitEventArgs>? RateLimitWaiting;

    /// <summary>
    /// Creates a new AgentService instance.
    /// </summary>
    /// <param name="config">The Microbot configuration.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <param name="deviceCodeCallback">Optional callback for device code authentication messages (used by Outlook skill).</param>
    public AgentService(
        MicrobotConfig config,
        ILoggerFactory? loggerFactory = null,
        Action<string>? deviceCodeCallback = null)
    {
        _config = config;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory?.CreateLogger<AgentService>();
        _deviceCodeCallback = deviceCodeCallback;
    }

    /// <summary>
    /// Initializes the agent with the configured AI provider and skills.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Initializing agent...");

        // Build the kernel with the configured AI provider
        var builder = Kernel.CreateBuilder();

        // Add logging if available
        if (_loggerFactory != null)
        {
            builder.Services.AddSingleton(_loggerFactory);
        }

        // Configure the AI provider
        ConfigureAiProvider(builder);

        _kernel = builder.Build();

        // Create and register safety filters
        var agentLoopConfig = _config.AgentLoop;
        
        _safetyFilter = new SafetyLimitFilter(
            maxIterations: agentLoopConfig.MaxIterations,
            maxTotalFunctionCalls: agentLoopConfig.MaxTotalFunctionCalls);

        _timeoutFilter = new TimeoutFilter(agentLoopConfig.FunctionTimeoutSeconds);

        // Wire up events from filters to this service
        _safetyFilter.FunctionInvoking += (s, e) => FunctionInvoking?.Invoke(this, e);
        _safetyFilter.FunctionInvoked += (s, e) => FunctionInvoked?.Invoke(this, e);
        _safetyFilter.SafetyLimitReached += (s, e) => SafetyLimitReached?.Invoke(this, e);
        _timeoutFilter.FunctionTimedOut += (s, e) => FunctionTimedOut?.Invoke(this, e);

        // Register filters with kernel
        _kernel.AutoFunctionInvocationFilters.Add(_safetyFilter);
        _kernel.AutoFunctionInvocationFilters.Add(_timeoutFilter);

        _logger?.LogInformation(
            "Safety filters registered: MaxIterations={MaxIterations}, MaxFunctionCalls={MaxFunctionCalls}, FunctionTimeout={FunctionTimeout}s, RuntimeTimeout={RuntimeTimeout}s",
            agentLoopConfig.MaxIterations,
            agentLoopConfig.MaxTotalFunctionCalls,
            agentLoopConfig.FunctionTimeoutSeconds,
            agentLoopConfig.RuntimeTimeoutSeconds);

        // Load and register skills
        _skillManager = new SkillManager(_config.Skills, _loggerFactory, _deviceCodeCallback);
        var plugins = await _skillManager.LoadAllSkillsAsync(cancellationToken);
        _skillManager.RegisterPluginsWithKernel(_kernel);

        // Initialize memory system if enabled
        await InitializeMemoryAsync(cancellationToken);

        // Initialize scheduling system if enabled
        await InitializeSchedulingAsync(cancellationToken);

        // Configure function choice behavior options
        var functionChoiceOptions = new FunctionChoiceBehaviorOptions
        {
            AllowConcurrentInvocation = agentLoopConfig.AllowConcurrentInvocation
        };

        // Create the chat completion agent
        _agent = new ChatCompletionAgent
        {
            Name = _config.Preferences.AgentName,
            Instructions = GetSystemPrompt(),
            Kernel = _kernel,
            Arguments = new KernelArguments(
                new OpenAIPromptExecutionSettings
                {
                    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(options: functionChoiceOptions)
                })
        };

        _logger?.LogInformation("Agent initialized successfully with agentic loop safety mechanisms");
    }

    /// <summary>
    /// Configures the AI provider based on the configuration.
    /// </summary>
    private void ConfigureAiProvider(IKernelBuilder builder)
    {
        var provider = _config.AiProvider;

        switch (provider.Provider.ToLowerInvariant())
        {
            case "openai":
                builder.AddOpenAIChatCompletion(
                    modelId: provider.ModelId,
                    apiKey: provider.ApiKey ?? throw new InvalidOperationException("OpenAI API key is required."));
                break;

            case "azure":
            case "azureopenai":
                if (string.IsNullOrEmpty(provider.Endpoint))
                {
                    throw new InvalidOperationException("Azure OpenAI requires an endpoint.");
                }
                builder.AddAzureOpenAIChatCompletion(
                    deploymentName: provider.ModelId,
                    endpoint: provider.Endpoint,
                    apiKey: provider.ApiKey ?? throw new InvalidOperationException("Azure OpenAI API key is required."));
                break;

            case "anthropic":
            case "claude":
                // For Anthropic/Claude, we use our custom IChatCompletionService implementation
                var anthropicService = new AnthropicChatCompletionService(
                    apiKey: provider.ApiKey ?? throw new InvalidOperationException("Anthropic API key is required."),
                    modelId: provider.ModelId,
                    maxTokens: provider.MaxTokens);
                builder.Services.AddSingleton<IChatCompletionService>(anthropicService);
                break;

            case "ollama":
                // For Ollama, we use the OpenAI-compatible API with custom endpoint
                var endpoint = provider.Endpoint ?? "http://localhost:11434/v1";
                builder.AddOpenAIChatCompletion(
                    modelId: provider.ModelId,
                    apiKey: "ollama", // Ollama doesn't require a real API key
                    endpoint: new Uri(endpoint));
                break;

            default:
                throw new InvalidOperationException($"Unsupported AI provider: {provider.Provider}");
        }
    }

    /// <summary>
    /// Gets the last memory initialization error, if any.
    /// </summary>
    public string? MemoryInitializationError { get; private set; }

    /// <summary>
    /// Initializes the memory system if enabled in configuration.
    /// </summary>
    private async Task InitializeMemoryAsync(CancellationToken cancellationToken)
    {
        var memoryConfig = _config.Memory;
        MemoryInitializationError = null;
        
        if (!memoryConfig.Enabled)
        {
            _logger?.LogInformation("Memory system is disabled");
            return;
        }

        _logger?.LogInformation("Initializing memory system...");

        try
        {
            // Create memory manager using factory
            _memoryManager = MemoryManagerFactory.CreateFromMicrobotConfig(
                _config,
                _loggerFactory);

            // Initialize the memory manager (creates database, runs migrations)
            await _memoryManager.InitializeAsync(cancellationToken);

            // Register memory skill with kernel
            if (_kernel != null)
            {
                var memorySkill = new MemorySkill(_memoryManager);
                _kernel.ImportPluginFromObject(memorySkill, "Memory");
                _logger?.LogInformation("Memory skill registered with kernel");
            }

            // Start memory sync service if file watching is enabled
            if (memoryConfig.Sync.EnableFileWatching)
            {
                _memorySyncService = new MemorySyncService(
                    _memoryManager,
                    _loggerFactory?.CreateLogger<MemorySyncService>());
                
                // Watch the memory folder and sessions folder
                _memorySyncService.DebounceDelayMs = memoryConfig.Sync.DebounceMs;
                _memorySyncService.StartWatching(memoryConfig.MemoryFolder, memoryConfig.SessionsFolder);
                _logger?.LogInformation("Memory sync service started watching: {MemoryFolder}, {SessionsFolder}",
                    memoryConfig.MemoryFolder, memoryConfig.SessionsFolder);
            }

            // Start a new session
            StartNewSession();

            _logger?.LogInformation("Memory system initialized successfully");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to initialize memory system. Memory features will be disabled.");
            MemoryInitializationError = ex.Message;
            _memoryManager = null;
            _memorySyncService = null;
        }
    }

    /// <summary>
    /// Initializes the scheduling system if enabled in configuration.
    /// </summary>
    private async Task InitializeSchedulingAsync(CancellationToken cancellationToken)
    {
        var schedulingConfig = _config.Skills.Scheduling;
        
        if (schedulingConfig?.Enabled != true)
        {
            _logger?.LogInformation("Scheduling system is disabled");
            return;
        }

        _logger?.LogInformation("Initializing scheduling system...");

        try
        {
            // Ensure the database directory exists
            var dbPath = schedulingConfig.DatabasePath ?? "./schedules/schedules.db";
            var dbDir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dbDir) && !Directory.Exists(dbDir))
            {
                Directory.CreateDirectory(dbDir);
            }

            // Create database context
            _scheduleDbContext = new ScheduleDbContext(dbPath);
            
            // Initialize the database (creates tables)
            await _scheduleDbContext.InitializeAsync(cancellationToken);

            // Create schedule service
            _scheduleService = new ScheduleService(
                _scheduleDbContext,
                TimeZoneInfo.Local,
                _loggerFactory?.CreateLogger<ScheduleService>());

            // Register schedule skill with kernel
            if (_kernel != null)
            {
                var scheduleSkill = new ScheduleSkill(_scheduleService);
                _kernel.ImportPluginFromObject(scheduleSkill, "Scheduling");
                _logger?.LogInformation("Scheduling skill registered with kernel");
            }

            // Create and start the executor service
            var checkIntervalSeconds = schedulingConfig.CheckIntervalSeconds;
            var executionTimeoutSeconds = _config.AgentLoop.RuntimeTimeoutSeconds;
            _scheduleExecutorService = new ScheduleExecutorService(
                _scheduleService,
                ExecuteScheduledCommandAsync,
                checkIntervalSeconds,
                executionTimeoutSeconds,
                TimeZoneInfo.Local,
                _loggerFactory?.CreateLogger<ScheduleExecutorService>());

            _logger?.LogInformation("Schedule executor service started with {Interval}s check interval",
                schedulingConfig.CheckIntervalSeconds);

            _logger?.LogInformation("Scheduling system initialized successfully");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to initialize scheduling system. Scheduling features will be disabled.");
            _scheduleDbContext?.Dispose();
            _scheduleDbContext = null;
            _scheduleService = null;
            _scheduleExecutorService = null;
        }
    }

    /// <summary>
    /// Executes a scheduled command through the agent.
    /// </summary>
    private async Task<string> ExecuteScheduledCommandAsync(string command, CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Executing scheduled command: {Command}", command);
        
        try
        {
            // Execute the command through the agent
            var response = await ChatAsync(command, cancellationToken);
            return response;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to execute scheduled command: {Command}", command);
            throw;
        }
    }

    /// <summary>
    /// Starts a new conversation session for memory tracking.
    /// </summary>
    private void StartNewSession()
    {
        _currentSessionKey = $"session-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        _currentSession = new SessionTranscript
        {
            SessionKey = _currentSessionKey,
            StartedAt = DateTime.UtcNow,
            Metadata = new Dictionary<string, string>
            {
                ["agent_name"] = _config.Preferences.AgentName,
                ["ai_provider"] = _config.AiProvider.Provider,
                ["model"] = _config.AiProvider.ModelId
            }
        };
        
        _logger?.LogDebug("Started new session: {SessionKey}", _currentSessionKey);
    }

    /// <summary>
    /// Records a conversation turn in the current session.
    /// </summary>
    private void RecordConversationTurn(string userMessage, string assistantResponse)
    {
        if (_currentSession == null || _memoryManager == null)
            return;

        _currentSession.Entries.Add(new TranscriptEntry
        {
            Role = "user",
            Content = userMessage,
            Timestamp = DateTime.UtcNow
        });

        _currentSession.Entries.Add(new TranscriptEntry
        {
            Role = "assistant",
            Content = assistantResponse,
            Timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Saves the current session to memory.
    /// </summary>
    public async Task SaveCurrentSessionAsync(CancellationToken cancellationToken = default)
    {
        if (_currentSession == null || _memoryManager == null)
            return;

        if (_currentSession.Entries.Count == 0)
        {
            _logger?.LogDebug("No entries in current session, skipping save");
            return;
        }

        _currentSession.EndedAt = DateTime.UtcNow;
        
        try
        {
            await _memoryManager.SaveSessionAsync(_currentSession, cancellationToken);
            _logger?.LogInformation("Session {SessionKey} saved with {EntryCount} entries",
                _currentSession.SessionKey, _currentSession.Entries.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to save session {SessionKey}", _currentSession.SessionKey);
        }
    }

    /// <summary>
    /// Gets the system prompt for the agent.
    /// </summary>
    private string GetSystemPrompt()
    {
        var skillsDescription = GetSkillsDescription();
        var agentLoopConfig = _config.AgentLoop;
        
        // Get current time and timezone information
        var localTimeZone = TimeZoneInfo.Local;
        var currentLocalTime = DateTimeOffset.Now;
        var utcOffset = localTimeZone.GetUtcOffset(currentLocalTime);
        var utcOffsetString = utcOffset >= TimeSpan.Zero 
            ? $"+{utcOffset:hh\\:mm}" 
            : $"-{utcOffset:hh\\:mm}";
        
        return $"""
            You are {_config.Preferences.AgentName}, a helpful personal AI assistant.
            
            ## Current Time Context
            - **Timezone**: {localTimeZone.DisplayName} ({localTimeZone.Id})
            - **UTC Offset**: {utcOffsetString}
            - **Current Local Time**: {currentLocalTime:yyyy-MM-dd HH:mm:ss} ({localTimeZone.StandardName})
            - **Current UTC Time**: {currentLocalTime.UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC
            
            Use this time information when the user asks about time-related queries, scheduling, or when context about "today", "now", "this week", etc. is needed.
            
            Your capabilities include:
            - Answering questions and providing information
            - Helping with tasks using available tools/skills
            - Maintaining context throughout the conversation
            
            Guidelines:
            - Be helpful, accurate, and concise
            - Use available tools when they can help accomplish the user's request
            - If you're unsure about something, say so
            - Respect user privacy and handle sensitive information carefully
            
            ## Execution Limits and Safety Guidelines
            
            You are operating within an agentic loop with the following safety limits:
            - Maximum iterations per request: {agentLoopConfig.MaxIterations}
            - Maximum total function calls per request: {agentLoopConfig.MaxTotalFunctionCalls}
            - Function execution timeout: {agentLoopConfig.FunctionTimeoutSeconds} seconds
            - Overall request timeout: {agentLoopConfig.RuntimeTimeoutSeconds} seconds
            
            To work efficiently within these limits:
            1. **Plan before acting**: Think through the steps needed before making function calls
            2. **Batch operations when possible**: Combine related operations to minimize function calls
            3. **Avoid redundant calls**: Don't repeat the same function call with identical parameters
            4. **Handle errors gracefully**: If a function fails, explain the issue rather than retrying indefinitely
            5. **Provide partial results**: If you're approaching limits, summarize what you've accomplished
            6. **Be direct**: Provide answers without unnecessary tool calls when you already have the information
            
            If you reach a safety limit, explain to the user what was accomplished and what remains.
            
            {skillsDescription}
            """;
    }

    /// <summary>
    /// Gets a description of available skills for the system prompt.
    /// </summary>
    private string GetSkillsDescription()
    {
        if (_skillManager == null || !_skillManager.LoadedPlugins.Any())
        {
            return "No tools/skills are currently loaded.";
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("You have access to the following tools/skills:");
        sb.AppendLine();

        foreach (var plugin in _skillManager.LoadedPlugins)
        {
            sb.AppendLine($"**{plugin.Name}**");
            if (!string.IsNullOrEmpty(plugin.Description))
            {
                sb.AppendLine($"  Description: {plugin.Description}");
            }
            sb.AppendLine($"  Functions:");
            foreach (var function in plugin)
            {
                var description = function.Description ?? "No description";
                sb.AppendLine($"    - {function.Name}: {description}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("Use these tools when they can help accomplish the user's request. Call the appropriate function with the required parameters.");

        return sb.ToString();
    }

    /// <summary>
    /// Parses the retry-after value from a rate limit exception message.
    /// </summary>
    /// <param name="exception">The exception to parse.</param>
    /// <returns>The number of seconds to wait, or null if not found.</returns>
    private int? ParseRetryAfterSeconds(Exception exception)
    {
        var message = exception.Message;
        
        // Try to parse "retry after X seconds" pattern from the error message
        var retryAfterMatch = Regex.Match(message, @"retry after (\d+) seconds?", RegexOptions.IgnoreCase);
        if (retryAfterMatch.Success && int.TryParse(retryAfterMatch.Groups[1].Value, out var seconds))
        {
            return seconds;
        }

        // Try to parse "Retry-After: X" header pattern
        var headerMatch = Regex.Match(message, @"Retry-After:\s*(\d+)", RegexOptions.IgnoreCase);
        if (headerMatch.Success && int.TryParse(headerMatch.Groups[1].Value, out seconds))
        {
            return seconds;
        }

        return null;
    }

    /// <summary>
    /// Checks if an exception is a rate limit error (HTTP 429).
    /// </summary>
    /// <param name="exception">The exception to check.</param>
    /// <returns>True if this is a rate limit error.</returns>
    private static bool IsRateLimitException(Exception exception)
    {
        // Check for ClientResultException with 429 status
        if (exception is ClientResultException clientEx)
        {
            return clientEx.Message.Contains("429") ||
                   clientEx.Message.Contains("RateLimitReached", StringComparison.OrdinalIgnoreCase);
        }

        // Check the message for rate limit indicators
        var message = exception.Message;
        return message.Contains("429") ||
               message.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("RateLimitReached", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("exceeded the token rate limit", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Waits for the specified duration with progress updates.
    /// </summary>
    /// <param name="sessionId">The session ID for event tracking.</param>
    /// <param name="waitSeconds">Number of seconds to wait.</param>
    /// <param name="retryAttempt">Current retry attempt number.</param>
    /// <param name="maxRetries">Maximum number of retries.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task WaitForRateLimitAsync(
        string sessionId,
        int waitSeconds,
        int retryAttempt,
        int maxRetries,
        CancellationToken cancellationToken)
    {
        var message = $"Rate limit reached. Waiting {waitSeconds} seconds before retry {retryAttempt}/{maxRetries}...";
        
        _logger?.LogWarning(
            "Rate limit encountered for {SessionId}. Waiting {WaitSeconds}s (attempt {Attempt}/{MaxRetries})",
            sessionId, waitSeconds, retryAttempt, maxRetries);

        // Raise event for UI to display progress
        RateLimitWaiting?.Invoke(this, new RateLimitWaitEventArgs(
            sessionId,
            waitSeconds,
            retryAttempt,
            maxRetries,
            message));

        // Wait for the specified duration
        await Task.Delay(TimeSpan.FromSeconds(waitSeconds), cancellationToken);
    }

    /// <summary>
    /// Sends a message to the agent and gets a response.
    /// Implements runtime timeout, safety tracking, and rate limit handling for the agentic loop.
    /// </summary>
    /// <param name="userMessage">The user's message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The agent's response.</returns>
    public async Task<string> ChatAsync(string userMessage, CancellationToken cancellationToken = default)
    {
        if (_agent == null)
        {
            throw new InvalidOperationException("Agent not initialized. Call InitializeAsync first.");
        }

        // Generate unique session ID for this request
        var sessionId = $"request-{Interlocked.Increment(ref _requestCounter)}";
        
        _logger?.LogDebug("Starting request {SessionId}: {Message}", sessionId, userMessage);

        // Start new request tracking in safety filter
        _safetyFilter?.StartNewRequest(sessionId, userMessage);

        var rateLimitConfig = _config.AgentLoop.RateLimit;
        var retryAttempt = 0;
        var userMessageAdded = false;

        while (true)
        {
            // Create runtime timeout - this is the overall timeout for the entire request
            using var runtimeTimeoutCts = new CancellationTokenSource(
                TimeSpan.FromSeconds(_config.AgentLoop.RuntimeTimeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, runtimeTimeoutCts.Token);

            try
            {
                // Add user message to history only on first attempt
                if (!userMessageAdded)
                {
                    _chatHistory.AddUserMessage(userMessage);
                    userMessageAdded = true;
                }

                // Get response from agent
                var response = new System.Text.StringBuilder();
                var iterationCount = 0;
                
                await foreach (var message in _agent.InvokeAsync(_chatHistory, cancellationToken: linkedCts.Token))
                {
                    iterationCount++;
                    var content = message.Message.Content;
                    if (content != null)
                    {
                        response.Append(content);
                    }
                    
                    // Add agent response to history
                    _chatHistory.Add(message.Message);
                }

                var responseText = response.ToString();
                
                // Signal completion to safety filter
                _safetyFilter?.CompleteRequest(responseText, iterationCount);
                
                // Record conversation turn in session
                RecordConversationTurn(userMessage, responseText);
                
                _logger?.LogDebug("Request {SessionId} completed. Response: {Response}", sessionId, responseText);

                return responseText;
            }
            catch (OperationCanceledException) when (runtimeTimeoutCts.IsCancellationRequested)
            {
                _logger?.LogWarning(
                    "Request {SessionId} timed out after {Timeout} seconds (runtime timeout)",
                    sessionId, _config.AgentLoop.RuntimeTimeoutSeconds);
                
                // Signal failure to safety filter
                _safetyFilter?.FailRequest(
                    AgentLoopErrorType.RequestTimeout,
                    $"Runtime timeout after {_config.AgentLoop.RuntimeTimeoutSeconds} seconds",
                    0);
                
                // Add a message to history indicating timeout
                var timeoutMessage = $"[Request timed out after {_config.AgentLoop.RuntimeTimeoutSeconds} seconds. The operation was taking too long to complete.]";
                _chatHistory.AddAssistantMessage(timeoutMessage);
                
                return timeoutMessage;
            }
            catch (Exception ex) when (IsRateLimitException(ex) && rateLimitConfig.EnableRetry)
            {
                retryAttempt++;
                
                if (retryAttempt > rateLimitConfig.MaxRetries)
                {
                    _logger?.LogError(
                        "Rate limit retry exhausted for {SessionId} after {Attempts} attempts",
                        sessionId, rateLimitConfig.MaxRetries);
                    
                    // Signal failure to safety filter
                    _safetyFilter?.FailRequest(
                        AgentLoopErrorType.RateLimitExceeded,
                        $"Rate limit exceeded after {rateLimitConfig.MaxRetries} retry attempts",
                        0);
                    
                    var errorMessage = $"[Rate limit exceeded. Maximum retry attempts ({rateLimitConfig.MaxRetries}) exhausted. Please try again later.]";
                    _chatHistory.AddAssistantMessage(errorMessage);
                    
                    return errorMessage;
                }

                // Parse wait time from exception or use default
                var waitSeconds = ParseRetryAfterSeconds(ex) ?? rateLimitConfig.DefaultWaitSeconds;
                
                // Cap the wait time
                if (waitSeconds > rateLimitConfig.MaxWaitSeconds)
                {
                    _logger?.LogWarning(
                        "Rate limit wait time {WaitSeconds}s exceeds maximum {MaxWait}s for {SessionId}",
                        waitSeconds, rateLimitConfig.MaxWaitSeconds, sessionId);
                    
                    var errorMessage = $"[Rate limit requires waiting {waitSeconds} seconds, which exceeds the maximum allowed wait time of {rateLimitConfig.MaxWaitSeconds} seconds. Please try again later.]";
                    _chatHistory.AddAssistantMessage(errorMessage);
                    
                    return errorMessage;
                }

                // Wait and retry
                await WaitForRateLimitAsync(sessionId, waitSeconds, retryAttempt, rateLimitConfig.MaxRetries, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Sends a message to the agent and streams the response.
    /// Implements runtime timeout, safety tracking, and rate limit handling for the agentic loop.
    /// </summary>
    /// <param name="userMessage">The user's message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of response chunks.</returns>
    public async IAsyncEnumerable<string> ChatStreamingAsync(
        string userMessage,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_agent == null)
        {
            throw new InvalidOperationException("Agent not initialized. Call InitializeAsync first.");
        }

        // Generate unique session ID for this request
        var sessionId = $"request-{Interlocked.Increment(ref _requestCounter)}";
        
        _logger?.LogDebug("Starting streaming request {SessionId}: {Message}", sessionId, userMessage);

        // Start new request tracking in safety filter
        _safetyFilter?.StartNewRequest(sessionId, userMessage);

        var rateLimitConfig = _config.AgentLoop.RateLimit;
        var retryAttempt = 0;
        var userMessageAdded = false;
        var fullResponse = new System.Text.StringBuilder();
        var completed = false;

        while (!completed)
        {
            // Create runtime timeout - this is the overall timeout for the entire request
            using var runtimeTimeoutCts = new CancellationTokenSource(
                TimeSpan.FromSeconds(_config.AgentLoop.RuntimeTimeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, runtimeTimeoutCts.Token);

            // Add user message to history only on first attempt
            if (!userMessageAdded)
            {
                _chatHistory.AddUserMessage(userMessage);
                userMessageAdded = true;
            }

            var timedOut = false;
            string? errorMessage = null;
            var iterationCount = 0;
            Exception? rateLimitException = null;

            // We need to handle exceptions inside the async enumerable
            IAsyncEnumerator<AgentResponseItem<StreamingChatMessageContent>>? enumerator = null;
            
            try
            {
                enumerator = _agent.InvokeStreamingAsync(_chatHistory, cancellationToken: linkedCts.Token)
                    .GetAsyncEnumerator(linkedCts.Token);

                while (true)
                {
                    bool hasNext;
                    try
                    {
                        hasNext = await enumerator.MoveNextAsync();
                    }
                    catch (OperationCanceledException) when (runtimeTimeoutCts.IsCancellationRequested)
                    {
                        timedOut = true;
                        errorMessage = $"\n\n[Request timed out after {_config.AgentLoop.RuntimeTimeoutSeconds} seconds]";
                        break;
                    }
                    catch (Exception ex) when (IsRateLimitException(ex) && rateLimitConfig.EnableRetry)
                    {
                        rateLimitException = ex;
                        break;
                    }

                    if (!hasNext)
                        break;

                    iterationCount++;
                    var content = enumerator.Current.Message.Content;
                    if (content != null)
                    {
                        fullResponse.Append(content);
                        yield return content;
                    }
                }
            }
            finally
            {
                if (enumerator != null)
                {
                    await enumerator.DisposeAsync();
                }
            }

            // Handle rate limit exception
            if (rateLimitException != null)
            {
                retryAttempt++;
                
                if (retryAttempt > rateLimitConfig.MaxRetries)
                {
                    _logger?.LogError(
                        "Rate limit retry exhausted for streaming {SessionId} after {Attempts} attempts",
                        sessionId, rateLimitConfig.MaxRetries);
                    
                    // Signal failure to safety filter
                    _safetyFilter?.FailRequest(
                        AgentLoopErrorType.RateLimitExceeded,
                        $"Rate limit exceeded after {rateLimitConfig.MaxRetries} retry attempts",
                        iterationCount);
                    
                    var rateLimitErrorMessage = $"\n\n[Rate limit exceeded. Maximum retry attempts ({rateLimitConfig.MaxRetries}) exhausted. Please try again later.]";
                    fullResponse.Append(rateLimitErrorMessage);
                    yield return rateLimitErrorMessage;
                    
                    // Add the complete response to history
                    _chatHistory.AddAssistantMessage(fullResponse.ToString());
                    completed = true;
                    continue;
                }

                // Parse wait time from exception or use default
                var waitSeconds = ParseRetryAfterSeconds(rateLimitException) ?? rateLimitConfig.DefaultWaitSeconds;
                
                // Cap the wait time
                if (waitSeconds > rateLimitConfig.MaxWaitSeconds)
                {
                    _logger?.LogWarning(
                        "Rate limit wait time {WaitSeconds}s exceeds maximum {MaxWait}s for streaming {SessionId}",
                        waitSeconds, rateLimitConfig.MaxWaitSeconds, sessionId);
                    
                    var maxWaitErrorMessage = $"\n\n[Rate limit requires waiting {waitSeconds} seconds, which exceeds the maximum allowed wait time of {rateLimitConfig.MaxWaitSeconds} seconds. Please try again later.]";
                    fullResponse.Append(maxWaitErrorMessage);
                    yield return maxWaitErrorMessage;
                    
                    // Add the complete response to history
                    _chatHistory.AddAssistantMessage(fullResponse.ToString());
                    completed = true;
                    continue;
                }

                // Yield a message about waiting
                var waitMessage = $"\n\n[Rate limit reached. Waiting {waitSeconds} seconds before retry {retryAttempt}/{rateLimitConfig.MaxRetries}...]";
                yield return waitMessage;

                // Wait and retry
                await WaitForRateLimitAsync(sessionId, waitSeconds, retryAttempt, rateLimitConfig.MaxRetries, cancellationToken);
                
                // Clear the response for retry (we'll start fresh)
                fullResponse.Clear();
                continue;
            }

            // Yield error message if there was one
            if (errorMessage != null)
            {
                fullResponse.Append(errorMessage);
                yield return errorMessage;
            }

            // Add the complete response to history
            _chatHistory.AddAssistantMessage(fullResponse.ToString());

            if (timedOut)
            {
                _logger?.LogWarning(
                    "Streaming request {SessionId} timed out after {Timeout} seconds",
                    sessionId, _config.AgentLoop.RuntimeTimeoutSeconds);
                
                // Signal failure to safety filter
                _safetyFilter?.FailRequest(
                    AgentLoopErrorType.RequestTimeout,
                    $"Runtime timeout after {_config.AgentLoop.RuntimeTimeoutSeconds} seconds",
                    iterationCount);
            }
            else
            {
                _logger?.LogDebug("Streaming request {SessionId} completed successfully", sessionId);
                
                // Signal completion to safety filter
                _safetyFilter?.CompleteRequest(fullResponse.ToString(), iterationCount);
                
                // Record conversation turn in session
                RecordConversationTurn(userMessage, fullResponse.ToString());
            }

            completed = true;
        }
    }

    /// <summary>
    /// Clears the chat history.
    /// </summary>
    public void ClearHistory()
    {
        _chatHistory = [];
        _logger?.LogInformation("Chat history cleared");
    }

    /// <summary>
    /// Gets the number of messages in the chat history.
    /// </summary>
    public int GetHistoryCount() => _chatHistory.Count;

    /// <summary>
    /// Reloads skills with updated configuration.
    /// </summary>
    /// <param name="config">The updated configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ReloadSkillsAsync(MicrobotConfig config, CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Reloading skills...");

        // Dispose existing skill manager
        if (_skillManager != null)
        {
            await _skillManager.DisposeAsync();
        }

        // Create new skill manager with updated config
        _skillManager = new SkillManager(
            config.Skills,
            _loggerFactory,
            _deviceCodeCallback);

        // Load skills
        await _skillManager.LoadAllSkillsAsync(cancellationToken);

        // Re-register with kernel
        if (_kernel != null)
        {
            // Clear existing plugins
            _kernel.Plugins.Clear();
            
            // Register new plugins
            _skillManager.RegisterPluginsWithKernel(_kernel);
        }

        _logger?.LogInformation("Skills reloaded successfully");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        // Save current session before disposing
        try
        {
            await SaveCurrentSessionAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error saving session during dispose");
        }

        // Dispose schedule executor service
        _scheduleExecutorService?.Dispose();

        // Dispose schedule database context
        _scheduleDbContext?.Dispose();

        // Dispose memory sync service
        _memorySyncService?.Dispose();

        // Dispose memory manager
        if (_memoryManager is IDisposable disposableMemory)
        {
            disposableMemory.Dispose();
        }

        // Dispose skill manager
        if (_skillManager != null)
        {
            await _skillManager.DisposeAsync();
        }

        _disposed = true;
    }
}
