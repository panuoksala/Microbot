namespace Microbot.Skills.Loaders;

using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microbot.Core.Interfaces;
using Microbot.Core.Models;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

/// <summary>
/// Loads the built-in Browser skill using Playwright MCP server.
/// This provides web automation capabilities including navigation, clicking,
/// form filling, screenshots, and more.
/// </summary>
public class BrowserSkillLoader : ISkillLoader, IAsyncDisposable
{
    private readonly BrowserSkillConfig _config;
    private readonly ILogger<BrowserSkillLoader>? _logger;
    private McpClient? _mcpClient;
    private bool _disposed;

    /// <inheritdoc />
    public string LoaderName => "Browser";

    /// <summary>
    /// Creates a new Browser skill loader.
    /// </summary>
    /// <param name="config">Browser skill configuration.</param>
    /// <param name="logger">Optional logger.</param>
    public BrowserSkillLoader(
        BrowserSkillConfig config,
        ILogger<BrowserSkillLoader>? logger = null)
    {
        _config = config;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<KernelPlugin>> LoadSkillsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_config.Enabled)
        {
            _logger?.LogInformation("Browser skill is disabled");
            return [];
        }

        // Check if Node.js is available
        if (!IsNodeJsAvailable())
        {
            _logger?.LogWarning(
                "Browser skill requires Node.js but it was not found. " +
                "Please install Node.js from https://nodejs.org/ to enable browser automation.");
            Console.WriteLine("[Browser] Warning: Node.js not found. Browser skill will be disabled.");
            return [];
        }

        _logger?.LogInformation("Starting Playwright MCP server for Browser skill...");
        Console.WriteLine("[Browser] Starting Playwright MCP server...");

