namespace Microbot.Console.Services;

using System.Text.Json;
using Spectre.Console;
using Microbot.Core.Models;
using Microbot.Core.Models.McpRegistry;
using Microbot.Core.Services;

/// <summary>
/// Service for managing MCP servers from the official MCP Registry.
/// </summary>
public class McpRegistryService : IDisposable
{
    private readonly McpRegistryClient _client;
    private readonly ConsoleUIService _ui;
    private bool _disposed;

    /// <summary>
    /// Creates a new MCP Registry service.
    /// </summary>
    /// <param name="ui">Console UI service for user interaction.</param>
    public McpRegistryService(ConsoleUIService ui)
    {
        _client = new McpRegistryClient();
        _ui = ui;
    }

    /// <summary>
    /// Lists MCP servers from the registry with pagination.
    /// </summary>
    /// <param name="search">Optional search query.</param>
    /// <param name="pageSize">Number of items per page (default: 50).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ListServersAsync(
        string? search = null,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        string? cursor = null;
        var pageNumber = 1;
        var totalFetched = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            var response = await _client.ListServersAsync(pageSize, cursor, search, cancellationToken);
            var servers = response.Servers.Select(s => s.Server).ToList();

            if (servers.Count == 0)
            {
                if (pageNumber == 1)
                {
                    _ui.DisplayWarning(string.IsNullOrEmpty(search)
                        ? "No MCP servers found in the registry."
                        : $"No MCP servers found matching '{search}'.");
                }
                break;
            }

            totalFetched += servers.Count;

            // Display the servers
            DisplayServerList(servers, pageNumber, totalFetched, response.Metadata?.NextCursor != null);

            // Check if there are more pages
            if (string.IsNullOrEmpty(response.Metadata?.NextCursor))
            {
                _ui.DisplayInfo($"End of list. Total servers: {totalFetched}");
                break;
            }

            // Prompt for next page
            _ui.DisplayInfo("Press Enter to load more, or type 'q' to quit...");
            var input = System.Console.ReadLine();
            if (input?.Trim().ToLowerInvariant() == "q")
            {
                break;
            }

