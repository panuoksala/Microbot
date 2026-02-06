# Microbot

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Semantic Kernel](https://img.shields.io/badge/Semantic%20Kernel-1.70+-blue)](https://github.com/microsoft/semantic-kernel)

**Microbot** is an agentic AI application built with [Microsoft Semantic Kernel](https://github.com/microsoft/semantic-kernel). It acts as your personal AI assistant with extensible skills for productivity, communication, and task automation.

![Microbot Demo](docs/images/demo.gif) <!-- TODO: Add demo gif -->

## ✨ Features

### Core Capabilities
- 🤖 **AI-Powered Chat** - Conversational AI assistant using Semantic Kernel's ChatCompletionAgent
- 🔌 **MCP Server Support** - Load tools from [Model Context Protocol](https://modelcontextprotocol.io/) servers
- 📦 **NuGet Skill Packages** - Dynamically load skills from .NET assemblies
- 🎨 **Beautiful Console UI** - Rich terminal interface with markdown rendering using Spectre.Console
- 🧠 **Long-term Memory** - Vector-based memory system with hybrid search (semantic + full-text)
- ⏰ **Task Scheduling** - Schedule recurring and one-time tasks with natural language

### Built-in Skills
- 📧 **Outlook** - Read/send emails, manage calendar events (Microsoft Graph)
- 💬 **Slack** - Read/send messages in channels and DMs
- 👥 **Teams** - Access Teams chats and channels (Microsoft Graph)
- 🎫 **YouTrack** - Manage issues and projects in JetBrains YouTrack
- 📅 **Scheduling** - Create automated tasks with cron expressions or natural language

### Safety & Observability
- 🛡️ **Agentic Loop Safety** - Iteration limits, function call limits, and timeouts
- 📊 **.NET Aspire Integration** - Monitoring and orchestration support
- 📝 **Session Transcripts** - Automatic conversation logging

## 🚀 Quick Start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- An AI provider account:
  - [Azure OpenAI](https://azure.microsoft.com/products/ai-services/openai-service) (recommended)
  - [OpenAI](https://platform.openai.com/)
  - [Ollama](https://ollama.ai/) (local, free)

### Installation

```bash
# Clone the repository
git clone https://github.com/yourusername/Microbot.git
cd Microbot

# Build the solution
dotnet build

# Run Microbot
dotnet run --project src/Microbot.Console
```

On first run, Microbot will guide you through the initial configuration:
1. Select your AI provider (Azure OpenAI, OpenAI, or Ollama)
2. Enter your model/deployment name
3. Provide your API endpoint and key
4. Choose a name for your AI assistant

### Start Chatting!

Once configured, you can start chatting with your AI assistant. Type `/help` to see available commands.

## 📖 Documentation

### Configuration

Microbot stores its configuration in `Microbot.config` (JSON format). This file is created automatically during first-run setup.

<details>
<summary>Example Configuration</summary>

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
    "outlook": { "enabled": false },
    "slack": { "enabled": false },
    "teams": { "enabled": false },
    "youTrack": { "enabled": false },
    "scheduling": { "enabled": true }
  },
  "preferences": {
    "agentName": "Microbot",
    "maxHistoryMessages": 100,
    "useStreaming": true
  },
  "agentLoop": {
    "maxIterations": 10,
    "maxTotalFunctionCalls": 50,
    "runtimeTimeoutSeconds": 600
  },
  "memory": {
    "enabled": true,
    "databasePath": "./memory/microbot-memory.db"
  }
}
```

</details>

### Commands

| Command | Description |
|---------|-------------|
| `/help` | Show available commands |
| `/skills` | List loaded skills and their functions |
| `/clear` | Clear the screen and chat history |
| `/config` | Show/edit current configuration |
| `/history` | Show chat history message count |
| `/reload` | Reload configuration from file |
| `/memory status` | Show memory system status |
| `/memory search <query>` | Search long-term memory |
| `/schedule list` | List scheduled tasks |
| `/exit` | Exit the application |

### Adding Skills

#### MCP Servers

MCP (Model Context Protocol) servers provide tools that the AI can use. Add servers via the configuration wizard (`/config`) or manually:

```json
{
  "skills": {
    "mcpServers": [
      {
        "name": "filesystem",
        "description": "File system operations",
        "command": "npx",
        "args": ["-y", "@modelcontextprotocol/server-filesystem", "/path/to/directory"],
        "env": {},
        "enabled": true
      }
    ]
  }
}
```

#### Custom NuGet Skills

Create custom skills as .NET class libraries:

```csharp
using Microsoft.SemanticKernel;
using System.ComponentModel;

public class MyCustomSkills
{
    [KernelFunction("get_current_time")]
    [Description("Gets the current date and time")]
    public string GetCurrentTime() => DateTime.Now.ToString("F");
}
```

Build and copy the DLL to `skills/nuget/`, then add to configuration:

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

### Skill Configuration

#### Outlook Skill
Requires an Azure AD app registration with Microsoft Graph permissions:
- `Mail.Read`, `Mail.Send` (for email)
- `Calendars.Read`, `Calendars.ReadWrite` (for calendar)

#### Slack Skill
Requires a Slack Bot Token (`xoxb-...`) with appropriate scopes:
- `channels:read`, `channels:history` (for channels)
- `im:read`, `im:history`, `chat:write` (for DMs)

#### YouTrack Skill
Requires a YouTrack Permanent Token generated from:
Profile → Account Security → Tokens → New Token

## 🏗️ Project Structure

```
Microbot/
├── src/
│   ├── Microbot.Console/          # Main console application
│   ├── Microbot.Core/             # Core models and interfaces
│   ├── Microbot.Memory/           # Long-term memory system
│   ├── Microbot.Skills/           # Skill loading infrastructure
│   ├── Microbot.Skills.Outlook/   # Outlook/Calendar skill
│   ├── Microbot.Skills.Slack/     # Slack skill
│   ├── Microbot.Skills.Teams/     # Microsoft Teams skill
│   ├── Microbot.Skills.YouTrack/  # YouTrack skill
│   ├── Microbot.Skills.Scheduling/# Task scheduling skill
│   ├── Microbot.AppHost/          # .NET Aspire host
│   └── Microbot.ServiceDefaults/  # Aspire service defaults
├── skills/
│   ├── mcp/                       # MCP server configurations
│   └── nuget/                     # NuGet skill packages
├── memory/                        # Memory storage
│   └── sessions/                  # Session transcripts
└── plans/                         # Architecture documentation
```

## 🔧 Development

### Building

```bash
dotnet build Microbot.slnx
```

### Running with .NET Aspire

For monitoring and observability:

```bash
dotnet run --project src/Microbot.AppHost
```

### Creating a Release Build

```bash
dotnet publish src/Microbot.Console -c Release -o ./publish
```

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

Please read [CONTRIBUTING.md](CONTRIBUTING.md) for details on our code of conduct and the process for submitting pull requests.

## 📋 Roadmap

- [x] Core chat functionality with Semantic Kernel
- [x] MCP server integration
- [x] NuGet skill loading
- [x] Outlook/Calendar skill
- [x] Slack skill
- [x] YouTrack skill
- [x] Long-term memory system
- [x] Task scheduling
- [x] Agentic loop safety mechanisms
- [ ] Teams skill (in progress)
- [ ] Web UI dashboard
- [ ] Plugin marketplace integration
- [ ] Multi-agent orchestration
- [ ] Voice input/output support

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🙏 Acknowledgments

- [Microsoft Semantic Kernel](https://github.com/microsoft/semantic-kernel) - AI orchestration framework
- [Model Context Protocol](https://modelcontextprotocol.io/) - Tool integration standard
- [Spectre.Console](https://spectreconsole.net/) - Beautiful console UI
- [.NET Aspire](https://learn.microsoft.com/dotnet/aspire/) - Cloud-native development

---

<p align="center">
  Made with ❤️ using .NET and Semantic Kernel
</p>
