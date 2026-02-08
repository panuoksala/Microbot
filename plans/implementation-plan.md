# Microbot Implementation Plan

This document provides a detailed step-by-step implementation plan for the Microbot application.

## Prerequisites

Before starting implementation, ensure you have:
- .NET 10 SDK installed
- Visual Studio 2022 or VS Code with C# extension
- Docker (optional, for some MCP servers)

## Phase 1: Project Setup

### Step 1.1: Create Solution Structure

```bash
# Create solution
dotnet new sln -n Microbot

# Create projects
dotnet new console -n Microbot.Console -o src/Microbot.Console
dotnet new classlib -n Microbot.Core -o src/Microbot.Core
dotnet new classlib -n Microbot.Skills -o src/Microbot.Skills

# Create Aspire projects
dotnet new aspire-apphost -n Microbot.AppHost -o src/Microbot.AppHost
dotnet new aspire-servicedefaults -n Microbot.ServiceDefaults -o src/Microbot.ServiceDefaults

# Add projects to solution
dotnet sln add src/Microbot.Console/Microbot.Console.csproj
dotnet sln add src/Microbot.Core/Microbot.Core.csproj
dotnet sln add src/Microbot.Skills/Microbot.Skills.csproj
dotnet sln add src/Microbot.AppHost/Microbot.AppHost.csproj
dotnet sln add src/Microbot.ServiceDefaults/Microbot.ServiceDefaults.csproj
```

### Step 1.2: Add Project References

```bash
# Microbot.Console references
cd src/Microbot.Console
dotnet add reference ../Microbot.Core/Microbot.Core.csproj
dotnet add reference ../Microbot.Skills/Microbot.Skills.csproj
dotnet add reference ../Microbot.ServiceDefaults/Microbot.ServiceDefaults.csproj

# Microbot.Skills references
cd ../Microbot.Skills
dotnet add reference ../Microbot.Core/Microbot.Core.csproj

# Microbot.AppHost references
cd ../Microbot.AppHost
dotnet add reference ../Microbot.Console/Microbot.Console.csproj
```

### Step 1.3: Add NuGet Packages

**Microbot.Console:**
```bash
dotnet add package Spectre.Console
dotnet add package Spectre.Console.Cli
dotnet add package Microsoft.SemanticKernel
dotnet add package Microsoft.SemanticKernel.Agents.Core --prerelease
dotnet add package Microsoft.Extensions.Hosting
dotnet add package Microsoft.Extensions.Configuration.Json
```

**Microbot.Core:**
```bash
dotnet add package System.Text.Json
dotnet add package Microsoft.Extensions.Options
```

**Microbot.Skills:**
```bash
dotnet add package Microsoft.SemanticKernel
dotnet add package ModelContextProtocol --prerelease
dotnet add package System.Runtime.Loader
```

**Microbot.AppHost:**
```bash
dotnet add package Aspire.Hosting
```

### Step 1.4: Create Directory Structure

```bash
# Create skills directories
mkdir -p skills/mcp
mkdir -p skills/nuget

# Create placeholder files
echo "{\"servers\":[]}" > skills/mcp/servers.json
touch skills/nuget/.gitkeep
```

## Phase 2: Core Domain Implementation

### Step 2.1: Configuration Models

Create the following files in `Microbot.Core/Models/`:

**MicrobotConfig.cs:**
```csharp
namespace Microbot.Core.Models;

public class MicrobotConfig
{
    public string Version { get; set; } = "1.0";
    public AiProviderConfig AiProvider { get; set; } = new();
    public SkillsConfig Skills { get; set; } = new();
    public UserPreferences Preferences { get; set; } = new();
}

public class AiProviderConfig
{
    public string Provider { get; set; } = "AzureOpenAI";
    public string ModelId { get; set; } = "gpt-4o";
    public string? Endpoint { get; set; }
    public string? ApiKey { get; set; }
}

public class SkillsConfig
{
    public string McpFolder { get; set; } = "./skills/mcp";
    public string NuGetFolder { get; set; } = "./skills/nuget";
    public List<McpServerConfig> McpServers { get; set; } = [];
    public List<NuGetSkillConfig> NuGetSkills { get; set; } = [];
}

public class McpServerConfig
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public List<string> Args { get; set; } = [];
    public Dictionary<string, string> Env { get; set; } = [];
    public bool Enabled { get; set; } = true;
}

public class NuGetSkillConfig
{
    public string Name { get; set; } = string.Empty;
    public string AssemblyPath { get; set; } = string.Empty;
    public string? TypeName { get; set; }
    public bool Enabled { get; set; } = true;
}

public class UserPreferences
{
    public string Theme { get; set; } = "default";
    public bool VerboseLogging { get; set; } = false;
}
```

### Step 2.2: Interfaces

Create in `Microbot.Core/Interfaces/`:

**ISkillLoader.cs:**
```csharp
namespace Microbot.Core.Interfaces;

using Microsoft.SemanticKernel;

public interface ISkillLoader
{
    Task<IEnumerable<KernelPlugin>> LoadSkillsAsync(CancellationToken cancellationToken = default);
}
```

**IConfigurationService.cs:**
```csharp
namespace Microbot.Core.Interfaces;

using Microbot.Core.Models;

public interface IConfigurationService
{
    Task<MicrobotConfig> LoadConfigurationAsync();
    Task SaveConfigurationAsync(MicrobotConfig config);
    bool ConfigurationExists();
}
```