            cursor = response.Metadata.NextCursor;
            pageNumber++;
        }
    }

    /// <summary>
    /// Displays a list of servers in a formatted table.
    /// </summary>
    private void DisplayServerList(List<McpRegistryServer> servers, int pageNumber, int totalFetched, bool hasMore)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .AddColumn(new TableColumn("[cyan]Name[/]"))
            .AddColumn(new TableColumn("[cyan]Version[/]"))
            .AddColumn(new TableColumn("[cyan]Type[/]"))
            .AddColumn(new TableColumn("[cyan]Description[/]"));

        foreach (var server in servers)
        {
            var packageType = server.PreferredPackage?.RegistryType ?? "unknown";
            var description = server.Description;
            if (description.Length > 60)
            {
                description = description[..57] + "...";
            }

            table.AddRow(
                Markup.Escape(server.Name),
                Markup.Escape(server.Version),
                $"[blue]{packageType}[/]",
                Markup.Escape(description)
            );
        }

        var title = hasMore
            ? $"[cyan]MCP Registry Servers (Page {pageNumber}, showing {totalFetched} so far)[/]"
            : $"[cyan]MCP Registry Servers ({totalFetched} total)[/]";

        AnsiConsole.Write(new Panel(table)
        {
            Header = new PanelHeader(title),
            Border = BoxBorder.Rounded,
            Padding = new Padding(1, 0)
        });
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Installs an MCP server from the registry.
    /// </summary>
    /// <param name="serverName">The server name to install.</param>
    /// <param name="config">The current Microbot configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if installation was successful.</returns>
    public async Task<bool> InstallServerAsync(
        string serverName,
        MicrobotConfig config,
        CancellationToken cancellationToken = default)
    {
        // Check if already installed
        var existingServer = config.Skills.McpServers
            .FirstOrDefault(s => s.RegistryName?.Equals(serverName, StringComparison.OrdinalIgnoreCase) == true
                              || s.Name.Equals(serverName, StringComparison.OrdinalIgnoreCase));

        if (existingServer != null)
        {
            _ui.DisplayWarning($"Server '{serverName}' is already installed as '{existingServer.Name}'.");
            if (!AnsiConsole.Confirm("[cyan]Would you like to reinstall/update it?[/]", false))
            {
                return false;
            }
            // Remove existing for reinstall
            config.Skills.McpServers.Remove(existingServer);
        }

        // Fetch server details from registry
        _ui.DisplayInfo($"Fetching server details for '{serverName}'...");
        var server = await _client.GetServerAsync(serverName, "latest", cancellationToken);

        if (server == null)
        {
            _ui.DisplayError($"Server '{serverName}' not found in the MCP Registry.");
            _ui.DisplayInfo("Use '/mcp list' to see available servers, or '/mcp list <search>' to search.");
            return false;
        }

        // Get the preferred package
        var package = server.PreferredPackage;
        if (package == null)
        {
            _ui.DisplayError($"Server '{serverName}' has no installable packages.");
            return false;
        }

        // Create the server configuration
        var serverConfig = CreateServerConfig(server, package);

        // Add to configuration
        config.Skills.McpServers.Add(serverConfig);

        _ui.DisplaySuccess($"Server '{server.DisplayName}' v{server.Version} added to configuration.");

        // Display environment variable requirements
        DisplayEnvironmentVariableRequirements(server, serverConfig);

        return true;
    }

    /// <summary>
    /// Creates an McpServerConfig from registry server data.
    /// </summary>
    private static McpServerConfig CreateServerConfig(McpRegistryServer server, McpRegistryPackage package)
    {
        // Create a safe name for the config (replace / with -)
        var safeName = server.Name.Replace("/", "-").Replace(".", "-");

        var config = new McpServerConfig
        {
            Name = safeName,
            Description = server.Description,
            Command = package.GetCommand(),
            Args = package.GetArguments(),
            Enabled = false, // Disabled by default until configured
            RegistryName = server.Name,
            RegistryVersion = server.Version,
            RegistryPackageType = package.RegistryType,
            RegistryPackageId = package.Identifier,
            Env = [],
            EnvVarDefinitions = []
        };

        // Add environment variable definitions
        if (server.EnvironmentVariables != null)
        {
            foreach (var envVar in server.EnvironmentVariables)
            {
                config.EnvVarDefinitions.Add(new McpEnvVarDefinition
                {
                    Name = envVar.Name,
                    Description = envVar.Description,
                    IsRequired = envVar.IsRequired,
                    IsSecret = envVar.IsSecret,
                    Default = envVar.Default
                });

                // Add placeholder in env dictionary
                if (!string.IsNullOrEmpty(envVar.Default))
                {
                    config.Env[envVar.Name] = envVar.Default;
                }
                else
                {
                    config.Env[envVar.Name] = $"${{env:{envVar.Name}}}";
                }
            }
        }

        return config;
    }

    /// <summary>
    /// Displays environment variable requirements for a server.
    /// </summary>
    private void DisplayEnvironmentVariableRequirements(McpRegistryServer server, McpServerConfig config)
    {
        var envVars = server.EnvironmentVariables;
        if (envVars == null || envVars.Count == 0)
        {
            _ui.DisplayInfo("This server does not require any environment variables.");
            return;
        }

        var requiredVars = envVars.Where(v => v.IsRequired).ToList();
        var optionalVars = envVars.Where(v => !v.IsRequired).ToList();

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]⚠ This MCP server requires configuration:[/]");
        AnsiConsole.WriteLine();

        if (requiredVars.Count > 0)
        {
            AnsiConsole.MarkupLine("[red]Required environment variables:[/]");
            foreach (var envVar in requiredVars)
            {
                var secretTag = envVar.IsSecret ? " [grey](secret)[/]" : "";
                AnsiConsole.MarkupLine($"  • [white]{envVar.Name}[/]{secretTag}");
                if (!string.IsNullOrEmpty(envVar.Description))
                {
                    AnsiConsole.MarkupLine($"    [grey]{Markup.Escape(envVar.Description)}[/]");
                }
            }
            AnsiConsole.WriteLine();
        }

        if (optionalVars.Count > 0)
        {
            AnsiConsole.MarkupLine("[blue]Optional environment variables:[/]");
            foreach (var envVar in optionalVars)
            {
                var defaultTag = !string.IsNullOrEmpty(envVar.Default) ? $" [grey](default: {envVar.Default})[/]" : "";
                AnsiConsole.MarkupLine($"  • [white]{envVar.Name}[/]{defaultTag}");
                if (!string.IsNullOrEmpty(envVar.Description))
                {
                    AnsiConsole.MarkupLine($"    [grey]{Markup.Escape(envVar.Description)}[/]");
                }
            }
            AnsiConsole.WriteLine();
        }

        // Show configuration instructions
        AnsiConsole.MarkupLine("[cyan]To configure, edit your Microbot.config file:[/]");
        AnsiConsole.WriteLine();

        var exampleEnv = new Dictionary<string, string>();
        foreach (var envVar in envVars.Take(3))
        {
            exampleEnv[envVar.Name] = envVar.IsSecret
                ? "${env:" + envVar.Name + "}"
                : "<your-value>";
        }

        var jsonExample = JsonSerializer.Serialize(new
        {
            skills = new
            {
                mcpServers = new[]
                {
                    new
                    {
                        name = config.Name,
                        env = exampleEnv,
                        enabled = true
                    }
                }
            }
        }, new JsonSerializerOptions { WriteIndented = true });

        AnsiConsole.Write(new Panel(new Text(jsonExample))
        {
            Border = BoxBorder.Rounded,
            Padding = new Padding(1, 0)
        });

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Tip: Use ${env:VAR_NAME} syntax to load values from system environment variables.[/]");
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Gets details about a specific server.
    /// </summary>
    /// <param name="serverName">The server name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ShowServerDetailsAsync(
        string serverName,
        CancellationToken cancellationToken = default)
    {
        var server = await _client.GetServerAsync(serverName, "latest", cancellationToken);

        if (server == null)
        {
            _ui.DisplayError($"Server '{serverName}' not found.");
            return;
        }

        var table = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn("Property")
            .AddColumn("Value");

        table.AddRow("[cyan]Name[/]", Markup.Escape(server.Name));
        table.AddRow("[cyan]Title[/]", Markup.Escape(server.DisplayName));
        table.AddRow("[cyan]Version[/]", Markup.Escape(server.Version));
        table.AddRow("[cyan]Description[/]", Markup.Escape(server.Description));

        if (server.Repository != null)
        {
            table.AddRow("[cyan]Repository[/]", Markup.Escape(server.Repository.Url));
        }

        if (!string.IsNullOrEmpty(server.License))
        {
            table.AddRow("[cyan]License[/]", Markup.Escape(server.License));
        }

        if (server.Packages.Count > 0)
        {
            var packages = string.Join(", ", server.Packages.Select(p => $"{p.RegistryType}: {p.Identifier}"));
            table.AddRow("[cyan]Packages[/]", Markup.Escape(packages));
        }

        if (server.EnvironmentVariables?.Count > 0)
        {
            var envVars = string.Join(", ", server.EnvironmentVariables.Select(e =>
                e.IsRequired ? $"{e.Name} (required)" : e.Name));
            table.AddRow("[cyan]Env Vars[/]", Markup.Escape(envVars));
        }

        AnsiConsole.Write(new Panel(table)
        {
            Header = new PanelHeader($"[cyan]MCP Server: {Markup.Escape(server.DisplayName)}[/]"),
            Border = BoxBorder.Rounded,
            Padding = new Padding(2, 1)
        });
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Disposes the service.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _client.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
