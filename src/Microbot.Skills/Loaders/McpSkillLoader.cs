namespace Microbot.Skills.Loaders;

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microbot.Core.Interfaces;
using Microbot.Core.Models;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

/// <summary>
/// Loads skills from MCP (Model Context Protocol) servers.
/// </summary>
public class McpSkillLoader : ISkillLoader
{
    private readonly SkillsConfig _config;
    private readonly ILogger<McpSkillLoader>? _logger;
    private readonly List<McpClient> _clients = [];
    private bool _disposed;

    /// <inheritdoc />
    public string LoaderName => "MCP";

    /// <summary>
    /// Creates a new MCP skill loader.
    /// </summary>
    /// <param name="config">Skills configuration.</param>
    /// <param name="logger">Optional logger.</param>
    public McpSkillLoader(SkillsConfig config, ILogger<McpSkillLoader>? logger = null)
    {
        _config = config;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<KernelPlugin>> LoadSkillsAsync(
        CancellationToken cancellationToken = default)
    {
        var plugins = new List<KernelPlugin>();

        // Load MCP servers from configuration
        foreach (var serverConfig in _config.McpServers.Where(s => s.Enabled))
        {
            try
            {
                _logger?.LogInformation("Loading MCP server: {ServerName} (command: {Command} {Args})",
                    serverConfig.Name, serverConfig.Command, string.Join(" ", serverConfig.Args));
                var plugin = await LoadMcpServerAsync(serverConfig, cancellationToken);
                if (plugin != null)
                {
                    plugins.Add(plugin);
                    _logger?.LogInformation("Successfully loaded MCP server: {ServerName} with {ToolCount} tools",
                        serverConfig.Name, plugin.Count());
                }
                else
                {
                    _logger?.LogWarning("MCP server {ServerName} loaded but returned no tools", serverConfig.Name);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to load MCP server {ServerName}: {Message}", serverConfig.Name, ex.Message);
                // Also write to console for visibility even without logging
                Console.Error.WriteLine($"[MCP Error] Failed to load {serverConfig.Name}: {ex.Message}");
            }
        }

        // Also try to load from servers.json in the MCP folder
        var serversJsonPath = Path.Combine(_config.McpFolder, "servers.json");
        if (File.Exists(serversJsonPath))
        {
            try
            {
                var additionalServers = await LoadServersFromJsonAsync(serversJsonPath, cancellationToken);
                foreach (var serverConfig in additionalServers.Where(s => s.Enabled))
                {
                    // Skip if already loaded from main config
                    if (_config.McpServers.Any(s => s.Name == serverConfig.Name))
                        continue;

                    try
                    {
                        var plugin = await LoadMcpServerAsync(serverConfig, cancellationToken);
                        if (plugin != null)
                        {
                            plugins.Add(plugin);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Failed to load MCP server {ServerName} from servers.json", serverConfig.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to load servers.json from {Path}", serversJsonPath);
            }
        }

        return plugins;
    }

    private async Task<List<McpServerConfig>> LoadServersFromJsonAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var json = await File.ReadAllTextAsync(path, cancellationToken);
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var wrapper = JsonSerializer.Deserialize<McpServersWrapper>(json, options);
        return wrapper?.Servers ?? [];
    }

    private async Task<KernelPlugin?> LoadMcpServerAsync(
        McpServerConfig serverConfig,
        CancellationToken cancellationToken)
    {
        // Expand environment variables in the env dictionary
        var expandedEnv = new Dictionary<string, string>();
        foreach (var (key, value) in serverConfig.Env)
        {
            var expandedValue = ExpandEnvironmentValue(value);
            expandedEnv[key] = expandedValue;
        }

        // On Windows, we need to run commands through cmd.exe to properly handle .cmd/.bat files
        // and PATH resolution. This is because Process.Start doesn't use the shell by default.
        string command;
        string[] arguments;
        
        if (OperatingSystem.IsWindows())
        {
            // Use cmd.exe /c to run the command through the shell
            command = "cmd.exe";
            var cmdArgs = $"{serverConfig.Command} {string.Join(" ", serverConfig.Args)}";
            arguments = ["/c", cmdArgs];
            _logger?.LogInformation("Running via cmd.exe: cmd.exe /c {CmdArgs}", cmdArgs);
        }
        else
        {
            command = serverConfig.Command;
            arguments = [.. serverConfig.Args];
        }

        // Log the full command being executed
        var fullCommand = $"{command} {string.Join(" ", arguments)}";
        _logger?.LogInformation("Starting MCP server with command: {FullCommand}", fullCommand);
        Console.WriteLine($"[MCP] Starting: {fullCommand}");

        var clientTransport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = command,
            Arguments = arguments,
            EnvironmentVariables = expandedEnv!,
            Name = serverConfig.Name
        });

        // Create the MCP client with a timeout
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(30)); // 30 second timeout for server startup

        try
        {
            _logger?.LogInformation("Creating MCP client for {ServerName}...", serverConfig.Name);
            var client = await McpClient.CreateAsync(
                clientTransport,
                cancellationToken: timeoutCts.Token);

            _clients.Add(client);
            _logger?.LogInformation("MCP client created successfully for {ServerName}", serverConfig.Name);

            // Get tools from MCP server
            _logger?.LogInformation("Listing tools from MCP server {ServerName}...", serverConfig.Name);
            var tools = await client.ListToolsAsync();
            var toolsList = tools.ToList();

            _logger?.LogInformation("MCP server {ServerName} returned {ToolCount} tools",
                serverConfig.Name, toolsList.Count);

            if (toolsList.Count == 0)
            {
                _logger?.LogWarning("MCP server {ServerName} has no tools", serverConfig.Name);
                Console.WriteLine($"[MCP Warning] Server {serverConfig.Name} has no tools");
                return null;
            }

            // Log each tool
            foreach (var tool in toolsList)
            {
                _logger?.LogInformation("  - Tool: {ToolName}: {ToolDescription}",
                    tool.Name, tool.Description ?? "(no description)");
                Console.WriteLine($"[MCP] Tool found: {tool.Name}");
            }

            // Convert MCP tools to Kernel functions
            var functions = toolsList.Select(tool =>
                CreateKernelFunctionFromMcpTool(client, tool, serverConfig.Name));

            // Sanitize the plugin name - Semantic Kernel only allows ASCII letters, digits, and underscores
            var sanitizedName = SanitizePluginName(serverConfig.Name);
            _logger?.LogInformation("Sanitized plugin name from '{OriginalName}' to '{SanitizedName}'",
                serverConfig.Name, sanitizedName);

            return KernelPluginFactory.CreateFromFunctions(
                sanitizedName,
                serverConfig.Description,
                functions);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            _logger?.LogError("Timeout waiting for MCP server {ServerName} to start (30 seconds)", serverConfig.Name);
            Console.Error.WriteLine($"[MCP Error] Timeout waiting for {serverConfig.Name} to start");
            throw new TimeoutException($"MCP server {serverConfig.Name} did not start within 30 seconds");
        }
    }