### Step 2.3: Configuration Service

Create in `Microbot.Core/Configuration/`:

**ConfigurationService.cs:**
```csharp
namespace Microbot.Core.Configuration;

using System.Text.Json;
using Microbot.Core.Interfaces;
using Microbot.Core.Models;

public class ConfigurationService : IConfigurationService
{
    private const string ConfigFileName = "Microbot.config";
    private readonly string _configPath;
    private readonly JsonSerializerOptions _jsonOptions;

    public ConfigurationService(string? basePath = null)
    {
        _configPath = Path.Combine(basePath ?? Directory.GetCurrentDirectory(), ConfigFileName);
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public bool ConfigurationExists() => File.Exists(_configPath);

    public async Task<MicrobotConfig> LoadConfigurationAsync()
    {
        if (!ConfigurationExists())
        {
            return new MicrobotConfig();
        }

        var json = await File.ReadAllTextAsync(_configPath);
        return JsonSerializer.Deserialize<MicrobotConfig>(json, _jsonOptions) 
            ?? new MicrobotConfig();
    }

    public async Task SaveConfigurationAsync(MicrobotConfig config)
    {
        var json = JsonSerializer.Serialize(config, _jsonOptions);
        await File.WriteAllTextAsync(_configPath, json);
    }
}
```

## Phase 3: Skill Loading System

### Step 3.1: MCP Skill Loader

Create in `Microbot.Skills/Loaders/`:

**McpSkillLoader.cs:**
```csharp
namespace Microbot.Skills.Loaders;

using System.Diagnostics;
using Microsoft.SemanticKernel;
using Microbot.Core.Interfaces;
using Microbot.Core.Models;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Transport;

public class McpSkillLoader : ISkillLoader
{
    private readonly SkillsConfig _config;
    private readonly List<IMcpClient> _clients = [];

    public McpSkillLoader(SkillsConfig config)
    {
        _config = config;
    }

    public async Task<IEnumerable<KernelPlugin>> LoadSkillsAsync(
        CancellationToken cancellationToken = default)
    {
        var plugins = new List<KernelPlugin>();

        foreach (var serverConfig in _config.McpServers.Where(s => s.Enabled))
        {
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
                // Log error but continue loading other servers
                Console.WriteLine($"Failed to load MCP server {serverConfig.Name}: {ex.Message}");
            }
        }

        return plugins;
    }

    private async Task<KernelPlugin?> LoadMcpServerAsync(
        McpServerConfig serverConfig,
        CancellationToken cancellationToken)
    {
        var clientTransport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = serverConfig.Command,
            Arguments = serverConfig.Args,
            EnvironmentVariables = serverConfig.Env
        });

        var client = await McpClientFactory.CreateAsync(
            clientTransport,
            cancellationToken: cancellationToken);

        _clients.Add(client);

        // Get tools from MCP server and convert to Kernel functions
        var tools = await client.ListToolsAsync(cancellationToken);
        
        var functions = tools.Select(tool => 
            CreateKernelFunctionFromMcpTool(client, tool));

        return KernelPluginFactory.CreateFromFunctions(
            serverConfig.Name,
            serverConfig.Description,
            functions);
    }

    private KernelFunction CreateKernelFunctionFromMcpTool(
        IMcpClient client, 
        McpTool tool)
    {
        // Create a kernel function that calls the MCP tool
        return KernelFunctionFactory.CreateFromMethod(
            async (Kernel kernel, KernelArguments arguments) =>
            {
                var result = await client.CallToolAsync(
                    tool.Name,
                    arguments.ToDictionary(
                        kvp => kvp.Key,
                        kvp => kvp.Value?.ToString() ?? string.Empty));
                
                return result.Content?.FirstOrDefault()?.Text ?? string.Empty;
            },
            tool.Name,
            tool.Description);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
        {
            if (client is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
        }
    }
}
```

### Step 3.2: NuGet Skill Loader

Create in `Microbot.Skills/Loaders/`:

