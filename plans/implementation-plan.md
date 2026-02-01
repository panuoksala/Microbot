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

## Next Steps

After completing this implementation:

1. **Add Sample Skills** - Create example MCP servers and NuGet skill packages
2. ~~**Implement Configuration Wizard**~~ - ✅ Completed
3. **Add Conversation History** - Persist chat history between sessions
4. **Create Custom Aspire Dashboard** - Web UI for monitoring and configuration
5. **Add Skill Hot-Reload** - Reload skills without restarting the application
