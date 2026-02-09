using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Services;

namespace Microbot.Console.Services;

/// <summary>
/// Custom IChatCompletionService implementation for Anthropic Claude models.
/// Bridges the Anthropic SDK with Microsoft Semantic Kernel.
/// </summary>
public sealed class AnthropicChatCompletionService : IChatCompletionService
{
    private readonly AnthropicClient _client;
    private readonly string _modelId;
    private readonly int _maxTokens;
    private readonly Dictionary<string, object?> _attributes;

    /// <summary>
    /// Initializes a new instance of the AnthropicChatCompletionService.
    /// </summary>
    /// <param name="apiKey">The Anthropic API key.</param>
    /// <param name="modelId">The model ID (e.g., "claude-sonnet-4-5-20250929").</param>
    /// <param name="maxTokens">Maximum tokens for responses (default: 4096).</param>
    public AnthropicChatCompletionService(string apiKey, string modelId, int maxTokens = 4096)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey, nameof(apiKey));
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId, nameof(modelId));

        _client = new AnthropicClient { ApiKey = apiKey };
        _modelId = modelId;
        _maxTokens = maxTokens;
        _attributes = new Dictionary<string, object?>
        {
            [AIServiceExtensions.ModelIdKey] = modelId,
            ["Provider"] = "Anthropic"
        };
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, object?> Attributes => _attributes;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        var (systemPrompt, messages) = ConvertChatHistory(chatHistory);
        var tools = ConvertKernelFunctions(kernel);

        var parameters = CreateMessageParams(systemPrompt, messages, tools);

        var response = await _client.Messages.Create(parameters, cancellationToken);
        return ConvertResponse(response);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (systemPrompt, messages) = ConvertChatHistory(chatHistory);
        var tools = ConvertKernelFunctions(kernel);

        var parameters = CreateMessageParams(systemPrompt, messages, tools);

        var toolCallsInProgress = new Dictionary<int, ToolCallBuilder>();
        string? currentToolCallId = null;
        int toolCallIndex = 0;

        await foreach (var rawEvent in _client.Messages.CreateStreaming(parameters).WithCancellation(cancellationToken))
        {
            // Handle text content
            if (rawEvent.TryPickContentBlockDelta(out var delta))
            {
                if (delta.Delta.TryPickText(out var textDelta))
                {
                    yield return new StreamingChatMessageContent(
                        AuthorRole.Assistant,
                        textDelta.Text,
                        modelId: _modelId);
                }
                else if (delta.Delta.TryPickInputJson(out var inputJsonDelta))
                {
                    // Accumulate tool input JSON
                    if (currentToolCallId != null && toolCallsInProgress.TryGetValue(toolCallIndex - 1, out var builder))
                    {
                        builder.AppendInput(inputJsonDelta.PartialJson);
                    }
                }
            }
            // Handle content block start (for tool use)
            else if (rawEvent.TryPickContentBlockStart(out var blockStart))
            {
                if (blockStart.ContentBlock.TryPickToolUse(out var toolUseStart))
                {
                    currentToolCallId = toolUseStart.ID;
                    toolCallsInProgress[toolCallIndex] = new ToolCallBuilder(toolUseStart.ID, toolUseStart.Name);
                    toolCallIndex++;
                }
            }
            // Handle content block stop - emit accumulated tool calls
            else if (rawEvent.TryPickContentBlockStop(out _))
            {
                // Check if we have tool calls to emit
                if (toolCallsInProgress.Count > 0)
                {
                    foreach (var kvp in toolCallsInProgress)
                    {
                        var builder = kvp.Value;
                        var functionCallContent = new FunctionCallContent(
                            builder.Name,
                            builder.Name,
                            builder.Id,
                            builder.GetKernelArguments());

                        yield return new StreamingChatMessageContent(
                            AuthorRole.Assistant,
                            content: null,
                            modelId: _modelId,
                            innerContent: null,
                            metadata: new Dictionary<string, object?>
                            {
                                ["FunctionCallContent"] = functionCallContent
                            });
                    }
                    toolCallsInProgress.Clear();
                }
            }
        }
    }

    /// <summary>
    /// Creates MessageCreateParams with the given parameters.
    /// </summary>
    private MessageCreateParams CreateMessageParams(
        string? systemPrompt,
        List<MessageParam> messages,
        List<Tool> tools)
    {
        return new MessageCreateParams
        {
            MaxTokens = _maxTokens,
            Model = _modelId,
            Messages = messages,
            System = string.IsNullOrEmpty(systemPrompt) ? null : (MessageCreateParamsSystem)systemPrompt,
            Tools = tools.Count > 0 ? tools.Select(t => (ToolUnion)t).ToList() : null
        };
    }

    /// <summary>
    /// Converts Semantic Kernel ChatHistory to Anthropic message format.
    /// Extracts system prompt separately as Anthropic handles it differently.
    /// </summary>
    private static (string? SystemPrompt, List<MessageParam> Messages) ConvertChatHistory(ChatHistory chatHistory)
    {
        string? systemPrompt = null;
        var messages = new List<MessageParam>();

        foreach (var message in chatHistory)
        {
            if (message.Role == AuthorRole.System)
            {
                // Anthropic handles system prompts separately
                systemPrompt = message.Content;
                continue;
            }

            var role = message.Role == AuthorRole.Assistant ? Role.Assistant : Role.User;

            // Check for function call results (tool results)
            var functionResultContents = message.Items.OfType<FunctionResultContent>().ToList();
            if (functionResultContents.Count > 0)
            {
                var toolResults = new List<ContentBlockParam>();
                foreach (var functionResult in functionResultContents)
                {
                    var resultContent = functionResult.Result?.ToString() ?? string.Empty;
                    toolResults.Add(new ToolResultBlockParam(functionResult.CallId ?? string.Empty)
                    {
                        Content = resultContent
                    });
                }
                messages.Add(new MessageParam { Role = Role.User, Content = toolResults });
                continue;
            }

            // Check for function calls (tool use) in assistant messages
            var functionCallContents = message.Items.OfType<FunctionCallContent>().ToList();
            if (functionCallContents.Count > 0)
            {
                var contentBlocks = new List<ContentBlockParam>();

                // Add any text content first
                if (!string.IsNullOrEmpty(message.Content))
                {
                    contentBlocks.Add(new TextBlockParam(message.Content));
                }

                // Add tool use blocks
                foreach (var functionCall in functionCallContents)
                {
                    var inputDict = functionCall.Arguments != null
                        ? ConvertKernelArgumentsToJsonDict(functionCall.Arguments)
                        : new Dictionary<string, JsonElement>();

                    var toolUseBlock = new ToolUseBlockParam
                    {
                        ID = functionCall.Id ?? Guid.NewGuid().ToString(),
                        Name = functionCall.FunctionName,
                        Input = inputDict
                    };
                    contentBlocks.Add(toolUseBlock);
                }

                messages.Add(new MessageParam { Role = Role.Assistant, Content = contentBlocks });
                continue;
            }

            // Regular text message
            if (!string.IsNullOrEmpty(message.Content))
            {
                messages.Add(new MessageParam { Role = role, Content = message.Content });
            }
        }

        return (systemPrompt, messages);
    }

    /// <summary>
    /// Converts KernelArguments to a dictionary for JSON serialization.
    /// </summary>
    private static Dictionary<string, object?> ConvertKernelArgumentsToDict(KernelArguments? arguments)
    {
        if (arguments == null)
        {
            return new Dictionary<string, object?>();
        }

        var dict = new Dictionary<string, object?>();
        foreach (var kvp in arguments)
        {
            dict[kvp.Key] = kvp.Value;
        }
        return dict;
    }

    /// <summary>
    /// Converts KernelArguments to a dictionary of JsonElements for Anthropic API.
    /// </summary>
    private static Dictionary<string, JsonElement> ConvertKernelArgumentsToJsonDict(KernelArguments? arguments)
    {
        if (arguments == null)
        {
            return new Dictionary<string, JsonElement>();
        }

        var dict = new Dictionary<string, JsonElement>();
        foreach (var kvp in arguments)
        {
            dict[kvp.Key] = JsonSerializer.SerializeToElement(kvp.Value);
        }
        return dict;
    }

    /// <summary>
    /// Converts Semantic Kernel functions to Anthropic tool definitions.
    /// </summary>
    private static List<Tool> ConvertKernelFunctions(Kernel? kernel)
    {
        var tools = new List<Tool>();

        if (kernel?.Plugins == null)
        {
            return tools;
        }

        foreach (var plugin in kernel.Plugins)
        {
            foreach (var function in plugin)
            {
                var properties = new Dictionary<string, JsonElement>();
                var required = new List<string>();

                foreach (var parameter in function.Metadata.Parameters)
                {
                    var paramSchema = new Dictionary<string, object>
                    {
                        ["type"] = GetJsonType(parameter.ParameterType),
                        ["description"] = parameter.Description ?? parameter.Name
                    };

                    properties[parameter.Name] = JsonSerializer.SerializeToElement(paramSchema);

                    if (parameter.IsRequired)
                    {
                        required.Add(parameter.Name);
                    }
                }

                var tool = new Tool
                {
                    Name = $"{plugin.Name}-{function.Name}",
                    Description = function.Description ?? function.Name,
                    InputSchema = new InputSchema
                    {
                        Properties = properties,
                        Required = required
                    }
                };

                tools.Add(tool);
            }
        }

        return tools;
    }

    /// <summary>
    /// Maps .NET types to JSON Schema types.
    /// </summary>
    private static string GetJsonType(System.Type? type)
    {
        if (type == null) return "string";

        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

        return underlyingType switch
        {
            System.Type t when t == typeof(string) => "string",
            System.Type t when t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte) => "integer",
            System.Type t when t == typeof(float) || t == typeof(double) || t == typeof(decimal) => "number",
            System.Type t when t == typeof(bool) => "boolean",
            System.Type t when t.IsArray || t.IsAssignableTo(typeof(System.Collections.IEnumerable)) => "array",
            _ => "object"
        };
    }

    /// <summary>
    /// Converts Anthropic response to Semantic Kernel ChatMessageContent.
    /// </summary>
    private IReadOnlyList<ChatMessageContent> ConvertResponse(Message response)
    {
        var results = new List<ChatMessageContent>();
        var textContent = new StringBuilder();
        var functionCalls = new List<FunctionCallContent>();

        foreach (var contentBlock in response.Content)
        {
            if (contentBlock.TryPickText(out var textBlock))
            {
                textContent.Append(textBlock.Text);
            }
            else if (contentBlock.TryPickToolUse(out var toolUse))
            {
                var arguments = ConvertToolInputToKernelArguments(toolUse.Input);

                functionCalls.Add(new FunctionCallContent(
                    toolUse.Name,
                    toolUse.Name,
                    toolUse.ID,
                    arguments));
            }
        }

        // Create the chat message with text content
        var chatMessage = new ChatMessageContent(
            AuthorRole.Assistant,
            textContent.Length > 0 ? textContent.ToString() : null)
        {
            ModelId = _modelId,
            Metadata = new Dictionary<string, object?>
            {
                ["StopReason"] = response.StopReason?.ToString(),
                ["InputTokens"] = response.Usage?.InputTokens,
                ["OutputTokens"] = response.Usage?.OutputTokens
            }
        };

        // Add function calls to the items collection
        foreach (var functionCall in functionCalls)
        {
            chatMessage.Items.Add(functionCall);
        }

        results.Add(chatMessage);
        return results;
    }

    /// <summary>
    /// Converts Anthropic tool input to Semantic Kernel KernelArguments.
    /// </summary>
    private static KernelArguments? ConvertToolInputToKernelArguments(IReadOnlyDictionary<string, JsonElement>? input)
    {
        if (input == null || input.Count == 0)
        {
            return null;
        }

        var arguments = new KernelArguments();
        foreach (var kvp in input)
        {
            arguments[kvp.Key] = ConvertJsonElement(kvp.Value);
        }
        return arguments;
    }

    /// <summary>
    /// Converts a JsonElement to an appropriate .NET object.
    /// </summary>
    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToList(),
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => ConvertJsonElement(p.Value)),
            _ => element.GetRawText()
        };
    }

    /// <summary>
    /// Helper class to accumulate tool call information during streaming.
    /// </summary>
    private sealed class ToolCallBuilder
    {
        private readonly StringBuilder _inputJson = new();

        public string Id { get; }
        public string Name { get; }

        public ToolCallBuilder(string id, string name)
        {
            Id = id;
            Name = name;
        }

        public void AppendInput(string json)
        {
            _inputJson.Append(json);
        }

        public KernelArguments? GetKernelArguments()
        {
            var json = _inputJson.ToString();
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                return ConvertToolInputToKernelArguments(dict);
            }
            catch
            {
                return null;
            }
        }
    }
}
