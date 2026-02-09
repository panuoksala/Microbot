# Anthropic Claude Provider Implementation Plan

## Overview

This document outlines the implementation plan for adding direct Anthropic Claude model support to Microbot. This will allow users to use Claude models (Claude 4 Opus, Claude 4 Sonnet, Claude 3.5 Sonnet, etc.) directly from Anthropic's API.

## Current State

Microbot currently supports three AI providers:
- **OpenAI** - Direct OpenAI API
- **Azure OpenAI** - Azure-hosted OpenAI models
- **Ollama** - Local LLM via OpenAI-compatible API

The AI provider is configured in `AiProviderConfig` and initialized in `AgentService.ConfigureAiProvider()`.

## Research Findings

### Semantic Kernel Anthropic Support

**Key Finding**: Microsoft Semantic Kernel for .NET does NOT have a native Anthropic connector for direct API access.

### Chosen Approach

**Custom IChatCompletionService Implementation** using the `Anthropic.SDK` NuGet package.

This approach:
- Maintains consistency with existing Semantic Kernel architecture
- Provides full control over Anthropic-specific features
- Allows proper mapping of function calling/tools
- Supports streaming responses
- No dependency on AWS or Azure Foundry

## Implementation Details

### 1. NuGet Package

Add the Anthropic SDK package to `Microbot.Console.csproj`:

```xml
<PackageReference Include="Anthropic.SDK" Version="3.*" />
```

### 2. Configuration Model Updates

Update `AiProviderConfig` in `MicrobotConfig.cs`:

```csharp
public class AiProviderConfig
{
    /// <summary>
    /// The AI provider type: "OpenAI", "AzureOpenAI", "Ollama", "Anthropic".
    /// </summary>
    public string Provider { get; set; } = "AzureOpenAI";

    /// <summary>
    /// The model/deployment ID to use.
    /// For Anthropic: claude-opus-4-5-20250929, claude-sonnet-4-5-20250929,
    /// claude-3-5-sonnet-20241022, claude-3-5-haiku-20241022, etc.
    /// </summary>
    public string ModelId { get; set; } = "gpt-4o";

    /// <summary>
    /// The endpoint URL (required for Azure OpenAI, optional for Anthropic).
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// The API key for authentication.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Maximum tokens to generate in the response.
    /// Anthropic requires this to be set explicitly.
    /// </summary>
    public int? MaxTokens { get; set; }
}
```

### 3. AnthropicChatCompletionService

Create a new file `src/Microbot.Console/Services/AnthropicChatCompletionService.cs`:

