using Microsoft.Extensions.Logging;
using Microbot.Console.Services;
using Microbot.Core.Configuration;
using Microbot.Core.Models;
using Spectre.Console;

namespace Microbot.Console;

/// <summary>
/// Main entry point for the Microbot console application.
/// </summary>
public class Program
{
    private static ConsoleUIService _ui = null!;
    private static ConfigurationService _configService = null!;
    private static AgentService _agentService = null!;
    private static MicrobotConfig _config = null!;
    private static ILoggerFactory? _loggerFactory;
    private static CancellationTokenSource _cts = new();

    public static async Task<int> Main(string[] args)
    {
        // Initialize UI service
        _ui = new ConsoleUIService();
        
        // Display header
        _ui.DisplayHeader();

        try
        {
            // Initialize configuration
            await InitializeConfigurationAsync();

            // Setup logging if verbose mode is enabled
            if (_config.Preferences.VerboseLogging)
            {
                _loggerFactory = LoggerFactory.Create(builder =>
                {
                    builder.AddConsole();
                    builder.SetMinimumLevel(LogLevel.Debug);
                });
            }

            // Initialize the agent
            await InitializeAgentAsync();

            // Display welcome and skills summary
            _ui.DisplayWelcome(_config);
            
            if (_agentService.SkillManager != null)
            {
                _ui.DisplaySkillsSummary(_agentService.SkillManager.GetSkillSummaries());
            }

            // Start the main chat loop
            await RunChatLoopAsync();

            return 0;
        }
        catch (Exception ex)
        {
            _ui.DisplayError($"Fatal error: {ex.Message}");
            if (_config?.Preferences.VerboseLogging == true)
            {
                AnsiConsole.WriteException(ex);
            }
            return 1;
        }
        finally
        {
            // Cleanup
            if (_agentService != null)
            {
                await _agentService.DisposeAsync();
            }
            _loggerFactory?.Dispose();
        }
    }

    /// <summary>
    /// Initializes the configuration, creating a new config file if needed.
    /// </summary>
    private static async Task InitializeConfigurationAsync()
    {
        _configService = new ConfigurationService();

        if (!_configService.ConfigurationExists())
        {
            _ui.DisplayFirstTimeSetup();
            
            // Create default configuration
            _config = new MicrobotConfig();
            
            // TODO: Run initialization wizard here
            // For now, we'll create a default config and prompt for essential settings
            await RunMinimalSetupAsync();
            
            // Save the configuration
            await _configService.SaveConfigurationAsync(_config);
            _ui.DisplaySuccess($"Configuration saved to {_configService.ConfigurationPath}");
        }
        else
        {
            _config = await _configService.LoadConfigurationAsync();
            _ui.DisplaySuccess("Configuration loaded successfully");
        }
    }