**NuGetSkillLoader.cs:**
```csharp
namespace Microbot.Skills.Loaders;

using System.Reflection;
using System.Runtime.Loader;
using Microsoft.SemanticKernel;
using Microbot.Core.Interfaces;
using Microbot.Core.Models;

public class NuGetSkillLoader : ISkillLoader
{
    private readonly SkillsConfig _config;
    private readonly SkillAssemblyLoadContext _loadContext;

    public NuGetSkillLoader(SkillsConfig config)
    {
        _config = config;
        _loadContext = new SkillAssemblyLoadContext();
    }

    public async Task<IEnumerable<KernelPlugin>> LoadSkillsAsync(
        CancellationToken cancellationToken = default)
    {
        var plugins = new List<KernelPlugin>();
        var nugetFolder = _config.NuGetFolder;

        if (!Directory.Exists(nugetFolder))
        {
            return plugins;
        }

        // Load explicitly configured skills
        foreach (var skillConfig in _config.NuGetSkills.Where(s => s.Enabled))
        {
            var plugin = await LoadSkillFromConfigAsync(skillConfig, cancellationToken);
            if (plugin != null)
            {
                plugins.Add(plugin);
            }
        }

        // Auto-discover skills from DLLs in the nuget folder
        var discoveredPlugins = await DiscoverSkillsAsync(nugetFolder, cancellationToken);
        plugins.AddRange(discoveredPlugins);

        return plugins;
    }

    private async Task<KernelPlugin?> LoadSkillFromConfigAsync(
        NuGetSkillConfig config,
        CancellationToken cancellationToken)
    {
        try
        {
            var assemblyPath = Path.GetFullPath(config.AssemblyPath);
            var assembly = _loadContext.LoadFromAssemblyPath(assemblyPath);

            Type? skillType = null;
            if (!string.IsNullOrEmpty(config.TypeName))
            {
                skillType = assembly.GetType(config.TypeName);
            }
            else
            {
                // Find first type with KernelFunction methods
                skillType = FindSkillType(assembly);
            }

            if (skillType == null)
            {
                return null;
            }

            var instance = Activator.CreateInstance(skillType);
            return KernelPluginFactory.CreateFromObject(instance!, config.Name);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load skill {config.Name}: {ex.Message}");
            return null;
        }
    }

    private async Task<IEnumerable<KernelPlugin>> DiscoverSkillsAsync(
        string folder,
        CancellationToken cancellationToken)
    {
        var plugins = new List<KernelPlugin>();

        foreach (var dllPath in Directory.GetFiles(folder, "*.dll"))
        {
            try
            {
                var assembly = _loadContext.LoadFromAssemblyPath(dllPath);
                var skillTypes = FindAllSkillTypes(assembly);

                foreach (var skillType in skillTypes)
                {
                    var instance = Activator.CreateInstance(skillType);
                    var pluginName = skillType.Name.Replace("Plugin", "")
                                                   .Replace("Skill", "");
                    var plugin = KernelPluginFactory.CreateFromObject(instance!, pluginName);
                    plugins.Add(plugin);
                }
            }
            catch
            {
                // Skip assemblies that can't be loaded
            }
        }

        return plugins;
    }

    private Type? FindSkillType(Assembly assembly)
    {
        return assembly.GetTypes()
            .FirstOrDefault(t => t.GetMethods()
                .Any(m => m.GetCustomAttribute<KernelFunctionAttribute>() != null));
    }

    private IEnumerable<Type> FindAllSkillTypes(Assembly assembly)
    {
        return assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract &&
                        t.GetMethods().Any(m => 
                            m.GetCustomAttribute<KernelFunctionAttribute>() != null));
    }
}

internal class SkillAssemblyLoadContext : AssemblyLoadContext
{
    public SkillAssemblyLoadContext() : base(isCollectible: true)
    {
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Try to load from the default context first
        try
        {
            return Default.LoadFromAssemblyName(assemblyName);
        }
        catch
        {
            return null;
        }
    }
}
```

### Step 3.3: Skill Manager

Create in `Microbot.Skills/`:

**SkillManager.cs:**
```csharp
namespace Microbot.Skills;

using Microsoft.SemanticKernel;
using Microbot.Core.Interfaces;
using Microbot.Core.Models;
using Microbot.Skills.Loaders;

public class SkillManager : IAsyncDisposable
{
    private readonly SkillsConfig _config;
    private readonly McpSkillLoader _mcpLoader;
    private readonly NuGetSkillLoader _nugetLoader;
    private readonly List<KernelPlugin> _loadedPlugins = [];

    public IReadOnlyList<KernelPlugin> LoadedPlugins => _loadedPlugins.AsReadOnly();

    public SkillManager(SkillsConfig config)
    {
        _config = config;
        _mcpLoader = new McpSkillLoader(config);
        _nugetLoader = new NuGetSkillLoader(config);
    }

    public async Task<IEnumerable<KernelPlugin>> LoadAllSkillsAsync(
        CancellationToken cancellationToken = default)
    {
        _loadedPlugins.Clear();

        // Load MCP skills
        var mcpPlugins = await _mcpLoader.LoadSkillsAsync(cancellationToken);
        _loadedPlugins.AddRange(mcpPlugins);

        // Load NuGet skills
        var nugetPlugins = await _nugetLoader.LoadSkillsAsync(cancellationToken);
        _loadedPlugins.AddRange(nugetPlugins);

        return _loadedPlugins;
    }

    public void RegisterPluginsWithKernel(Kernel kernel)
    {
        foreach (var plugin in _loadedPlugins)
        {
            kernel.Plugins.Add(plugin);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _mcpLoader.DisposeAsync();
    }
}
```

## Phase 4: Console Application

### Step 4.1: Program.cs

Create in `Microbot.Console/`:

**Program.cs:**
```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.SemanticKernel;
using Microbot.Console.Services;
using Microbot.Core.Configuration;
using Microbot.Core.Interfaces;
using Spectre.Console;

var builder = Host.CreateApplicationBuilder(args);

// Register services
builder.Services.AddSingleton<IConfigurationService, ConfigurationService>();
builder.Services.AddSingleton<ConsoleUIService>();
builder.Services.AddSingleton<AgentService>();

// Add Aspire service defaults
builder.AddServiceDefaults();

var host = builder.Build();

// Run the application
var ui = host.Services.GetRequiredService<ConsoleUIService>();
var configService = host.Services.GetRequiredService<IConfigurationService>();
var agentService = host.Services.GetRequiredService<AgentService>();

await ui.ShowWelcomeAsync();

// Check for configuration
if (!configService.ConfigurationExists())
{
    await ui.ShowMessageAsync("First time setup required. Configuration wizard will start.", "info");
    // TODO: Implement configuration wizard in Phase 5
    var defaultConfig = new Microbot.Core.Models.MicrobotConfig();
    await configService.SaveConfigurationAsync(defaultConfig);
}

var config = await configService.LoadConfigurationAsync();

// Initialize and run agent
await agentService.InitializeAsync(config);
await agentService.RunChatLoopAsync();
```

