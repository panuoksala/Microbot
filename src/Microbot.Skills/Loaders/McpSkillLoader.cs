namespace Microbot.Skills.Loaders;

using System.Text.Json;
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
                _logger?.LogInformation("Loading MCP server: {ServerName}", serverConfig.Name);
                var plugin = await LoadMcpServerAsync(serverConfig, cancellationToken);
                if (plugin != null)
                {
                    plugins.Add(plugin);
                    _logger?.LogInformation("Successfully loaded MCP server: {ServerName}", serverConfig.Name);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to load MCP server {ServerName}", serverConfig.Name);
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
            var expandedValue = Environment.ExpandEnvironmentVariables(value);
            // Also handle ${VAR} syntax
            if (expandedValue.StartsWith("${") && expandedValue.EndsWith("}"))
            {
                var envVarName = expandedValue[2..^1];
                expandedValue = Environment.GetEnvironmentVariable(envVarName) ?? string.Empty;
            }
            expandedEnv[key] = expandedValue;
        }

        var clientTransport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = serverConfig.Command,
            Arguments = [.. serverConfig.Args],
            EnvironmentVariables = expandedEnv!,
            Name = serverConfig.Name
        });

        var client = await McpClient.CreateAsync(
            clientTransport,
            cancellationToken: cancellationToken);

        _clients.Add(client);

        // Get tools from MCP server
        var tools = await client.ListToolsAsync();

        if (!tools.Any())
        {
            _logger?.LogWarning("MCP server {ServerName} has no tools", serverConfig.Name);
            return null;
        }

        // Convert MCP tools to Kernel functions
        var functions = tools.Select(tool =>
            CreateKernelFunctionFromMcpTool(client, tool, serverConfig.Name));

        return KernelPluginFactory.CreateFromFunctions(
            serverConfig.Name,
            serverConfig.Description,
            functions);
    }

    private KernelFunction CreateKernelFunctionFromMcpTool(
        McpClient client,
        McpClientTool tool,
        string serverName)
    {
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

        return KernelFunctionFactory.CreateFromMethod(
            CallToolAsync,
            tool.Name,
            tool.Description ?? $"Tool from {serverName}");
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