    /// <summary>
    /// Runs a minimal setup wizard for first-time configuration.
    /// </summary>
    private static Task RunMinimalSetupAsync()
    {
        _ui.DisplayInfo("Please configure your AI provider settings.");
        AnsiConsole.WriteLine();

        // Select AI provider
        var provider = _ui.SelectOption(
            "Select your AI provider:",
            new[] { "AzureOpenAI", "OpenAI", "Ollama" });
        _config.AiProvider.Provider = provider;

        // Get model ID
        var defaultModel = provider switch
        {
            "AzureOpenAI" => "gpt-4o",
            "OpenAI" => "gpt-4o",
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

        // Agent name
        _config.Preferences.AgentName = _ui.PromptText(
            "Enter a name for your AI assistant (default: Microbot):",
            "Microbot");

        // Outlook skill configuration
        AnsiConsole.WriteLine();
        ConfigureOutlookSkill();

        AnsiConsole.WriteLine();
        _ui.DisplaySuccess("Initial setup complete!");
        
        return Task.CompletedTask;
    }

    /// <summary>
    /// Configures the Outlook skill settings.
    /// </summary>
    private static void ConfigureOutlookSkill()
    {
        var enableOutlook = AnsiConsole.Confirm(
            "[cyan]Would you like to enable the Outlook skill?[/] (requires Azure AD app registration)",
            false);

        if (!enableOutlook)
        {
            _config.Skills.Outlook.Enabled = false;
            return;
        }

        _config.Skills.Outlook.Enabled = true;

        // Mode selection
        var mode = _ui.SelectOption(
            "Select Outlook skill permission mode:",
            new[] { "ReadOnly", "ReadWriteCalendar", "Full" });
        _config.Skills.Outlook.Mode = mode;

        // Display mode description
        var modeDescription = mode switch
        {
            "ReadOnly" => "Read emails and calendar events only",
            "ReadWriteCalendar" => "Read emails, read/write calendar events",
            "Full" => "Read/send emails, read/write calendar events",
            _ => ""
        };
        _ui.DisplayInfo($"Mode: {modeDescription}");
        AnsiConsole.WriteLine();

        // Client ID
        _config.Skills.Outlook.ClientId = _ui.PromptText(
            "Enter your Azure AD Application (Client) ID:");

        // Tenant ID
        _config.Skills.Outlook.TenantId = _ui.PromptText(
            "Enter your Tenant ID (or 'common' for multi-tenant):",
            "common");

        // Authentication method
        var authMethod = _ui.SelectOption(
            "Select authentication method:",
            new[] { "DeviceCode", "InteractiveBrowser" });
        _config.Skills.Outlook.AuthenticationMethod = authMethod;

        if (authMethod == "InteractiveBrowser")
        {
            _config.Skills.Outlook.RedirectUri = _ui.PromptText(
                "Enter Redirect URI:",
                "http://localhost");
        }

        _ui.DisplaySuccess("Outlook skill configured!");
        _ui.DisplayInfo("Note: You will be prompted to authenticate when the skill is first used.");
    }

    /// <summary>
    /// Initializes the AI agent with skills.
    /// </summary>
    private static async Task InitializeAgentAsync()
    {
        // Create device code callback for Outlook authentication
        Action<string>? deviceCodeCallback = null;
        if (_config.Skills.Outlook?.Enabled == true &&
            _config.Skills.Outlook.AuthenticationMethod?.Equals("DeviceCode", StringComparison.OrdinalIgnoreCase) == true)
        {
            deviceCodeCallback = message =>
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[yellow]Outlook Authentication Required[/]");
                AnsiConsole.MarkupLine($"[cyan]{Markup.Escape(message)}[/]");
                AnsiConsole.WriteLine();
            };
        }

        _agentService = new AgentService(_config, _loggerFactory, deviceCodeCallback);

        await _ui.WithSpinnerAsync("Initializing AI agent and loading skills...", async () =>
        {
            await _agentService.InitializeAsync(_cts.Token);
        });

        // Wire up agent loop events for progress display (if enabled in config)
        if (_config.AgentLoop.ShowFunctionCallProgress)
        {
            WireUpAgentLoopEvents();
        }

        _ui.DisplaySuccess("Agent initialized successfully");
        
        // Display agent loop configuration
        _ui.DisplayInfo($"Agent loop safety: max {_config.AgentLoop.MaxIterations} iterations, " +
                       $"max {_config.AgentLoop.MaxTotalFunctionCalls} function calls, " +
                       $"{_config.AgentLoop.RuntimeTimeoutSeconds}s timeout");
    }

    /// <summary>
    /// Wires up agent loop events to display function call progress.
    /// </summary>
    private static void WireUpAgentLoopEvents()
    {
        _agentService.FunctionInvoking += (sender, e) =>
        {
            _ui.DisplayFunctionInvoking(e);
        };

        _agentService.FunctionInvoked += (sender, e) =>
        {
            _ui.DisplayFunctionInvoked(e);
        };

        _agentService.SafetyLimitReached += (sender, e) =>
        {
            _ui.DisplaySafetyLimitReached(e);
        };

        _agentService.FunctionTimedOut += (sender, e) =>
        {
            _ui.DisplayFunctionTimeout(e);
        };
    }

    /// <summary>
    /// Runs the main chat loop.
    /// </summary>
    private static async Task RunChatLoopAsync()
    {
        _ui.DisplayRule("Chat");
        _ui.DisplayInfo("Type your message or use /help for commands. Type /exit to quit.");
        AnsiConsole.WriteLine();

        while (!_cts.Token.IsCancellationRequested)
        {
            var input = _ui.GetUserInput();

            if (string.IsNullOrWhiteSpace(input))
                continue;

            // Handle commands
            if (input.StartsWith('/'))
            {
                var handled = await HandleCommandAsync(input);
                if (!handled)
                    break; // Exit command
                continue;
            }

            // Send message to agent
            try
            {
                if (_config.Preferences.UseStreaming)
                {
                    await _ui.DisplayStreamingResponseAsync(
                        _agentService.ChatStreamingAsync(input, _cts.Token),
                        _config.Preferences.AgentName);
                }
                else
                {
                    var response = await _ui.WithSpinnerAsync(
                        "Thinking...",
                        () => _agentService.ChatAsync(input, _cts.Token));
                    _ui.DisplayAgentResponse(response, _config.Preferences.AgentName);
                }
            }
            catch (OperationCanceledException)
            {
                _ui.DisplayWarning("Operation cancelled");
            }
            catch (Exception ex)
            {
                _ui.DisplayError($"Error: {ex.Message}");
                if (_config.Preferences.VerboseLogging)
                {
                    AnsiConsole.WriteException(ex);
                }
            }
        }

        _ui.DisplayGoodbye();
    }