### Step 4.2: Console UI Service

Create in `Microbot.Console/Services/`:

**ConsoleUIService.cs:**
```csharp
namespace Microbot.Console.Services;

using Spectre.Console;

public class ConsoleUIService
{
    public async Task ShowWelcomeAsync()
    {
        AnsiConsole.Clear();
        
        var panel = new Panel(
            Align.Center(
                new FigletText("Microbot")
                    .Color(Color.Cyan1)))
        {
            Border = BoxBorder.Double,
            BorderStyle = new Style(Color.Cyan1)
        };
        
        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
        
        AnsiConsole.MarkupLine("[grey]Your personal AI assistant powered by Semantic Kernel[/]");
        AnsiConsole.WriteLine();
    }

    public async Task ShowMessageAsync(string message, string type = "info")
    {
        var color = type switch
        {
            "error" => "red",
            "warning" => "yellow",
            "success" => "green",
            _ => "blue"
        };
        
        AnsiConsole.MarkupLine($"[{color}]{message}[/]");
    }

    public async Task ShowSkillsLoadedAsync(IEnumerable<string> skillNames)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Loaded Skills")
            .BorderColor(Color.Green);

        foreach (var name in skillNames)
        {
            table.AddRow($"[green]✓[/] {name}");
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    public string GetUserInput()
    {
        return AnsiConsole.Prompt(
            new TextPrompt<string>("[cyan]You:[/]")
                .PromptStyle("white"));
    }

    public void ShowAgentResponse(string response)
    {
        var panel = new Panel(response)
        {
            Header = new PanelHeader("[yellow]Microbot[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Yellow)
        };
        
        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    public async Task ShowProgressAsync(string message, Func<Task> action)
    {
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("cyan"))
            .StartAsync(message, async ctx => await action());
    }
}
```

### Step 4.3: Agent Service

Create in `Microbot.Console/Services/`:

**AgentService.cs:**
```csharp
namespace Microbot.Console.Services;

using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microbot.Core.Models;
using Microbot.Skills;

public class AgentService
{
    private readonly ConsoleUIService _ui;
    private Kernel? _kernel;
    private ChatCompletionAgent? _agent;
    private SkillManager? _skillManager;
    private ChatHistory _chatHistory = [];

    public AgentService(ConsoleUIService ui)
    {
        _ui = ui;
    }

    public async Task InitializeAsync(MicrobotConfig config)
    {
        await _ui.ShowProgressAsync("Initializing Semantic Kernel...", async () =>
        {
            var builder = Kernel.CreateBuilder();

            // Configure AI provider
            switch (config.AiProvider.Provider.ToLowerInvariant())
            {
                case "azureopenai":
                    builder.AddAzureOpenAIChatCompletion(
                        config.AiProvider.ModelId,
                        config.AiProvider.Endpoint!,
                        config.AiProvider.ApiKey!);
                    break;
                case "openai":
                    builder.AddOpenAIChatCompletion(
                        config.AiProvider.ModelId,
                        config.AiProvider.ApiKey!);
                    break;
                default:
                    throw new NotSupportedException(
                        $"AI provider {config.AiProvider.Provider} is not supported");
            }

            _kernel = builder.Build();
        });

        await _ui.ShowProgressAsync("Loading skills...", async () =>
        {
            _skillManager = new SkillManager(config.Skills);
            var plugins = await _skillManager.LoadAllSkillsAsync();
            _skillManager.RegisterPluginsWithKernel(_kernel!);
        });

        var skillNames = _skillManager!.LoadedPlugins.Select(p => p.Name);
        await _ui.ShowSkillsLoadedAsync(skillNames);

        // Create the agent
        _agent = new ChatCompletionAgent
        {
            Name = "Microbot",
            Instructions = """
                You are Microbot, a helpful personal AI assistant.
                You have access to various tools and skills to help the user.
                Be concise, helpful, and friendly in your responses.
                When using tools, explain what you're doing.
                """,
            Kernel = _kernel!,
            Arguments = new KernelArguments(
                new OpenAIPromptExecutionSettings
                {
                    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
                })
        };
    }

    public async Task RunChatLoopAsync()
    {
        await _ui.ShowMessageAsync("Ready! Type 'exit' to quit.", "success");
        
        while (true)
        {
            var input = _ui.GetUserInput();
            
            if (string.IsNullOrWhiteSpace(input))
                continue;
                
            if (input.Equals("exit", StringComparison.OrdinalIgnoreCase))
                break;

            _chatHistory.AddUserMessage(input);

            try
            {
                var response = new System.Text.StringBuilder();
                
                await foreach (var message in _agent!.InvokeAsync(_chatHistory))
                {
                    response.Append(message.Content);
                }

                var responseText = response.ToString();
                _chatHistory.AddAssistantMessage(responseText);
                _ui.ShowAgentResponse(responseText);
            }
            catch (Exception ex)
            {
                await _ui.ShowMessageAsync($"Error: {ex.Message}", "error");
            }
        }

        await _ui.ShowMessageAsync("Goodbye!", "info");
    }
}
```

## Phase 5: Aspire Integration

### Step 5.1: AppHost Program.cs

Create in `Microbot.AppHost/`:

**Program.cs:**
```csharp
var builder = DistributedApplication.CreateBuilder(args);

var microbot = builder.AddProject<Projects.Microbot_Console>("microbot")
    .WithEnvironment("ASPIRE_ENABLED", "true");

builder.Build().Run();
```

### Step 5.2: Service Defaults

Update `Microbot.ServiceDefaults/Extensions.cs`:

```csharp
namespace Microbot.ServiceDefaults;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

public static class Extensions
{
    public static IHostApplicationBuilder AddServiceDefaults(
        this IHostApplicationBuilder builder)
    {
        builder.ConfigureOpenTelemetry();
        return builder;
    }

    public static IHostApplicationBuilder ConfigureOpenTelemetry(
        this IHostApplicationBuilder builder)
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddRuntimeInstrumentation()
                       .AddBuiltInMeters();
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource("Microsoft.SemanticKernel*");
            });

        return builder;
    }

    private static MeterProviderBuilder AddBuiltInMeters(
        this MeterProviderBuilder meterProviderBuilder) =>
        meterProviderBuilder.AddMeter(
            "Microsoft.AspNetCore.Hosting",
            "Microsoft.AspNetCore.Server.Kestrel",
            "System.Net.Http");
}
```

## Phase 6: Configuration Wizard (Future Implementation)

The configuration wizard will be implemented in a future phase. It will include:

1. **AI Provider Selection** - Choose between OpenAI, Azure OpenAI, or other providers
2. **API Key Configuration** - Securely enter and store API credentials
3. **Skill Configuration** - Enable/disable and configure available skills
4. **Preference Settings** - Set user preferences like theme and logging level

## Testing the Implementation

### Step 1: Build the Solution

```bash
dotnet build
```

### Step 2: Create a Test Configuration

Create `Microbot.config` in the root directory:

```json
{
    "version": "1.0",
    "aiProvider": {
        "provider": "AzureOpenAI",
        "modelId": "gpt-4o",
        "endpoint": "https://your-endpoint.openai.azure.com/",
        "apiKey": "your-api-key"
    },
    "skills": {
        "mcpFolder": "./skills/mcp",
        "nugetFolder": "./skills/nuget",
        "mcpServers": [],
        "nugetSkills": []
    },
    "preferences": {
        "theme": "default",
        "verboseLogging": false
    }
}
```

### Step 3: Run the Application

```bash
# Run directly
cd src/Microbot.Console
dotnet run

# Or run via Aspire
cd src/Microbot.AppHost
dotnet run
```

## Implementation Status

### Phase 1: Foundation ✅ COMPLETED
- [x] Create solution structure
- [x] Set up Microbot.Console with Spectre.Console
- [x] Implement basic configuration system
- [x] Create Microbot.config file handling

### Phase 2: Semantic Kernel Integration ✅ COMPLETED
- [x] Set up Semantic Kernel with ChatCompletionAgent
- [x] Implement basic chat loop
- [x] Add AI provider configuration (OpenAI, Azure OpenAI, Ollama)

### Phase 3: Skill Loading System ✅ COMPLETED
- [x] Implement MCP skill loader
- [x] Implement NuGet skill loader
- [x] Create NuGet to MCP wrapper
- [x] Add skill discovery and registration

### Phase 4: Aspire Integration ✅ COMPLETED
- [x] Add Aspire AppHost project
- [x] Configure service defaults
- [x] Set up telemetry and monitoring

### Phase 5: Configuration Wizard ✅ COMPLETED
- [x] Implement first-time setup wizard
- [x] Add configuration validation
- [x] Create interactive prompts

### Phase 6: AI Provider Support ✅ COMPLETED
- [x] OpenAI provider support
- [x] Azure OpenAI provider support
- [x] Ollama provider support (via OpenAI-compatible API at `http://localhost:11434/v1`)

### Phase 7: Outlook Skill ✅ COMPLETED
- [x] Create Microbot.Skills.Outlook project
- [x] Implement OutlookSkillMode enum (ReadOnly, ReadWriteCalendar, Full)
- [x] Add OutlookSkillConfig to MicrobotConfig
- [x] Implement OutlookAuthenticationService (Device Code and Interactive Browser flows)
- [x] Implement OutlookSkill with KernelFunction methods
- [x] Add email reading functions (list_emails, get_email, search_emails)
- [x] Add email sending functions (send_email, reply_to_email, forward_email)
- [x] Add calendar reading functions (list_calendar_events, get_calendar_event)
- [x] Add calendar write functions (create_calendar_event, update_calendar_event, delete_calendar_event)
- [x] Implement permission checking based on OutlookSkillMode
- [x] Create OutlookSkillLoader and register with SkillManager
- [x] Update configuration wizard for Outlook skill setup
- [x] Document Azure AD app registration steps (see plans/outlook-skill-implementation.md)

### Phase 8: Skill Configuration Commands ✅ COMPLETED
- [x] Create AvailableSkill model in Microbot.Core/Models
- [x] Add GetAvailableSkills method to SkillManager
- [x] Update ConsoleUIService with DisplayAvailableSkills method
- [x] Create SkillConfigurationService for interactive skill configuration
- [x] Implement Outlook skill configuration wizard
- [x] Add ReloadSkillsAsync method to AgentService
- [x] Update Program.cs to handle /skills avail and /skills config commands
- [x] Update DisplayHelp with new commands
- [x] Document implementation in plans/skill-configuration-plan.md