        try
        {
            // Build command arguments
            var args = BuildCommandArgs();

            // Determine command based on OS
            string command;
            string[] arguments;

            if (OperatingSystem.IsWindows())
            {
                // Use cmd.exe to run npx on Windows
                command = "cmd.exe";
                var cmdArgs = $"npx {string.Join(" ", args)}";
                arguments = ["/c", cmdArgs];
                _logger?.LogInformation("Running via cmd.exe: cmd.exe /c {CmdArgs}", cmdArgs);
            }
            else
            {
                command = "npx";
                arguments = args;
            }

            // Log the full command
            var fullCommand = $"{command} {string.Join(" ", arguments)}";
            _logger?.LogInformation("Starting Browser MCP server with command: {FullCommand}", fullCommand);
            Console.WriteLine($"[Browser] Command: {fullCommand}");

            // Create MCP client transport
            var transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Command = command,
                Arguments = arguments,
                Name = "playwright-browser"
            });

            // Create MCP client with timeout
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(60)); // 60 second startup timeout

            _mcpClient = await McpClient.CreateAsync(transport, cancellationToken: timeoutCts.Token);

            // Get available tools
            var tools = await _mcpClient.ListToolsAsync();
            var toolsList = tools.ToList();

            _logger?.LogInformation("Playwright MCP server started with {ToolCount} tools", toolsList.Count);
            Console.WriteLine($"[Browser] Loaded {toolsList.Count} browser tools");

            // Log each tool
            foreach (var tool in toolsList)
            {
                _logger?.LogDebug("  - Browser tool: {ToolName}: {ToolDescription}",
                    tool.Name, tool.Description ?? "(no description)");
            }

            // Convert MCP tools to Kernel functions
            var functions = toolsList.Select(tool => CreateKernelFunction(tool));

            return [KernelPluginFactory.CreateFromFunctions(
                "Browser",
                "Web browser automation using Playwright - navigate pages, click elements, fill forms, take screenshots, and more",
                functions)];
        }
        catch (OperationCanceledException)
        {
            _logger?.LogError("Timeout waiting for Playwright MCP server to start (60 seconds)");
            Console.Error.WriteLine("[Browser Error] Timeout waiting for Playwright MCP server to start");
            return [];
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to start Playwright MCP server: {Message}", ex.Message);
            Console.Error.WriteLine($"[Browser Error] Failed to start: {ex.Message}");
            return [];
        }
    }

    /// <summary>
    /// Builds command line arguments for the Playwright MCP server.
    /// </summary>
    private string[] BuildCommandArgs()
    {
        var args = new List<string> { "@playwright/mcp@latest" };

        // Browser selection
        args.Add($"--browser={_config.Browser}");

        // Headless mode
        if (_config.Headless)
            args.Add("--headless");

        // Viewport
        args.Add($"--viewport-size={_config.ViewportWidth}x{_config.ViewportHeight}");

        // Timeouts
        args.Add($"--timeout-action={_config.ActionTimeoutMs}");
        args.Add($"--timeout-navigation={_config.NavigationTimeoutMs}");

        // Isolated sessions
        if (_config.Isolated)
        {
            args.Add("--isolated");
        }
        else if (!string.IsNullOrEmpty(_config.UserDataDir))
        {
            args.Add($"--user-data-dir={_config.UserDataDir}");
        }

        // Capabilities
        if (_config.Capabilities.Count > 0)
        {
            args.Add($"--caps={string.Join(",", _config.Capabilities)}");
        }

        // Output directory
        if (!string.IsNullOrEmpty(_config.OutputDir))
        {
            // Ensure output directory exists
            try
            {
                Directory.CreateDirectory(_config.OutputDir);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to create browser output directory: {OutputDir}", _config.OutputDir);
            }
            args.Add($"--output-dir={_config.OutputDir}");
        }

        // Proxy
        if (!string.IsNullOrEmpty(_config.ProxyServer))
        {
            args.Add($"--proxy-server={_config.ProxyServer}");
        }

        // Blocked origins
        if (_config.BlockedOrigins.Count > 0)
        {
            args.Add($"--blocked-origins={string.Join(";", _config.BlockedOrigins)}");
        }

        // Device emulation
        if (!string.IsNullOrEmpty(_config.Device))
        {
            args.Add($"--device={_config.Device}");
        }

        return args.ToArray();
    }

    /// <summary>
    /// Checks if Node.js is available on the system.
    /// </summary>
    private bool IsNodeJsAvailable()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "node",
                Arguments = OperatingSystem.IsWindows() ? "/c node --version" : "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
                return false;

            process.WaitForExit(5000); // 5 second timeout
            var output = process.StandardOutput.ReadToEnd();
            
            _logger?.LogDebug("Node.js version check: {Output}", output.Trim());
            return process.ExitCode == 0 && output.StartsWith("v");
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Node.js availability check failed");
            return false;
        }
    }

    /// <summary>
    /// Creates a Kernel function from an MCP tool.
    /// </summary>
    private KernelFunction CreateKernelFunction(McpClientTool tool)
    {
        // Extract parameter metadata from the MCP tool's input schema
        var parameters = ExtractParameterMetadata(tool);

        // Create a kernel function that calls the MCP tool
        async Task<string> CallToolAsync(Kernel kernel, KernelArguments arguments)
        {
            if (_mcpClient == null)
            {
                return "Error: Browser MCP client is not initialized";
            }

            try
            {
                // Convert KernelArguments to dictionary for MCP
                var mcpArgs = arguments.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value);

                var result = await _mcpClient.CallToolAsync(
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
                _logger?.LogError(ex, "Error calling Browser tool {ToolName}", tool.Name);
                return $"Error: {ex.Message}";
            }
        }

        // Create the function with proper parameter metadata
        var options = new KernelFunctionFromMethodOptions
        {
            FunctionName = tool.Name,
            Description = tool.Description ?? $"Browser tool: {tool.Name}",
            Parameters = parameters,
            ReturnParameter = new KernelReturnParameterMetadata
            {
                Description = "The result of the browser operation",
                ParameterType = typeof(string)
            }
        };

        return KernelFunctionFactory.CreateFromMethod(CallToolAsync, options);
    }

    /// <summary>
    /// Extracts parameter metadata from an MCP tool's input schema.
    /// </summary>
    private List<KernelParameterMetadata> ExtractParameterMetadata(McpClientTool tool)
    {
        var parameters = new List<KernelParameterMetadata>();

        try
        {
            var inputSchema = tool.JsonSchema;
            if (inputSchema.ValueKind == JsonValueKind.Undefined || inputSchema.ValueKind == JsonValueKind.Null)
            {
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
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to extract parameter metadata from Browser tool {ToolName}", tool.Name);
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

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        if (_mcpClient != null)
        {
            try
            {
                await _mcpClient.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error disposing Browser MCP client");
            }
        }

        _disposed = true;
    }
}
