using Microsoft.Extensions.Logging;
using Microbot.Console.Services;
using Microbot.Core.Configuration;
using Microbot.Core.Models;
using Microbot.Memory;
using Microbot.Memory.Interfaces;
using Microbot.Skills.Loaders;
using Microbot.Skills.Scheduling.Database.Entities;
using Microbot.Skills.Scheduling.Services;
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

        // Get max tokens for Anthropic (required)
        if (provider == "Anthropic")
        {
            var maxTokensInput = _ui.PromptText(
                "Enter max tokens for responses (default: 4096):",
                "4096");
            if (int.TryParse(maxTokensInput, out var maxTokens))
            {
                _config.AiProvider.MaxTokens = maxTokens;
            }
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

        _agentService.RateLimitWaiting += (sender, e) =>
        {
            _ui.DisplayRateLimitWait(e);
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

            case "/memory":
                await HandleMemoryCommandAsync(parts);
                break;

            case "/schedule":
                await HandleScheduleCommandAsync(parts);
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
    /// Handles /memory subcommands for memory system operations.
    /// </summary>
    /// <param name="parts">The command parts.</param>
    private static async Task HandleMemoryCommandAsync(string[] parts)
    {
        if (parts.Length < 2)
        {
            DisplayMemoryHelp();
            return;
        }

        var subCommand = parts[1].ToLowerInvariant();

        // Enable/disable commands work even when memory is not initialized
        switch (subCommand)
        {
            case "enable":
                await EnableMemoryAsync();
                return;

            case "disable":
                await DisableMemoryAsync();
                return;
        }

        // All other commands require memory to be initialized
        if (_agentService.MemoryManager == null)
        {
            if (_config.Memory.Enabled)
            {
                // Memory is enabled in config but failed to initialize
                _ui.DisplayError("Memory system failed to initialize.");
                if (!string.IsNullOrEmpty(_agentService.MemoryInitializationError))
                {
                    _ui.DisplayError($"Error: {_agentService.MemoryInitializationError}");
                }
                _ui.DisplayInfo("Check your embedding configuration in Microbot.config.");
                _ui.DisplayInfo("Make sure the embedding model is deployed and accessible.");
            }
            else
            {
                _ui.DisplayWarning("Memory system is not enabled. Use '/memory enable' to enable it with default settings.");
            }
            return;
        }

        switch (subCommand)
        {
            case "status":
                DisplayMemoryStatus();
                break;

            case "sync":
                await SyncMemoryAsync(parts);
                break;

            case "search":
                if (parts.Length < 3)
                {
                    _ui.DisplayWarning("Usage: /memory search <query>");
                    _ui.DisplayInfo("Example: /memory search what did we discuss about the project");
                    return;
                }
                var query = string.Join(" ", parts.Skip(2));
                await SearchMemoryAsync(query);
                break;

            case "sessions":
                await ListSessionsAsync();
                break;

            case "save":
                await SaveCurrentSessionAsync();
                break;

            case "help":
            default:
                DisplayMemoryHelp();
                break;
        }
    }

    /// <summary>
    /// Displays the memory system status.
    /// </summary>
    private static void DisplayMemoryStatus()
    {
        var status = _agentService.MemoryManager!.GetStatus();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[cyan]Metric[/]")
            .AddColumn("[cyan]Value[/]");

        table.AddRow("Total Files Indexed", status.TotalFiles.ToString());
        table.AddRow("Total Text Chunks", status.TotalChunks.ToString());
        table.AddRow("Memory Files", status.MemoryFiles.ToString());
        table.AddRow("Session Files", status.SessionFiles.ToString());
        table.AddRow("Pending Changes", status.IsDirty ? "[yellow]Yes[/]" : "[green]No[/]");
        
        if (status.LastSyncAt.HasValue)
        {
            table.AddRow("Last Sync", status.LastSyncAt.Value.ToString("yyyy-MM-dd HH:mm:ss") + " UTC");
        }
        else
        {
            table.AddRow("Last Sync", "[grey]Never[/]");
        }

        if (!string.IsNullOrEmpty(status.EmbeddingModel))
        {
            table.AddRow("Embedding Model", Markup.Escape(status.EmbeddingModel));
        }

        var panel = new Panel(table)
        {
            Header = new PanelHeader("[cyan]Memory System Status[/]"),
            Border = BoxBorder.Rounded
        };

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Syncs the memory index.
    /// </summary>
    private static async Task SyncMemoryAsync(string[] parts)
    {
        var force = parts.Length > 2 && parts[2].ToLowerInvariant() == "--force";

        var progress = new Progress<SyncProgress>(p =>
        {
            // Progress is reported but we're using a spinner
        });

        SyncProgress? finalProgress = null;
        await _ui.WithSpinnerAsync("Syncing memory index...", async () =>
        {
            var options = new SyncOptions
            {
                Reason = "User requested sync",
                Force = force
            };
            
            var progressReporter = new Progress<SyncProgress>(p => finalProgress = p);
            await _agentService.MemoryManager!.SyncAsync(options, progressReporter, _cts.Token);
        });

        if (finalProgress != null)
        {
            _ui.DisplaySuccess($"Memory sync complete: {finalProgress.FilesProcessed} files processed, {finalProgress.ChunksCreated} chunks created.");
        }
        else
        {
            _ui.DisplaySuccess("Memory sync complete.");
        }
    }

    /// <summary>
    /// Searches memory for relevant information.
    /// </summary>
    private static async Task SearchMemoryAsync(string query)
    {
        IReadOnlyList<MemorySearchResult>? results = null;
        
        await _ui.WithSpinnerAsync("Searching memory...", async () =>
        {
            var options = new MemorySearchOptions
            {
                MaxResults = 10,
                MinScore = 0.5f,
                IncludeSessions = true,
                IncludeMemoryFiles = true
            };
            results = await _agentService.MemoryManager!.SearchAsync(query, options, _cts.Token);
        });

        if (results == null || results.Count == 0)
        {
            _ui.DisplayInfo("No relevant information found in memory.");
            return;
        }

        AnsiConsole.MarkupLine($"[cyan]Found {results.Count} relevant memory entries:[/]");
        AnsiConsole.WriteLine();

        foreach (var result in results)
        {
            var sourceLabel = result.Source == MemorySource.Sessions ? "[blue]Session[/]" : "[green]Memory[/]";
            var score = $"[grey](Score: {result.Score:F2})[/]";
            
            AnsiConsole.MarkupLine($"{sourceLabel} [white]{Markup.Escape(result.Path)}[/] {score}");
            AnsiConsole.MarkupLine($"[grey]Lines {result.StartLine}-{result.EndLine}[/]");
            
            var panel = new Panel(Markup.Escape(result.Snippet.Length > 500
                ? result.Snippet[..500] + "..."
                : result.Snippet))
            {
                Border = BoxBorder.Rounded,
                Padding = new Padding(1, 0)
            };
            AnsiConsole.Write(panel);
            AnsiConsole.WriteLine();
        }
    }

    /// <summary>
    /// Lists recent sessions.
    /// </summary>
    private static async Task ListSessionsAsync()
    {
        IReadOnlyList<SessionSummary>? sessions = null;
        
        await _ui.WithSpinnerAsync("Loading sessions...", async () =>
        {
            sessions = await _agentService.MemoryManager!.ListSessionsAsync(_cts.Token);
        });

        if (sessions == null || sessions.Count == 0)
        {
            _ui.DisplayInfo("No sessions found in memory.");
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[cyan]Session[/]")
            .AddColumn("[cyan]Started[/]")
            .AddColumn("[cyan]Messages[/]")
            .AddColumn("[cyan]Title/Summary[/]");

        foreach (var session in sessions.Take(20))
        {
            var title = !string.IsNullOrEmpty(session.Title)
                ? session.Title
                : (!string.IsNullOrEmpty(session.Summary)
                    ? (session.Summary.Length > 50 ? session.Summary[..50] + "..." : session.Summary)
                    : "[grey]No title[/]");
            
            table.AddRow(
                Markup.Escape(session.SessionKey),
                session.StartedAt.ToString("yyyy-MM-dd HH:mm"),
                session.MessageCount.ToString(),
                Markup.Escape(title));
        }

        var panel = new Panel(table)
        {
            Header = new PanelHeader($"[cyan]Recent Sessions ({Math.Min(sessions.Count, 20)} of {sessions.Count})[/]"),
            Border = BoxBorder.Rounded
        };

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Saves the current session to memory.
    /// </summary>
    private static async Task SaveCurrentSessionAsync()
    {
        await _ui.WithSpinnerAsync("Saving current session...", async () =>
        {
            await _agentService.SaveCurrentSessionAsync(_cts.Token);
        });
        _ui.DisplaySuccess("Current session saved to memory.");
    }

    /// <summary>
    /// Enables the memory system with default configuration.
    /// </summary>
    private static async Task EnableMemoryAsync()
    {
        if (_config.Memory.Enabled)
        {
            _ui.DisplayInfo("Memory system is already enabled.");
            return;
        }

        // Set default values based on current AI provider
        _config.Memory.Enabled = true;
        _config.Memory.DatabasePath = "./memory/microbot-memory.db";
        _config.Memory.MemoryFolder = "./memory";
        _config.Memory.SessionsFolder = "./memory/sessions";

        // Configure embedding provider based on current AI provider
        _config.Memory.Embedding.Provider = _config.AiProvider.Provider;
        _config.Memory.Embedding.Endpoint = _config.AiProvider.Endpoint;
        _config.Memory.Embedding.ApiKey = _config.AiProvider.ApiKey;
        
        // Set default embedding model based on provider
        _config.Memory.Embedding.ModelId = _config.AiProvider.Provider switch
        {
            "OpenAI" => "text-embedding-3-small",
            "AzureOpenAI" => "text-embedding-3-small",
            "Ollama" => "nomic-embed-text",
            _ => "text-embedding-3-small"
        };

        // Set default chunking options
        _config.Memory.Chunking.MaxTokens = 512;
        _config.Memory.Chunking.OverlapTokens = 50;
        _config.Memory.Chunking.MarkdownAware = true;

        // Set default search options
        _config.Memory.Search.MaxResults = 10;
        _config.Memory.Search.MinScore = 0.5f;
        _config.Memory.Search.VectorWeight = 0.7f;
        _config.Memory.Search.TextWeight = 0.3f;

        // Set default sync options
        _config.Memory.Sync.EnableFileWatching = true;
        _config.Memory.Sync.DebounceMs = 1000;

        // Save configuration
        await _configService.SaveConfigurationAsync(_config);
        _ui.DisplaySuccess("Memory system enabled with default configuration.");
        _ui.DisplayInfo("Configuration saved to Microbot.config.");
        
        // Display the configuration
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[cyan]Setting[/]")
            .AddColumn("[cyan]Value[/]");

        table.AddRow("Database Path", Markup.Escape(_config.Memory.DatabasePath));
        table.AddRow("Memory Folder", Markup.Escape(_config.Memory.MemoryFolder));
        table.AddRow("Sessions Folder", Markup.Escape(_config.Memory.SessionsFolder));
        table.AddRow("Embedding Provider", Markup.Escape(_config.Memory.Embedding.Provider));
        table.AddRow("Embedding Model", Markup.Escape(_config.Memory.Embedding.ModelId));
        table.AddRow("Chunk Size", $"{_config.Memory.Chunking.MaxTokens} tokens");
        table.AddRow("File Watching", _config.Memory.Sync.EnableFileWatching ? "Enabled" : "Disabled");

        var panel = new Panel(table)
        {
            Header = new PanelHeader("[cyan]Memory Configuration[/]"),
            Border = BoxBorder.Rounded
        };

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();

        // Prompt to restart
        _ui.DisplayWarning("Please restart Microbot for the memory system to initialize.");
    }

    /// <summary>
    /// Disables the memory system.
    /// </summary>
    private static async Task DisableMemoryAsync()
    {
        if (!_config.Memory.Enabled)
        {
            _ui.DisplayInfo("Memory system is already disabled.");
            return;
        }

        _config.Memory.Enabled = false;

        // Save configuration
        await _configService.SaveConfigurationAsync(_config);
        _ui.DisplaySuccess("Memory system disabled.");
        _ui.DisplayInfo("Configuration saved to Microbot.config.");
        _ui.DisplayInfo("Note: Existing memory data is preserved and will be available when re-enabled.");
    }

    /// <summary>
    /// Displays help for /memory commands.
    /// </summary>
    private static void DisplayMemoryHelp()
    {
        var table = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn("Command")
            .AddColumn("Description");

        table.AddRow("[cyan]/memory enable[/]", "Enable memory system with default configuration");
        table.AddRow("[cyan]/memory disable[/]", "Disable memory system (preserves data)");
        table.AddRow("[cyan]/memory status[/]", "Show memory system status (files, chunks, last sync)");
        table.AddRow("[cyan]/memory sync[/]", "Sync memory index with source files");
        table.AddRow("[cyan]/memory sync --force[/]", "Force full re-index of all files");
        table.AddRow("[cyan]/memory search <query>[/]", "Search memory for relevant information");
        table.AddRow("[cyan]/memory sessions[/]", "List recent conversation sessions");
        table.AddRow("[cyan]/memory save[/]", "Save current session to memory");

        var panel = new Panel(table)
        {
            Header = new PanelHeader("[cyan]Memory Commands[/]"),
            Border = BoxBorder.Rounded,
            Padding = new Padding(2, 1)
        };

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
        
        var memoryEnabled = _agentService.MemoryManager != null;
        if (memoryEnabled)
        {
            AnsiConsole.MarkupLine("[green]Memory system is enabled and running.[/]");
        }
        else if (_config.Memory.Enabled)
        {
            AnsiConsole.MarkupLine("[red]Memory system is enabled but failed to initialize.[/]");
            if (!string.IsNullOrEmpty(_agentService.MemoryInitializationError))
            {
                AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(_agentService.MemoryInitializationError)}[/]");
            }
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]Memory system is disabled. Use '/memory enable' to enable it.[/]");
        }
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Handles /schedule subcommands for schedule management.
    /// </summary>
    /// <param name="parts">The command parts.</param>
    private static async Task HandleScheduleCommandAsync(string[] parts)
    {
        if (parts.Length < 2)
        {
            DisplayScheduleHelp();
            return;
        }

        var subCommand = parts[1].ToLowerInvariant();

        // Get the schedule service from the agent service
        var scheduleService = _agentService.ScheduleService;
        if (scheduleService == null)
        {
            if (_config.Skills.Scheduling?.Enabled == true)
            {
                _ui.DisplayError("Scheduling system failed to initialize.");
                _ui.DisplayInfo("Check your configuration in Microbot.config.");
            }
            else
            {
                _ui.DisplayWarning("Scheduling system is not enabled. Use '/schedule enable' to enable it.");
            }
            return;
        }

        switch (subCommand)
        {
            case "enable":
                await EnableSchedulingAsync();
                break;

            case "disable":
                await DisableSchedulingAsync();
                break;

            case "list":
                await ListSchedulesAsync(scheduleService);
                break;

            case "add":
                await AddScheduleAsync(parts, scheduleService);
                break;

            case "remove":
            case "delete":
                await RemoveScheduleAsync(parts, scheduleService);
                break;

            case "enable-schedule":
                await EnableScheduleByIdAsync(parts, scheduleService);
                break;

            case "disable-schedule":
                await DisableScheduleByIdAsync(parts, scheduleService);
                break;

            case "run":
                await RunScheduleNowAsync(parts, scheduleService);
                break;

            case "history":
                await ShowScheduleHistoryAsync(parts, scheduleService);
                break;

            case "help":
            default:
                DisplayScheduleHelp();
                break;
        }
    }

    /// <summary>
    /// Lists all schedules.
    /// </summary>
    private static async Task ListSchedulesAsync(IScheduleService scheduleService)
    {
        var schedules = await scheduleService.GetAllSchedulesAsync(includeCompleted: true, _cts.Token);

        if (schedules.Count == 0)
        {
            _ui.DisplayInfo("No schedules configured.");
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[cyan]ID[/]")
            .AddColumn("[cyan]Name[/]")
            .AddColumn("[cyan]Type[/]")
            .AddColumn("[cyan]Schedule[/]")
            .AddColumn("[cyan]Status[/]")
            .AddColumn("[cyan]Next Run[/]")
            .AddColumn("[cyan]Command[/]");

        foreach (var schedule in schedules)
        {
            var status = schedule.Enabled
                ? (schedule.IsCompleted ? "[grey]Completed[/]" : "[green]Enabled[/]")
                : "[yellow]Disabled[/]";
            
            var nextRun = schedule.NextRunAt.HasValue
                ? schedule.NextRunAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                : "[grey]N/A[/]";

            var typeLabel = schedule.Type == ScheduleType.Once ? "[blue]Once[/]" : "[cyan]Recurring[/]";
            
            var commandPreview = schedule.Command.Length > 40
                ? schedule.Command[..40] + "..."
                : schedule.Command;

            table.AddRow(
                schedule.Id.ToString(),
                Markup.Escape(schedule.Name),
                typeLabel,
                Markup.Escape(schedule.Schedule),
                status,
                nextRun,
                Markup.Escape(commandPreview));
        }

        var panel = new Panel(table)
        {
            Header = new PanelHeader($"[cyan]Schedules ({schedules.Count})[/]"),
            Border = BoxBorder.Rounded
        };

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Adds a new schedule.
    /// </summary>
    private static async Task AddScheduleAsync(string[] parts, IScheduleService scheduleService)
    {
        // Usage: /schedule add <name> <expression> <command>
        // Example: /schedule add "daily-report" "every day at 9am" "Generate a daily summary report"
        if (parts.Length < 5)
        {
            _ui.DisplayWarning("Usage: /schedule add <name> <expression> <command>");
            _ui.DisplayInfo("Examples:");
            _ui.DisplayInfo("  /schedule add daily-report \"every day at 9am\" \"Generate a daily summary report\"");
            _ui.DisplayInfo("  /schedule add weekly-review \"every monday at 10am\" \"Review weekly tasks\"");
            _ui.DisplayInfo("  /schedule add reminder \"once tomorrow at 3pm\" \"Remind me about the meeting\"");
            _ui.DisplayInfo("  /schedule add cron-job \"0 9 * * *\" \"Run morning tasks\"");
            return;
        }

        var name = parts[2];
        
        // Parse the expression and command - they might be quoted
        var remainingArgs = string.Join(" ", parts.Skip(3));
        var (expression, command) = ParseQuotedArguments(remainingArgs);

        if (string.IsNullOrWhiteSpace(expression) || string.IsNullOrWhiteSpace(command))
        {
            _ui.DisplayWarning("Both expression and command are required.");
            _ui.DisplayInfo("Use quotes for expressions or commands with spaces.");
            return;
        }

        try
        {
            var schedule = await scheduleService.CreateScheduleAsync(name, expression, command, null, _cts.Token);
            _ui.DisplaySuccess($"Schedule created with ID {schedule.Id}");
            
            if (schedule.NextRunAt.HasValue)
            {
                _ui.DisplayInfo($"Next run: {schedule.NextRunAt.Value.ToLocalTime():yyyy-MM-dd HH:mm}");
            }
        }
        catch (ArgumentException ex)
        {
            _ui.DisplayError($"Invalid schedule: {ex.Message}");
        }
    }

    /// <summary>
    /// Parses quoted arguments from a string.
    /// </summary>
    private static (string first, string second) ParseQuotedArguments(string input)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        var quoteChar = '"';

        foreach (var c in input)
        {
            if ((c == '"' || c == '\'') && !inQuotes)
            {
                inQuotes = true;
                quoteChar = c;
            }
            else if (c == quoteChar && inQuotes)
            {
                inQuotes = false;
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
            }
            else if (c == ' ' && !inQuotes)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
        {
            result.Add(current.ToString());
        }

        return result.Count >= 2
            ? (result[0], string.Join(" ", result.Skip(1)))
            : (result.FirstOrDefault() ?? "", "");
    }

    /// <summary>
    /// Removes a schedule by ID.
    /// </summary>
    private static async Task RemoveScheduleAsync(string[] parts, IScheduleService scheduleService)
    {
        if (parts.Length < 3 || !int.TryParse(parts[2], out var id))
        {
            _ui.DisplayWarning("Usage: /schedule remove <id>");
            _ui.DisplayInfo("Example: /schedule remove 1");
            return;
        }

        var deleted = await scheduleService.RemoveScheduleAsync(id, _cts.Token);
        if (deleted)
        {
            _ui.DisplaySuccess($"Schedule {id} removed.");
        }
        else
        {
            _ui.DisplayWarning($"Schedule {id} not found.");
        }
    }

    /// <summary>
    /// Enables a schedule by ID.
    /// </summary>
    private static async Task EnableScheduleByIdAsync(string[] parts, IScheduleService scheduleService)
    {
        if (parts.Length < 3 || !int.TryParse(parts[2], out var id))
        {
            _ui.DisplayWarning("Usage: /schedule enable-schedule <id>");
            _ui.DisplayInfo("Example: /schedule enable-schedule 1");
            return;
        }

        var schedule = await scheduleService.EnableScheduleAsync(id, _cts.Token);
        if (schedule != null)
        {
            _ui.DisplaySuccess($"Schedule {id} enabled.");
            if (schedule.NextRunAt.HasValue)
            {
                _ui.DisplayInfo($"Next run: {schedule.NextRunAt.Value.ToLocalTime():yyyy-MM-dd HH:mm}");
            }
        }
        else
        {
            _ui.DisplayWarning($"Schedule {id} not found.");
        }
    }

    /// <summary>
    /// Disables a schedule by ID.
    /// </summary>
    private static async Task DisableScheduleByIdAsync(string[] parts, IScheduleService scheduleService)
    {
        if (parts.Length < 3 || !int.TryParse(parts[2], out var id))
        {
            _ui.DisplayWarning("Usage: /schedule disable-schedule <id>");
            _ui.DisplayInfo("Example: /schedule disable-schedule 1");
            return;
        }

        var schedule = await scheduleService.DisableScheduleAsync(id, _cts.Token);
        if (schedule != null)
        {
            _ui.DisplaySuccess($"Schedule {id} disabled.");
        }
        else
        {
            _ui.DisplayWarning($"Schedule {id} not found.");
        }
    }

    /// <summary>
    /// Runs a schedule immediately.
    /// </summary>
    private static async Task RunScheduleNowAsync(string[] parts, IScheduleService scheduleService)
    {
        if (parts.Length < 3 || !int.TryParse(parts[2], out var id))
        {
            _ui.DisplayWarning("Usage: /schedule run <id>");
            _ui.DisplayInfo("Example: /schedule run 1");
            return;
        }

        var schedule = await scheduleService.GetScheduleAsync(id, _cts.Token);
        if (schedule == null)
        {
            _ui.DisplayWarning($"Schedule {id} not found.");
            return;
        }

        _ui.DisplayInfo($"Running schedule '{schedule.Name}'...");
        _ui.DisplayInfo($"Command: {schedule.Command}");
        AnsiConsole.WriteLine();

        // Start execution tracking
        var execution = await scheduleService.StartExecutionAsync(id, _cts.Token);

        // Execute the command through the agent
        try
        {
            string? response = null;
            if (_config.Preferences.UseStreaming)
            {
                await _ui.DisplayStreamingResponseAsync(
                    _agentService.ChatStreamingAsync(schedule.Command, _cts.Token),
                    _config.Preferences.AgentName);
            }
            else
            {
                response = await _ui.WithSpinnerAsync(
                    "Executing...",
                    () => _agentService.ChatAsync(schedule.Command, _cts.Token));
                _ui.DisplayAgentResponse(response, _config.Preferences.AgentName);
            }

            // Record successful execution
            await scheduleService.CompleteExecutionAsync(execution.Id, response, _cts.Token);
        }
        catch (Exception ex)
        {
            _ui.DisplayError($"Execution failed: {ex.Message}");
            await scheduleService.FailExecutionAsync(execution.Id, ExecutionStatus.Failed, ex.Message, _cts.Token);
        }
    }

    /// <summary>
    /// Shows execution history for a schedule.
    /// </summary>
    private static async Task ShowScheduleHistoryAsync(string[] parts, IScheduleService scheduleService)
    {
        if (parts.Length < 3 || !int.TryParse(parts[2], out var id))
        {
            _ui.DisplayWarning("Usage: /schedule history <id>");
            _ui.DisplayInfo("Example: /schedule history 1");
            return;
        }

        var schedule = await scheduleService.GetScheduleAsync(id, _cts.Token);
        if (schedule == null)
        {
            _ui.DisplayWarning($"Schedule {id} not found.");
            return;
        }

        var history = await scheduleService.GetExecutionHistoryAsync(id, 20, _cts.Token);

        if (history.Count == 0)
        {
            _ui.DisplayInfo($"No execution history for schedule '{schedule.Name}'.");
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[cyan]Started At[/]")
            .AddColumn("[cyan]Status[/]")
            .AddColumn("[cyan]Duration[/]")
            .AddColumn("[cyan]Error[/]");

        foreach (var execution in history)
        {
            var status = execution.Status switch
            {
                ExecutionStatus.Completed => "[green]Success[/]",
                ExecutionStatus.Failed => "[red]Failed[/]",
                ExecutionStatus.Timeout => "[yellow]Timeout[/]",
                ExecutionStatus.Running => "[blue]Running[/]",
                _ => "[grey]Unknown[/]"
            };
            
            var duration = execution.Duration.HasValue
                ? $"{execution.Duration.Value.TotalSeconds:F1}s"
                : "[grey]N/A[/]";
            
            var error = string.IsNullOrEmpty(execution.ErrorMessage)
                ? "[grey]N/A[/]"
                : Markup.Escape(execution.ErrorMessage.Length > 40
                    ? execution.ErrorMessage[..40] + "..."
                    : execution.ErrorMessage);

            table.AddRow(
                execution.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                status,
                duration,
                error);
        }

        var panel = new Panel(table)
        {
            Header = new PanelHeader($"[cyan]Execution History: {Markup.Escape(schedule.Name)}[/]"),
            Border = BoxBorder.Rounded
        };

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Enables the scheduling system.
    /// </summary>
    private static async Task EnableSchedulingAsync()
    {
        if (_config.Skills.Scheduling?.Enabled == true)
        {
            _ui.DisplayInfo("Scheduling system is already enabled.");
            return;
        }

        _config.Skills.Scheduling ??= new SchedulingSkillConfig();
        _config.Skills.Scheduling.Enabled = true;
        _config.Skills.Scheduling.DatabasePath = "./schedules/schedules.db";
        _config.Skills.Scheduling.CheckIntervalSeconds = 60;

        await _configService.SaveConfigurationAsync(_config);
        _ui.DisplaySuccess("Scheduling system enabled.");
        _ui.DisplayInfo("Configuration saved to Microbot.config.");
        _ui.DisplayWarning("Please restart Microbot for the scheduling system to initialize.");
    }

    /// <summary>
    /// Disables the scheduling system.
    /// </summary>
    private static async Task DisableSchedulingAsync()
    {
        if (_config.Skills.Scheduling?.Enabled != true)
        {
            _ui.DisplayInfo("Scheduling system is already disabled.");
            return;
        }

        _config.Skills.Scheduling.Enabled = false;

        await _configService.SaveConfigurationAsync(_config);
        _ui.DisplaySuccess("Scheduling system disabled.");
        _ui.DisplayInfo("Configuration saved to Microbot.config.");
        _ui.DisplayInfo("Note: Existing schedules are preserved and will be available when re-enabled.");
    }

    /// <summary>
    /// Displays help for /schedule commands.
    /// </summary>
    private static void DisplayScheduleHelp()
    {
        var table = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn("Command")
            .AddColumn("Description");

        table.AddRow("[cyan]/schedule enable[/]", "Enable scheduling system");
        table.AddRow("[cyan]/schedule disable[/]", "Disable scheduling system (preserves schedules)");
        table.AddRow("[cyan]/schedule list[/]", "List all schedules");
        table.AddRow("[cyan]/schedule add <name> <expr> <cmd>[/]", "Add a new schedule");
        table.AddRow("[cyan]/schedule remove <id>[/]", "Remove a schedule by ID");
        table.AddRow("[cyan]/schedule enable-schedule <id>[/]", "Enable a schedule by ID");
        table.AddRow("[cyan]/schedule disable-schedule <id>[/]", "Disable a schedule by ID");
        table.AddRow("[cyan]/schedule run <id>[/]", "Run a schedule immediately");
        table.AddRow("[cyan]/schedule history <id>[/]", "Show execution history for a schedule");

        var panel = new Panel(table)
        {
            Header = new PanelHeader("[cyan]Schedule Commands[/]"),
            Border = BoxBorder.Rounded,
            Padding = new Padding(2, 1)
        };

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[cyan]Schedule Expression Examples:[/]");
        AnsiConsole.MarkupLine("  [grey]Recurring:[/] \"every day at 9am\", \"every monday at 10am\", \"0 9 * * *\"");
        AnsiConsole.MarkupLine("  [grey]One-time:[/] \"once tomorrow at 3pm\", \"once in 2 hours\", \"once 2024-12-25 10:00\"");
        AnsiConsole.WriteLine();

        var scheduleEnabled = _agentService.ScheduleService != null;
        if (scheduleEnabled)
        {
            AnsiConsole.MarkupLine("[green]Scheduling system is enabled and running.[/]");
        }
        else if (_config.Skills.Scheduling?.Enabled == true)
        {
            AnsiConsole.MarkupLine("[red]Scheduling system is enabled but failed to initialize.[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]Scheduling system is disabled. Use '/schedule enable' to enable it.[/]");
        }
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
