# Microbot

**Microbot** is an agentic AI application built with Microsoft Semantic Kernel. It acts as your personal AI assistant and supports various tools/skills to accomplish tasks.

## Features

- 🤖 **AI-Powered Chat** - Conversational AI assistant using Semantic Kernel's ChatCompletionAgent
- 🔌 **MCP Server Support** - Load tools from Model Context Protocol (MCP) servers
- 📦 **NuGet Skill Packages** - Dynamically load skills from .NET assemblies
- 🎨 **Beautiful Console UI** - Rich terminal interface using Spectre.Console
- ⚙️ **Configuration Wizard** - First-time setup with guided configuration
- 📊 **.NET Aspire Integration** - Monitoring and orchestration support

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- An AI provider (Azure OpenAI, OpenAI, or Ollama)

## Getting Started

### 1. Clone and Build

```bash
git clone https://github.com/yourusername/Microbot.git
cd Microbot
dotnet build
```

### 2. Run the Application

```bash
dotnet run --project src/Microbot.Console
```

On first run, Microbot will guide you through the initial configuration:
- Select your AI provider (Azure OpenAI, OpenAI, or Ollama)
- Enter your model/deployment name
- Provide your API endpoint and key
- Choose a name for your AI assistant

### 3. Start Chatting!

Once configured, you can start chatting with your AI assistant. Use `/help` to see available commands.

## Configuration

Microbot stores its configuration in `Microbot.config` (JSON format). Here's an example:

```json
{
  "version": "1.0",
  "aiProvider": {
    "provider": "AzureOpenAI",
    "modelId": "gpt-4o",
    "endpoint": "https://your-resource.openai.azure.com/",
    "apiKey": "your-api-key"
  },
  "skills": {
    "mcpFolder": "./skills/mcp",
    "nuGetFolder": "./skills/nuget",
    "mcpServers": [],
    "nuGetSkills": []
  },
  "preferences": {
    "agentName": "Microbot",
    "theme": "default",
    "verboseLogging": false,
    "maxHistoryMessages": 100,
    "useStreaming": true
  }
}
```

## Adding Skills

### MCP Servers

MCP (Model Context Protocol) servers provide tools that the AI can use. To add an MCP server:

1. Add the server configuration to `Microbot.config`:

```json
{
  "skills": {
    "mcpServers": [
      {
        "name": "filesystem",
        "description": "File system operations",
        "command": "npx",
        "args": ["-y", "@modelcontextprotocol/server-filesystem", "/path/to/allowed/directory"],
        "env": {},
        "enabled": true
      }
    ]
  }
}
```

2. Or add servers to `skills/mcp/servers.json`:

```json
{
  "servers": [
    {
      "name": "weather",
      "description": "Weather information",
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-weather"],
      "env": {
        "API_KEY": "${WEATHER_API_KEY}"
      },
      "enabled": true
    }
  ]
}
```

### NuGet Skill Packages

You can create custom skills as .NET class libraries and load them dynamically:

1. Create a class library with methods decorated with `[KernelFunction]`:

```csharp
using Microsoft.SemanticKernel;
using System.ComponentModel;

public class MyCustomSkills
{
    [KernelFunction("get_current_time")]
    [Description("Gets the current date and time")]
    public string GetCurrentTime()
    {
        return DateTime.Now.ToString("F");
    }

    [KernelFunction("calculate")]
    [Description("Performs a calculation")]
    public double Calculate(
        [Description("First number")] double a,
        [Description("Second number")] double b,
        [Description("Operation: add, subtract, multiply, divide")] string operation)
    {
        return operation.ToLower() switch
        {
            "add" => a + b,
            "subtract" => a - b,
            "multiply" => a * b,
            "divide" => a / b,
            _ => throw new ArgumentException($"Unknown operation: {operation}")
        };
    }
}
```

2. Build and copy the DLL to `skills/nuget/`

3. Add the skill configuration to `Microbot.config`:

```json
{
  "skills": {
    "nuGetSkills": [
      {
        "name": "MyCustomSkills",
        "assemblyPath": "MyCustomSkills.dll",
        "enabled": true
      }
    ]
  }
}
```

## Commands

| Command | Description |
|---------|-------------|
| `/help` | Show available commands |
| `/skills` | List loaded skills and their functions |
| `/clear` | Clear the screen and chat history |
| `/config` | Show current configuration |
| `/history` | Show chat history message count |
| `/reload` | Reload configuration from file |
| `/exit` | Exit the application |

## Project Structure

```
Microbot/
├── src/
│   ├── Microbot.Console/       # Main console application
│   │   ├── Program.cs          # Entry point
│   │   └── Services/
│   │       ├── AgentService.cs     # AI agent management
│   │       └── ConsoleUIService.cs # Spectre.Console UI
│   ├── Microbot.Core/          # Core models and interfaces
│   │   ├── Configuration/
│   │   ├── Interfaces/
│   │   └── Models/
│   ├── Microbot.Skills/        # Skill loading system
│   │   └── Loaders/
│   │       ├── McpSkillLoader.cs   # MCP server loader
│   │       └── NuGetSkillLoader.cs # NuGet package loader
│   ├── Microbot.AppHost/       # .NET Aspire host
│   └── Microbot.ServiceDefaults/ # Aspire service defaults
├── skills/
│   ├── mcp/                    # MCP server configurations
│   │   └── servers.json
│   └── nuget/                  # NuGet skill packages
└── Microbot.slnx               # Solution file
```

## Running with .NET Aspire

To run Microbot with .NET Aspire for monitoring:

```bash
dotnet run --project src/Microbot.AppHost
```

This will start the Aspire dashboard where you can monitor the application.

## Supported AI Providers

| Provider | Configuration |
|----------|---------------|
| **Azure OpenAI** | Requires `endpoint` and `apiKey` |
| **OpenAI** | Requires `apiKey` |
| **Ollama** | Local LLM, optional `endpoint` (default: `http://localhost:11434/v1`) |

## Development

### Building

```bash
dotnet build Microbot.slnx
```

### Running Tests

```bash
dotnet test Microbot.slnx
```

### Creating a Release Build

```bash
dotnet publish src/Microbot.Console -c Release -o ./publish
```

## License

MIT License - see [LICENSE](LICENSE) for details.

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## Roadmap

- [ ] Full initialization wizard implementation
- [ ] Plugin marketplace integration
- [ ] Web UI dashboard
- [ ] Multi-agent orchestration
- [ ] Memory/context persistence
- [ ] Voice input/output support