#### New Commands Added
- `/skills avail` - Lists all available skills with their current status (Enabled/Disabled/Not Configured)
- `/skills config <skillname>` - Runs an interactive wizard to configure a specific skill

#### Files Created/Modified
| File | Action | Description |
|------|--------|-------------|
| `Microbot.Core/Models/AvailableSkill.cs` | Created | Model for available skills with status |
| `Microbot.Skills/SkillManager.cs` | Modified | Added GetAvailableSkills() method |
| `Microbot.Console/Services/ConsoleUIService.cs` | Modified | Added DisplayAvailableSkills(), updated DisplayHelp() |
| `Microbot.Console/Services/SkillConfigurationService.cs` | Created | Interactive skill configuration wizards |
| `Microbot.Console/Services/AgentService.cs` | Modified | Added ReloadSkillsAsync() method |
| `Microbot.Console/Program.cs` | Modified | Added /skills avail and /skills config command handlers |
| `plans/skill-configuration-plan.md` | Created | Detailed implementation documentation |

### Phase 9: MCP Registry Integration ✅ COMPLETED
- [x] Research MCP Registry API at https://registry.modelcontextprotocol.io
- [x] Create MCP Registry models (McpRegistryServer, McpRegistryPackage, McpEnvironmentVariable, McpRegistryResponse)
- [x] Implement McpRegistryClient for API communication
- [x] Implement McpRegistryService for business logic
- [x] Add /mcp list command with pagination (50 items per page)
- [x] Add /mcp list <search> command for searching servers
- [x] Add /mcp install <name> command to install servers from registry
- [x] Add /mcp info <name> command to show server details
- [x] Update McpServerConfig with registry tracking fields (RegistryName, RegistryVersion, RegistryPackageType, RegistryPackageId)
- [x] Add McpEnvVarDefinition model for environment variable documentation
- [x] Implement environment variable expansion syntax (${env:VAR_NAME})
- [x] Update McpSkillLoader to expand environment variables
- [x] Update ConsoleUIService.DisplayHelp() with /mcp commands

#### MCP Registry Commands
| Command | Description |
|---------|-------------|
| `/mcp list` | List all MCP servers from the registry (paginated, 50 per page) |
| `/mcp list <search>` | Search for MCP servers by name or description |
| `/mcp install <name>` | Install an MCP server from the registry |
| `/mcp info <name>` | Show details about a specific MCP server |

#### Environment Variable Syntax
The configuration supports special syntax for loading values from system environment variables:
- `${env:VAR_NAME}` - Load from system environment variable (recommended)
- `${VAR_NAME}` - Legacy syntax, also loads from system environment variable
- `%VAR_NAME%` - Windows environment variable syntax

Example configuration:
```json
{
  "skills": {
    "mcpServers": [
      {
        "name": "github-mcp",
        "command": "npx",
        "args": ["-y", "@modelcontextprotocol/server-github"],
        "env": {
          "GITHUB_TOKEN": "${env:GITHUB_TOKEN}"
        },
        "enabled": true,
        "registryName": "io.github/github-mcp",
        "registryVersion": "1.0.0"
      }
    ]
  }
}
```

#### Files Created/Modified
| File | Action | Description |
|------|--------|-------------|
| `Microbot.Core/Models/McpRegistry/McpRegistryServer.cs` | Created | Server model from registry API |
| `Microbot.Core/Models/McpRegistry/McpRegistryPackage.cs` | Created | Package model (npm/oci) |
| `Microbot.Core/Models/McpRegistry/McpEnvironmentVariable.cs` | Created | Environment variable definition |
| `Microbot.Core/Models/McpRegistry/McpRegistryResponse.cs` | Created | API response wrappers |
| `Microbot.Core/Services/McpRegistryClient.cs` | Created | HTTP client for registry API |
| `Microbot.Console/Services/McpRegistryService.cs` | Created | Business logic for registry operations |
| `Microbot.Core/Models/MicrobotConfig.cs` | Modified | Added registry tracking fields and McpEnvVarDefinition |
| `Microbot.Skills/Loaders/McpSkillLoader.cs` | Modified | Added ExpandEnvironmentValue() method |
| `Microbot.Console/Program.cs` | Modified | Added /mcp command handlers |
| `Microbot.Console/Services/ConsoleUIService.cs` | Modified | Updated DisplayHelp() with /mcp commands |

### Phase 10: MCP Tool Parameter Metadata Fix ✅ COMPLETED
- [x] Identified issue: MCP tools were not working because parameter metadata was not being extracted
- [x] Updated McpSkillLoader.CreateKernelFunctionFromMcpTool() to use KernelFunctionFromMethodOptions
- [x] Implemented ExtractParameterMetadata() to parse MCP tool's JsonSchema
- [x] Added GetParameterType() to map JSON schema types to .NET types
- [x] Added GetDefaultValue() to extract default values from JSON schema
- [x] Added proper KernelReturnParameterMetadata for function return type

#### Problem Description
MCP tools were being loaded but not working correctly with Semantic Kernel because:
1. The `KernelFunction` was created without parameter metadata
2. Semantic Kernel couldn't determine what parameters to pass to the function
3. The AI model couldn't properly invoke the tools

#### Solution
Updated `McpSkillLoader.cs` to:
1. Extract parameter metadata from the MCP tool's `JsonSchema` property
2. Parse the JSON schema to get property names, types, descriptions, and required flags
3. Create `KernelParameterMetadata` for each parameter
4. Use `KernelFunctionFromMethodOptions` to pass the metadata when creating the function