```csharp
using System.Runtime.CompilerServices;
using System.Text.Json;
using Anthropic.SDK;
using Anthropic.SDK.Constants;
using Anthropic.SDK.Messaging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Services;

namespace Microbot.Console.Services;

/// <summary>
/// Anthropic Claude chat completion service implementing Semantic Kernel's IChatCompletionService.
/// </summary>
public class AnthropicChatCompletionService : IChatCompletionService
{
    private readonly AnthropicClient _client;
    private readonly string _modelId;
    private readonly int _maxTokens;
    private readonly Dictionary<string, object?> _attributes = new();

    public AnthropicChatCompletionService(
        string apiKey,
        string modelId,
        int maxTokens = 4096,
        string? endpoint = null)
    {
        _client = new AnthropicClient(apiKey);
        _modelId = modelId;
        _maxTokens = maxTokens;
        
        _attributes[AIServiceExtensions.ModelIdKey] = modelId;
    }

    public IReadOnlyDictionary<string, object?> Attributes => _attributes;

    public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        var messages = ConvertToAnthropicMessages(chatHistory);
        var systemPrompt = ExtractSystemPrompt(chatHistory);
        
        var parameters = new MessageParameters
        {
            Model = _modelId,
            MaxTokens = _maxTokens,
            Messages = messages,
            System = systemPrompt != null ? new List<SystemMessage> { new(systemPrompt) } : null
        };

        // Add tools if kernel has functions
        if (kernel?.Plugins.Any() == true)
        {
            parameters.Tools = ConvertKernelFunctionsToTools(kernel);
        }

        var response = await _client.Messages.GetClaudeMessageAsync(parameters, cancellationToken);

        return ProcessResponse(response, kernel);
    }

    public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var messages = ConvertToAnthropicMessages(chatHistory);
        var systemPrompt = ExtractSystemPrompt(chatHistory);
        
        var parameters = new MessageParameters
        {
            Model = _modelId,
            MaxTokens = _maxTokens,
            Messages = messages,
            System = systemPrompt != null ? new List<SystemMessage> { new(systemPrompt) } : null,
            Stream = true
        };

        // Add tools if kernel has functions
        if (kernel?.Plugins.Any() == true)
        {
            parameters.Tools = ConvertKernelFunctionsToTools(kernel);
        }

        await foreach (var streamEvent in _client.Messages.StreamClaudeMessageAsync(parameters, cancellationToken))
        {
            if (streamEvent is ContentBlockDelta delta && delta.Delta?.Text != null)
            {
                yield return new StreamingChatMessageContent(
                    AuthorRole.Assistant,
                    delta.Delta.Text,
                    modelId: _modelId);
            }
        }
    }

    private List<Message> ConvertToAnthropicMessages(ChatHistory chatHistory)
    {
        var messages = new List<Message>();
        
        foreach (var message in chatHistory)
        {
            // Skip system messages - they're handled separately
            if (message.Role == AuthorRole.System)
                continue;

            var role = message.Role == AuthorRole.User 
                ? RoleType.User 
                : RoleType.Assistant;

            messages.Add(new Message
            {
                Role = role,
                Content = message.Content ?? string.Empty
            });
        }

        return messages;
    }

    private string? ExtractSystemPrompt(ChatHistory chatHistory)
    {
        var systemMessages = chatHistory
            .Where(m => m.Role == AuthorRole.System)
            .Select(m => m.Content)
            .Where(c => !string.IsNullOrEmpty(c));

        return systemMessages.Any() 
            ? string.Join("\n\n", systemMessages) 
            : null;
    }

    private List<Tool> ConvertKernelFunctionsToTools(Kernel kernel)
    {
        var tools = new List<Tool>();
        
        foreach (var plugin in kernel.Plugins)
        {
            foreach (var function in plugin)
            {
                var tool = new Tool
                {
                    Name = $"{plugin.Name}_{function.Name}",
                    Description = function.Description,
                    InputSchema = ConvertParametersToSchema(function.Metadata.Parameters)
                };
                tools.Add(tool);
            }
        }

        return tools;
    }

    private InputSchema ConvertParametersToSchema(IReadOnlyList<KernelParameterMetadata> parameters)
    {
        var properties = new Dictionary<string, object>();
        var required = new List<string>();

        foreach (var param in parameters)
        {
            properties[param.Name] = new
            {
                type = GetJsonType(param.ParameterType),
                description = param.Description ?? param.Name
            };

            if (param.IsRequired)
            {
                required.Add(param.Name);
            }
        }

        return new InputSchema
        {
            Type = "object",
            Properties = properties,
            Required = required
        };
    }

    private string GetJsonType(Type? type)
    {
        if (type == null) return "string";
        
        return Type.GetTypeCode(type) switch
        {
            TypeCode.Boolean => "boolean",
            TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64 or
            TypeCode.UInt16 or TypeCode.UInt32 or TypeCode.UInt64 => "integer",
            TypeCode.Single or TypeCode.Double or TypeCode.Decimal => "number",
            _ => "string"
        };
    }

    private IReadOnlyList<ChatMessageContent> ProcessResponse(
        MessageResponse response, 
        Kernel? kernel)
    {
        var results = new List<ChatMessageContent>();

        foreach (var content in response.Content)
        {
            if (content is TextContent textContent)
            {
                results.Add(new ChatMessageContent(
                    AuthorRole.Assistant,
                    textContent.Text,
                    modelId: _modelId));
            }
            else if (content is ToolUseContent toolUse && kernel != null)
            {
                // Handle tool calls - this will be processed by Semantic Kernel's auto function calling
                var functionCallContent = new FunctionCallContent(
                    toolUse.Name,
                    toolUse.Id,
                    JsonSerializer.Deserialize<Dictionary<string, object?>>(
                        JsonSerializer.Serialize(toolUse.Input)));
                
                results.Add(new ChatMessageContent(
                    AuthorRole.Assistant,
                    [functionCallContent],
                    modelId: _modelId));
            }
        }

        return results;
    }
}
```

### 4. Update AgentService.ConfigureAiProvider

Update `src/Microbot.Console/Services/AgentService.cs`:

```csharp
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
            var endpoint = provider.Endpoint ?? "http://localhost:11434/v1";
            builder.AddOpenAIChatCompletion(
                modelId: provider.ModelId,
                apiKey: "ollama",
                endpoint: new Uri(endpoint));
            break;

        case "anthropic":
            var anthropicService = new AnthropicChatCompletionService(
                apiKey: provider.ApiKey ?? throw new InvalidOperationException("Anthropic API key is required."),
                modelId: provider.ModelId,
                maxTokens: provider.MaxTokens ?? 4096,
                endpoint: provider.Endpoint);
            builder.Services.AddSingleton<IChatCompletionService>(anthropicService);
            break;

        default:
            throw new InvalidOperationException($"Unsupported AI provider: {provider.Provider}");
    }
}
```

### 5. Update Setup Wizard

Update `src/Microbot.Console/Program.cs` `RunMinimalSetupAsync()`:

```csharp
private static Task RunMinimalSetupAsync()
{
    _ui.DisplayInfo("Please configure your AI provider settings.");
    AnsiConsole.WriteLine();

    // Select AI provider
    var provider = _ui.SelectOption(
        "Select your AI provider:",
        new[] { "AzureOpenAI", "OpenAI", "Anthropic", "Ollama" });
    _config.AiProvider.Provider = provider;

    // Get model ID
    var defaultModel = provider switch
    {
        "AzureOpenAI" => "gpt-4o",
        "OpenAI" => "gpt-4o",
        "Anthropic" => "claude-sonnet-4-5-20250929",
        "Ollama" => "llama3.2",
        _ => "gpt-4o"
    };
    _config.AiProvider.ModelId = _ui.PromptText(
        $"Enter model/deployment name (default: {defaultModel}):",
        defaultModel);

    // Get endpoint for Azure/Ollama
    if (provider is "AzureOpenAI" or "Ollama")
    {
        var defaultEndpoint = provider == "Ollama"
            ? "http://localhost:11434/v1"
            : "";
        _config.AiProvider.Endpoint = _ui.PromptText(
            $"Enter endpoint URL{(string.IsNullOrEmpty(defaultEndpoint) ? "" : $" (default: {defaultEndpoint})")}:",
            defaultEndpoint,
            allowEmpty: provider == "Ollama");
    }

    // Get API key (except for Ollama)
    if (provider != "Ollama")
    {
        _config.AiProvider.ApiKey = _ui.PromptSecret("Enter your API key:");
    }

    // Anthropic-specific: max tokens
    if (provider == "Anthropic")
    {
        var maxTokensStr = _ui.PromptText(
            "Enter max tokens for responses (default: 4096):",
            "4096");
        if (int.TryParse(maxTokensStr, out var maxTokens))
        {
            _config.AiProvider.MaxTokens = maxTokens;
        }
    }

    // Agent name
    _config.Preferences.AgentName = _ui.PromptText(
        "Enter a name for your AI assistant (default: Microbot):",
        "Microbot");

    // ... rest of setup
}
```

## Claude Model Options

| Model | Description | Context Window |
|-------|-------------|----------------|
| `claude-opus-4-5-20250929` | Most powerful Claude 4 model, best for complex reasoning | 200K tokens |
| `claude-sonnet-4-5-20250929` | Balanced Claude 4 model, great performance | 200K tokens |
| `claude-3-5-sonnet-20241022` | Claude 3.5 Sonnet, excellent for most tasks | 200K tokens |
| `claude-3-5-haiku-20241022` | Claude 3.5 Haiku, fast and cost-effective | 200K tokens |
| `claude-3-opus-20240229` | Claude 3 Opus, powerful for complex tasks | 200K tokens |
| `claude-3-sonnet-20240229` | Claude 3 Sonnet, balanced performance | 200K tokens |
| `claude-3-haiku-20240307` | Claude 3 Haiku, fastest and most affordable | 200K tokens |

**Recommended default**: `claude-sonnet-4-5-20250929` - Best balance of capability and cost.

## Configuration Example

```json
{
  "AiProvider": {
    "Provider": "Anthropic",
    "ModelId": "claude-sonnet-4-5-20250929",
    "ApiKey": "sk-ant-api03-...",
    "MaxTokens": 8192
  }
}
```

## Testing Checklist

- [ ] Basic chat completion works
- [ ] Streaming responses work
- [ ] Function calling/tools work with Semantic Kernel plugins
- [ ] System prompts are properly handled
- [ ] Error handling for rate limits
- [ ] Error handling for invalid API keys
- [ ] Setup wizard correctly configures Anthropic

## Limitations and Considerations

1. **Max Tokens Required**: Anthropic API requires `max_tokens` to be explicitly set
2. **Tool Calling Format**: Anthropic uses a different tool calling format than OpenAI
3. **No Vision Support Initially**: Image/vision support can be added later
4. **Rate Limits**: Anthropic has different rate limits than OpenAI

## Future Enhancements

1. Add vision/image support for Claude 3 models
2. Add support for Anthropic's extended thinking feature
3. Add support for Anthropic's computer use capabilities
4. Add caching support using Anthropic's prompt caching

## Files to Modify

1. `src/Microbot.Console/Microbot.Console.csproj` - Add Anthropic.SDK package
2. `src/Microbot.Core/Models/MicrobotConfig.cs` - Add MaxTokens property
3. `src/Microbot.Console/Services/AnthropicChatCompletionService.cs` - New file
4. `src/Microbot.Console/Services/AgentService.cs` - Add Anthropic case
5. `src/Microbot.Console/Program.cs` - Update setup wizard
6. `AGENTS.md` - Update documentation
7. `plans/implementation-plan.md` - Update implementation status