    /// <summary>
    /// Handles slash commands.
    /// </summary>
    /// <param name="command">The command to handle.</param>
    /// <returns>True if the loop should continue, false to exit.</returns>
    private static async Task<bool> HandleCommandAsync(string command)
    {
        // Split preserving original case for skill names
        var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cmd = parts[0].ToLowerInvariant();

        switch (cmd)
        {
            case "/help":
                _ui.DisplayHelp();
                break;

            case "/skills":
                await HandleSkillsCommandAsync(parts);
                break;

            case "/mcp":
                await HandleMcpCommandAsync(parts);
                break;

            case "/clear":
                _ui.DisplayHeader();
                _agentService.ClearHistory();
                _ui.DisplaySuccess("Screen and chat history cleared");
                break;

            case "/config":
                DisplayCurrentConfig();
                break;

            case "/history":
                _ui.DisplayInfo($"Chat history contains {_agentService.GetHistoryCount()} messages");
                break;

            case "/reload":
                await _ui.WithSpinnerAsync("Reloading configuration...", async () =>
                {
                    _config = await _configService.LoadConfigurationAsync();
                });
                _ui.DisplaySuccess("Configuration reloaded");
                break;

            case "/exit":
            case "/quit":
                return false;

            default:
                _ui.DisplayWarning($"Unknown command: {cmd}. Type /help for available commands.");
                break;
        }

        return true;
    }

    /// <summary>
    /// Handles /skills subcommands.
    /// </summary>
    /// <param name="parts">The command parts.</param>
    private static async Task HandleSkillsCommandAsync(string[] parts)
    {
        if (parts.Length < 2)
        {
            // Default: show loaded skills (existing behavior)
            if (_agentService.SkillManager != null)
            {
                _ui.DisplaySkillsSummary(_agentService.SkillManager.GetSkillSummaries());
            }
            else
            {
                _ui.DisplayWarning("No skills loaded");
            }
            return;
        }

        var subCommand = parts[1].ToLowerInvariant();

        switch (subCommand)
        {
            case "avail":
            case "available":
                DisplayAvailableSkills();
                break;

            case "config":
            case "configure":
                if (parts.Length < 3)
                {
                    _ui.DisplayWarning("Usage: /skills config <skillname>");
                    _ui.DisplayInfo("Example: /skills config outlook");
                    return;
                }
                await ConfigureSkillAsync(parts[2]);
                break;

            default:
                // Treat as showing loaded skills (existing behavior)
                if (_agentService.SkillManager != null)
                {
                    _ui.DisplaySkillsSummary(_agentService.SkillManager.GetSkillSummaries());
                }
                else
                {
                    _ui.DisplayWarning("No skills loaded");
                }
                break;
        }
    }

    /// <summary>
    /// Displays available skills.
    /// </summary>
    private static void DisplayAvailableSkills()
    {
        if (_agentService.SkillManager != null)
        {
            var availableSkills = _agentService.SkillManager.GetAvailableSkills();
            _ui.DisplayAvailableSkills(availableSkills);
        }
        else
        {
            _ui.DisplayWarning("Skill manager not initialized");
        }
    }

    /// <summary>
    /// Configures a skill interactively.
    /// </summary>
    /// <param name="skillId">The skill ID to configure.</param>
    private static async Task ConfigureSkillAsync(string skillId)
    {
        var configService = new SkillConfigurationService(_ui);
        
        if (configService.ConfigureSkill(skillId, _config))
        {
            // Save the updated configuration
            await _configService.SaveConfigurationAsync(_config);
            _ui.DisplaySuccess("Configuration saved.");
            
            // Prompt to reload
            if (AnsiConsole.Confirm("[cyan]Would you like to reload skills now?[/]", true))
            {
                await _ui.WithSpinnerAsync("Reloading skills...", async () =>
                {
                    await _agentService.ReloadSkillsAsync(_config, _cts.Token);
                });
                _ui.DisplaySuccess("Skills reloaded.");
                
                if (_agentService.SkillManager != null)
                {
                    _ui.DisplaySkillsSummary(_agentService.SkillManager.GetSkillSummaries());
                }
            }
        }
    }

