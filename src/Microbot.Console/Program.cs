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

        AnsiConsole.WriteLine();
        _ui.DisplaySuccess("Initial setup complete!");
        
        return Task.CompletedTask;
    }

    /// <summary>
    /// Initializes the AI agent with skills.
    /// </summary>
    private static async Task InitializeAgentAsync()
    {
        _agentService = new AgentService(_config, _loggerFactory);

        await _ui.WithSpinnerAsync("Initializing AI agent and loading skills...", async () =>
        {
            await _agentService.InitializeAsync(_cts.Token);
        });

        _ui.DisplaySuccess("Agent initialized successfully");
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
        var parts = command.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cmd = parts[0];

        switch (cmd)
        {
            case "/help":
                _ui.DisplayHelp();
                break;

            case "/skills":
                if (_agentService.SkillManager != null)
                {
                    _ui.DisplaySkillsSummary(_agentService.SkillManager.GetSkillSummaries());
                }
                else
                {
                    _ui.DisplayWarning("No skills loaded");
                }
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

        var panel = new Panel(table)
        {
            Header = new PanelHeader("[cyan]Current Configuration[/]"),
            Border = BoxBorder.Rounded
        };

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }
}