#### Files Modified
| File | Action | Description |
|------|--------|-------------|
| `Microbot.Skills/Loaders/McpSkillLoader.cs` | Modified | Added parameter metadata extraction from MCP tool InputSchema |

#### Key Code Changes
```csharp
// Extract parameter metadata from the MCP tool's input schema
var parameters = ExtractParameterMetadata(tool);

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
```

### Phase 11: Windows Command Resolution Fix ✅ COMPLETED
- [x] Identified issue: MCP servers using commands like `npx` fail on Windows
- [x] Root cause: Windows doesn't automatically resolve `.cmd`/`.bat` extensions when spawning processes
- [x] Implemented ResolveCommand() method to find the full path of commands on Windows
- [x] Added PATH environment variable search with common Windows extensions (.cmd, .bat, .exe)
- [x] Added platform detection to only apply Windows-specific resolution on Windows

#### Problem Description
When running MCP servers on Windows, commands like `npx` would fail because:
1. `Process.Start` (used by `StdioClientTransport`) doesn't automatically resolve `.cmd` extensions
2. The actual executable is `npx.cmd` in the Node.js installation directory
3. The MCP server would fail to start, and the AI would report "no MCP installed"

#### Solution
Added `ResolveCommand()` method in `McpSkillLoader.cs` that:
1. Checks if running on Windows (skips resolution on other platforms)
2. Searches the PATH environment variable for the command
3. Tries common Windows extensions: `.cmd`, `.bat`, `.exe`, and no extension
4. Returns the full path to the executable if found

#### Files Modified
| File | Action | Description |
|------|--------|-------------|
| `Microbot.Skills/Loaders/McpSkillLoader.cs` | Modified | Added ResolveCommand() method for Windows command resolution |

#### Key Code Changes
```csharp
/// <summary>
/// Resolves a command to its full path on Windows.
/// On Windows, commands like "npx" need to be resolved to "npx.cmd" because
/// Process.Start doesn't automatically resolve .cmd/.bat extensions.
/// </summary>
private static string ResolveCommand(string command)
{
    if (!OperatingSystem.IsWindows())
        return command;

    // Search PATH for the command with Windows extensions
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

    return command;
}
```

### Phase 12: Timezone and Time Context in System Prompt ✅ COMPLETED
- [x] Added current timezone information to agent system prompt
- [x] Added current local time and UTC time to system prompt
- [x] Agent can now understand time-related queries with proper context

#### Implementation Details
The system prompt now includes a "Current Time Context" section that provides:
- **Timezone**: Display name and ID (e.g., "FLE Standard Time (Europe/Helsinki)")
- **UTC Offset**: Formatted offset (e.g., "+02:00" or "-05:00")
- **Current Local Time**: Formatted as "yyyy-MM-dd HH:mm:ss" with timezone name
- **Current UTC Time**: Formatted as "yyyy-MM-dd HH:mm:ss UTC"

This information is captured at agent initialization time and helps the agent:
- Understand "today", "now", "this week" references
- Schedule events at appropriate times
- Convert between timezones when needed
- Provide accurate time-based responses

#### Files Modified
| File | Action | Description |
|------|--------|-------------|
| `Microbot.Console/Services/AgentService.cs` | Modified | Added timezone and time info to GetSystemPrompt() |

### Phase 13: Browser Skill (Playwright MCP) ✅ COMPLETED
- [x] Create BrowserSkillConfig in MicrobotConfig.cs
- [x] Create BrowserSkillLoader in Microbot.Skills/Loaders
- [x] Update SkillManager to load Browser skill
- [x] Add Browser skill to GetAvailableSkills()
- [x] Add Browser configuration wizard to SkillConfigurationService
- [x] Update AGENTS.md with Browser skill documentation
- [x] Create plans/browser-skill-implementation.md

#### Browser Skill Features
| Feature | Description |
|---------|-------------|
| Web Navigation | Navigate to URLs, go back/forward, refresh |
| Element Interaction | Click, type, hover, drag-and-drop |
| Page Snapshots | Accessibility tree for AI understanding |
| Screenshots | Capture page or element screenshots |
| PDF Generation | Generate PDFs (with pdf capability) |
| Form Filling | Fill multiple form fields at once |
| Tab Management | Create, close, switch tabs |
| Console/Network | Access browser logs and network requests |
| Device Emulation | Emulate mobile devices (iPhone, Pixel, etc.) |

#### Configuration Options
| Option | Default | Description |
|--------|---------|-------------|
| `enabled` | `true` | Enable/disable Browser skill |
| `browser` | `chromium` | Browser engine (chromium, firefox, webkit) |
| `headless` | `true` | Run without visible window |
| `viewportWidth` | `1280` | Browser viewport width |
| `viewportHeight` | `720` | Browser viewport height |
| `actionTimeoutMs` | `30000` | Timeout for actions |
| `navigationTimeoutMs` | `60000` | Timeout for navigation |
| `isolated` | `true` | Use isolated sessions |
| `capabilities` | `[]` | Optional: pdf, vision, testing, tracing |
| `outputDir` | `./browser-outputs` | Output directory |