    /// <summary>
    /// Handles /mcp subcommands for MCP Registry operations.
    /// </summary>
    /// <param name="parts">The command parts.</param>
    private static async Task HandleMcpCommandAsync(string[] parts)
    {
        if (parts.Length < 2)
        {
            DisplayMcpHelp();
            return;
        }

        var subCommand = parts[1].ToLowerInvariant();

        using var registryService = new McpRegistryService(_ui);

        switch (subCommand)
        {
            case "list":
                // /mcp list [search]
                var search = parts.Length > 2 ? string.Join(" ", parts.Skip(2)) : null;
                await _ui.WithSpinnerAsync("Fetching MCP servers from registry...", async () =>
                {
                    await registryService.ListServersAsync(search, 50, _cts.Token);
                });
                break;

            case "install":
                // /mcp install <server-name>
                if (parts.Length < 3)
                {
                    _ui.DisplayWarning("Usage: /mcp install <server-name>");
                    _ui.DisplayInfo("Example: /mcp install io.github/github-mcp");
                    return;
                }
                var serverName = parts[2];
                var installed = await registryService.InstallServerAsync(serverName, _config, _cts.Token);
                if (installed)
                {
                    // Save the updated configuration
                    await _configService.SaveConfigurationAsync(_config);
                    _ui.DisplaySuccess("Configuration saved.");
                    _ui.DisplayInfo("The server is disabled by default. Configure the required environment variables in Microbot.config, then set 'enabled' to true.");
                }
                break;

            case "info":
                // /mcp info <server-name>
                if (parts.Length < 3)
                {
                    _ui.DisplayWarning("Usage: /mcp info <server-name>");
                    _ui.DisplayInfo("Example: /mcp info io.github/github-mcp");
                    return;
                }
                await _ui.WithSpinnerAsync("Fetching server details...", async () =>
                {
                    await registryService.ShowServerDetailsAsync(parts[2], _cts.Token);
                });
                break;

            case "help":
            default:
                DisplayMcpHelp();
                break;
        }
    }

    /// <summary>
    /// Displays help for /mcp commands.
    /// </summary>
    private static void DisplayMcpHelp()
    {
        var table = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn("Command")
            .AddColumn("Description");

        table.AddRow("[cyan]/mcp list[/]", "List all MCP servers from the registry (paginated, 50 per page)");
        table.AddRow("[cyan]/mcp list <search>[/]", "Search for MCP servers by name or description");
        table.AddRow("[cyan]/mcp install <name>[/]", "Install an MCP server from the registry");
        table.AddRow("[cyan]/mcp info <name>[/]", "Show details about a specific MCP server");

        var panel = new Panel(table)
        {
            Header = new PanelHeader("[cyan]MCP Registry Commands[/]"),
            Border = BoxBorder.Rounded,
            Padding = new Padding(2, 1)
        };

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]The MCP Registry is at https://registry.modelcontextprotocol.io[/]");
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Displays the current configuration.
    /// </summary>
    private static void DisplayCurrentConfig()
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[cyan]Setting[/]")
            .AddColumn("[cyan]Value[/]");

        table.AddRow("AI Provider", Markup.Escape(_config.AiProvider.Provider));
        table.AddRow("Model", Markup.Escape(_config.AiProvider.ModelId));
        table.AddRow("Endpoint", Markup.Escape(_config.AiProvider.Endpoint ?? "(default)"));
        table.AddRow("Agent Name", Markup.Escape(_config.Preferences.AgentName));
        table.AddRow("Verbose Logging", _config.Preferences.VerboseLogging.ToString());
        table.AddRow("Use Streaming", _config.Preferences.UseStreaming.ToString());
        table.AddRow("MCP Servers", _config.Skills.McpServers.Count.ToString());
        table.AddRow("NuGet Skills", _config.Skills.NuGetSkills.Count.ToString());

        // Outlook skill settings
        var outlookStatus = _config.Skills.Outlook?.Enabled == true
            ? $"[green]Enabled[/] ({_config.Skills.Outlook.Mode})"
            : "[grey]Disabled[/]";
        table.AddRow("Outlook Skill", outlookStatus);

        var panel = new Panel(table)
        {
            Header = new PanelHeader("[cyan]Current Configuration[/]"),
            Border = BoxBorder.Rounded
        };

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }
}
