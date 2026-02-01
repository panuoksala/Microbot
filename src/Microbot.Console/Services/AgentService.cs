namespace Microbot.Console.Services;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microbot.Core.Models;
using Microbot.Skills;

/// <summary>
/// Service for managing the AI agent using Semantic Kernel.
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
    private bool _disposed;

    /// <summary>
    /// Gets the current chat history.
    /// </summary>
    public ChatHistory ChatHistory => _chatHistory;

    /// <summary>
    /// Gets the skill manager.
    /// </summary>
    public SkillManager? SkillManager => _skillManager;

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

        // Load and register skills
        _skillManager = new SkillManager(_config.Skills, _loggerFactory, _deviceCodeCallback);
        var plugins = await _skillManager.LoadAllSkillsAsync(cancellationToken);
        _skillManager.RegisterPluginsWithKernel(_kernel);

        // Create the chat completion agent
        _agent = new ChatCompletionAgent
        {
            Name = _config.Preferences.AgentName,
            Instructions = GetSystemPrompt(),
            Kernel = _kernel,
            Arguments = new KernelArguments(
                new OpenAIPromptExecutionSettings
                {
                    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
                })
        };

        _logger?.LogInformation("Agent initialized successfully");
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
    /// Gets the system prompt for the agent.
    /// </summary>
    private string GetSystemPrompt()
    {
        var skillsDescription = GetSkillsDescription();
        
        return $"""
            You are {_config.Preferences.AgentName}, a helpful personal AI assistant.
            
            Your capabilities include:
            - Answering questions and providing information
            - Helping with tasks using available tools/skills
            - Maintaining context throughout the conversation
            
            Guidelines:
            - Be helpful, accurate, and concise
            - Use available tools when they can help accomplish the user's request
            - If you're unsure about something, say so
            - Respect user privacy and handle sensitive information carefully
            
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
    /// Sends a message to the agent and gets a response.
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

        _logger?.LogDebug("User message: {Message}", userMessage);

        // Add user message to history
        _chatHistory.AddUserMessage(userMessage);

        // Get response from agent
        var response = new System.Text.StringBuilder();
        
        await foreach (var message in _agent.InvokeAsync(_chatHistory, cancellationToken: cancellationToken))
        {
            var content = message.Message.Content;
            if (content != null)
            {
                response.Append(content);
            }
            
            // Add agent response to history
            _chatHistory.Add(message.Message);
        }

        var responseText = response.ToString();
        _logger?.LogDebug("Agent response: {Response}", responseText);

        return responseText;
    }

    /// <summary>
    /// Sends a message to the agent and streams the response.
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

        _logger?.LogDebug("User message (streaming): {Message}", userMessage);

        // Add user message to history
        _chatHistory.AddUserMessage(userMessage);

        var fullResponse = new System.Text.StringBuilder();

        await foreach (var message in _agent.InvokeStreamingAsync(_chatHistory, cancellationToken: cancellationToken))
        {
            var content = message.Message.Content;
            if (content != null)
            {
                fullResponse.Append(content);
                yield return content;
            }
        }

        // Add the complete response to history
        _chatHistory.AddAssistantMessage(fullResponse.ToString());
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

        if (_skillManager != null)
        {
            await _skillManager.DisposeAsync();
        }

        _disposed = true;
    }
}