#### Files Created/Modified
| File | Action | Description |
|------|--------|-------------|
| `Microbot.Core/Models/MicrobotConfig.cs` | Modified | Added BrowserSkillConfig class |
| `Microbot.Skills/Loaders/BrowserSkillLoader.cs` | Created | Browser skill loader using Playwright MCP |
| `Microbot.Skills/SkillManager.cs` | Modified | Added Browser skill loading and disposal |
| `Microbot.Console/Services/SkillConfigurationService.cs` | Modified | Added Browser configuration wizard |
| `AGENTS.md` | Modified | Added Browser skill documentation |
| `plans/browser-skill-implementation.md` | Created | Detailed implementation documentation |

#### Prerequisites
- Node.js must be installed for Playwright MCP to work
- Browser skill fails gracefully if Node.js is not available

### Phase 14: Teams Skill 🔲 PLANNED
- [ ] Create Microbot.Skills.Teams project
- [ ] Implement TeamsSkillMode enum (ReadOnly, Full)
- [ ] Add TeamsSkillConfig to MicrobotConfig
- [ ] Implement TeamsAuthenticationService (Device Code and Interactive Browser flows)
- [ ] Implement ReadStateTracker for local unread message tracking
- [ ] Implement TeamsSkill with KernelFunction methods
- [ ] Add team/channel functions (list_teams, list_channels, get_channel)
- [ ] Add channel message functions (list_channel_messages, get_channel_message, send_channel_message, reply_to_channel_message)
- [ ] Add chat functions (list_chats, list_chat_messages, send_chat_message)
- [ ] Add unread message functions (get_unread_channel_messages, get_unread_chat_messages, mark_channel_as_read, mark_chat_as_read)
- [ ] Implement permission checking based on TeamsSkillMode
- [ ] Create TeamsSkillLoader and register with SkillManager
- [ ] Update configuration wizard for Teams skill setup
- [ ] Document Azure AD app registration steps (see plans/teams-skill-implementation.md)

#### Teams Skill Features
| Feature | ReadOnly | Full |
|---------|----------|------|
| List teams (all tenants) | ✅ | ✅ |
| List channels | ✅ | ✅ |
| Read channel messages | ✅ | ✅ |
| Send channel messages | ❌ | ✅ |
| Reply to channel messages | ❌ | ✅ |
| List chats (1:1 and group) | ✅ | ✅ |
| Read chat messages | ✅ | ✅ |
| Send chat messages | ❌ | ✅ |
| Track unread messages | ✅ | ✅ |

#### Multi-Tenant Support
The Teams skill automatically discovers and works with all tenants the user has access to:
- Home tenant (user's primary organization)
- Guest tenants (organizations where user is invited as guest)
- Uses `TenantId = "common"` for multi-tenant authentication
- `joinedTeams` API returns teams from all accessible tenants

#### Unread Message Tracking
Since Microsoft Graph doesn't provide built-in unread tracking for channel messages:
- Local JSON file stores last-read timestamps per channel/chat
- `ReadStateTracker` service manages read state persistence
- Timestamps stored in `~/.microbot/teams-read-state.json`

#### Files to Create
| File | Description |
|------|-------------|
| `Microbot.Skills.Teams/TeamsSkill.cs` | Main skill with KernelFunction methods |
| `Microbot.Skills.Teams/TeamsSkillMode.cs` | Permission mode enum |
| `Microbot.Skills.Teams/Models/Team.cs` | Team model |
| `Microbot.Skills.Teams/Models/Channel.cs` | Channel model |
| `Microbot.Skills.Teams/Models/ChannelMessage.cs` | Channel message model |
| `Microbot.Skills.Teams/Models/Chat.cs` | Chat model |
| `Microbot.Skills.Teams/Models/ChatMessage.cs` | Chat message model |
| `Microbot.Skills.Teams/Services/TeamsAuthenticationService.cs` | Authentication service |
| `Microbot.Skills.Teams/Services/ReadStateTracker.cs` | Local unread tracking |
| `Microbot.Skills/Loaders/TeamsSkillLoader.cs` | Skill loader |

#### Required Graph API Permissions
| Permission | Type | ReadOnly | Full |
|------------|------|----------|------|
| Team.ReadBasic.All | Delegated | ✅ | ✅ |
| Channel.ReadBasic.All | Delegated | ✅ | ✅ |
| ChannelMessage.Read.All | Delegated | ✅ | ✅ |
| ChannelMessage.Send | Delegated | ❌ | ✅ |
| Chat.Read | Delegated | ✅ | ✅ |
| ChatMessage.Read | Delegated | ✅ | ✅ |
| ChatMessage.Send | Delegated | ❌ | ✅ |
| User.Read | Delegated | ✅ | ✅ |

See `plans/teams-skill-implementation.md` for complete implementation details.

## Next Steps

After completing this implementation:

1. **Add Sample Skills** - Create example MCP servers and NuGet skill packages
2. ~~**Implement Configuration Wizard**~~ - ✅ Completed
3. **Add Conversation History** - Persist chat history between sessions
4. **Create Custom Aspire Dashboard** - Web UI for monitoring and configuration
5. ~~**Add Skill Hot-Reload**~~ - ✅ Completed (via /skills config and reload prompt)
6. ~~**Skill Registry**~~ - ✅ Completed (MCP Registry integration)
7. **MCP Server Configuration** - Add /skills config mcp to add/edit MCP servers
8. **NuGet Skill Configuration** - Add /skills config nuget to add/edit NuGet skills
9. **MCP Server Updates** - Add /mcp update command to check for and apply updates