    private KernelFunction CreateKernelFunctionFromMcpTool(
        McpClient client,
        McpClientTool tool,
        string serverName)
    {
        // Extract parameter metadata from the MCP tool's input schema
        var parameters = ExtractParameterMetadata(tool);

        // Create a kernel function that calls the MCP tool
        async Task<string> CallToolAsync(Kernel kernel, KernelArguments arguments)
        {
            try
            {
                // Convert KernelArguments to dictionary for MCP
                var mcpArgs = arguments.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value);

                var result = await client.CallToolAsync(
                    tool.Name,
                    mcpArgs,
                    cancellationToken: CancellationToken.None);

                // Return the result as string - extract text content
                if (result?.Content != null)
                {
                    var textContent = result.Content
                        .OfType<TextContentBlock>()
                        .Select(c => c.Text)
                        .FirstOrDefault();
                    return textContent ?? result.ToString() ?? string.Empty;
                }
                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error calling MCP tool {ToolName} on server {ServerName}",
                    tool.Name, serverName);
                return $"Error: {ex.Message}";
            }
        }

        // Create the function with proper parameter metadata
        var options = new KernelFunctionFromMethodOptions
        {
            FunctionName = tool.Name,
            Description = tool.Description ?? $"Tool from {serverName}",
            Parameters = parameters,
            ReturnParameter = new KernelReturnParameterMetadata
            {
                Description = "The result of the tool execution",
                ParameterType = typeof(string)
            }
        };

        return KernelFunctionFactory.CreateFromMethod(CallToolAsync, options);
    }

    /// <summary>
    /// Extracts parameter metadata from an MCP tool's input schema.
    /// </summary>
    /// <param name="tool">The MCP tool to extract parameters from.</param>
    /// <returns>A list of kernel parameter metadata.</returns>
    private List<KernelParameterMetadata> ExtractParameterMetadata(McpClientTool tool)
    {
        var parameters = new List<KernelParameterMetadata>();

        try
        {
            // The tool's JsonSchema property contains the input schema
            var inputSchema = tool.JsonSchema;
            if (inputSchema.ValueKind == JsonValueKind.Undefined || inputSchema.ValueKind == JsonValueKind.Null)
            {
                _logger?.LogDebug("MCP tool {ToolName} has no input schema", tool.Name);
                return parameters;
            }

            // Parse the JSON schema to extract properties
            if (inputSchema.TryGetProperty("properties", out var propertiesElement))
            {
                // Get required properties list
                var requiredProperties = new HashSet<string>();
                if (inputSchema.TryGetProperty("required", out var requiredElement) &&
                    requiredElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in requiredElement.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            requiredProperties.Add(item.GetString()!);
                        }
                    }
                }

                // Iterate through properties
                foreach (var property in propertiesElement.EnumerateObject())
                {
                    var paramName = property.Name;
                    var paramSchema = property.Value;

                    // Extract description
                    string? description = null;
                    if (paramSchema.TryGetProperty("description", out var descElement) &&
                        descElement.ValueKind == JsonValueKind.String)
                    {
                        description = descElement.GetString();
                    }

                    // Extract type and map to .NET type
                    var paramType = GetParameterType(paramSchema);

                    // Extract default value
                    object? defaultValue = null;
                    if (paramSchema.TryGetProperty("default", out var defaultElement))
                    {
                        defaultValue = GetDefaultValue(defaultElement, paramType);
                    }

                    var isRequired = requiredProperties.Contains(paramName);

                    var metadata = new KernelParameterMetadata(paramName)
                    {
                        Description = description ?? $"Parameter {paramName}",
                        ParameterType = paramType,
                        IsRequired = isRequired,
                        DefaultValue = defaultValue
                    };

                    parameters.Add(metadata);
                    _logger?.LogDebug(
                        "Extracted parameter {ParamName} (type: {ParamType}, required: {IsRequired}) from MCP tool {ToolName}",
                        paramName, paramType.Name, isRequired, tool.Name);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to extract parameter metadata from MCP tool {ToolName}", tool.Name);
        }

        return parameters;
    }

    /// <summary>
    /// Maps JSON schema type to .NET type.
    /// </summary>
    private static Type GetParameterType(JsonElement schema)
    {
        if (!schema.TryGetProperty("type", out var typeElement))
        {
            return typeof(object);
        }

        var typeString = typeElement.ValueKind == JsonValueKind.String
            ? typeElement.GetString()
            : null;

        return typeString switch
        {
            "string" => typeof(string),
            "integer" => typeof(int),
            "number" => typeof(double),
            "boolean" => typeof(bool),
            "array" => typeof(object[]),
            "object" => typeof(object),
            _ => typeof(object)
        };
    }

    /// <summary>
    /// Extracts default value from JSON element.
    /// </summary>
    private static object? GetDefaultValue(JsonElement element, Type targetType)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when targetType == typeof(int) => element.GetInt32(),
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => null
        };
    }

    /// <summary>
    /// Expands environment variable references in a value string.
    /// Supports the following syntax:
    /// - ${env:VAR_NAME} - Load from system environment variable
    /// - ${VAR_NAME} - Legacy syntax, load from system environment variable
    /// - %VAR_NAME% - Windows environment variable syntax
    /// - Plain value - Use as-is
    /// </summary>
    /// <param name="value">The value to expand.</param>
    /// <returns>The expanded value.</returns>
    private static string ExpandEnvironmentValue(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        // Handle ${env:VAR_NAME} syntax
        if (value.StartsWith("${env:") && value.EndsWith("}"))
        {
            var envVarName = value[6..^1]; // Remove "${env:" and "}"
            return Environment.GetEnvironmentVariable(envVarName) ?? string.Empty;
        }

        // Handle legacy ${VAR_NAME} syntax
        if (value.StartsWith("${") && value.EndsWith("}"))
        {
            var envVarName = value[2..^1]; // Remove "${" and "}"
            return Environment.GetEnvironmentVariable(envVarName) ?? string.Empty;
        }

        // Handle Windows %VAR_NAME% syntax via ExpandEnvironmentVariables
        return Environment.ExpandEnvironmentVariables(value);
    }

    /// <summary>
    /// Sanitizes a plugin name to only contain ASCII letters, digits, and underscores.
    /// Semantic Kernel requires plugin names to match this pattern.
    /// </summary>
    /// <param name="name">The original name.</param>
    /// <returns>A sanitized name safe for use as a plugin name.</returns>
    private static string SanitizePluginName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "UnnamedPlugin";

        // Replace hyphens and dots with underscores
        var sanitized = name.Replace('-', '_').Replace('.', '_');
        
        // Remove any other invalid characters (keep only ASCII letters, digits, underscores)
        var result = new System.Text.StringBuilder();
        foreach (var c in sanitized)
        {
            if (char.IsAsciiLetterOrDigit(c) || c == '_')
            {
                result.Append(c);
            }
        }

        // Ensure the name doesn't start with a digit
        if (result.Length > 0 && char.IsDigit(result[0]))
        {
            result.Insert(0, '_');
        }

        return result.Length > 0 ? result.ToString() : "UnnamedPlugin";
    }

    /// <summary>
    /// Resolves a command to its full path on Windows.
    /// On Windows, commands like "npx" need to be resolved to "npx.cmd" because
    /// Process.Start doesn't automatically resolve .cmd/.bat extensions.
    /// </summary>
    /// <param name="command">The command to resolve.</param>
    /// <returns>The resolved command path.</returns>
    private static string ResolveCommand(string command)
    {
        // Only apply Windows-specific resolution on Windows
        if (!OperatingSystem.IsWindows())
            return command;

        // If the command already has an extension, use it as-is
        if (Path.HasExtension(command))
            return command;

        // If the command contains a path separator, it's a path - use as-is
        if (command.Contains(Path.DirectorySeparatorChar) || command.Contains(Path.AltDirectorySeparatorChar))
            return command;

        // Try to find the command in PATH with common Windows extensions
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var paths = pathEnv.Split(Path.PathSeparator);
        var extensions = new[] { ".cmd", ".bat", ".exe", "" };

        foreach (var path in paths)
        {
            foreach (var ext in extensions)
            {
                var fullPath = Path.Combine(path, command + ext);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
        }

        // If not found, return the original command and let the system handle it
        return command;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        foreach (var client in _clients)
        {
            try
            {
                await client.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error disposing MCP client");
            }
        }

        _clients.Clear();
        _disposed = true;
    }

    /// <summary>
    /// Wrapper class for deserializing servers.json.
    /// </summary>
    private class McpServersWrapper
    {
        public List<McpServerConfig> Servers { get; set; } = [];
    }
}
