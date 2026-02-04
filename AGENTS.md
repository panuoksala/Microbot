# AGENTS.md - Microbot Development Guidelines

This file provides instructions for AI agents working on the Microbot codebase.

## Project Overview

Microbot is an agentic AI application built with Microsoft Semantic Kernel that acts as a personal bot supporting various tools/skills. It supports:
- **MCP Servers** - Model Context Protocol servers for external tool integration
- **NuGet Packages** - .NET assemblies loaded dynamically as skills
- **Multiple AI Providers** - OpenAI, Azure OpenAI, and Ollama

## Documentation Structure

### Plans Folder (`plans/`)

The `plans/` folder contains implementation details and architecture documentation:

- **`plans/microbot-architecture.md`** - High-level architecture, component diagrams, and design decisions
- **`plans/implementation-plan.md`** - Step-by-step implementation guide with code examples

**IMPORTANT**: Always update the plans folder when implementing new features:
1. Update `implementation-plan.md` with new implementation details
2. Update `microbot-architecture.md` if architecture changes
3. Mark completed phases/steps in the implementation plan
4. Add new phases for future work

## Before Making Changes

1. **Read the plans folder** - Review `plans/implementation-plan.md` and `plans/microbot-architecture.md` to understand the current state
2. **Check existing implementations** - The codebase may already have features implemented
3. **Update plans after implementation** - Always update the plans to reflect what was implemented

## Project Structure

```
Microbot/
├── AGENTS.md                    # This file - development guidelines
├── plans/                       # Architecture and implementation plans
│   ├── microbot-architecture.md # High-level architecture
│   ├── implementation-plan.md   # Detailed implementation guide
│   ├── agentic-loop-implementation.md # Agentic loop safety mechanisms
│   ├── memory-system-implementation.md # Long-term memory system
│   ├── outlook-skill-implementation.md # Outlook skill implementation details
│   └── youtrack-skill-implementation.md # YouTrack skill implementation details
├── memory/                      # Memory storage folder
│   └── sessions/                # Session transcripts
├── skills/                      # Runtime skill folders
│   ├── mcp/                     # MCP server configurations
│   └── nuget/                   # NuGet package DLLs
└── src/
    ├── Microbot.Console/        # Main console application
    │   ├── Filters/             # Semantic Kernel filters (safety, timeout)
    │   └── Services/            # Console services (UI, Agent, Markdown rendering)
    ├── Microbot.Core/           # Core domain logic and models
    │   └── Events/              # Lifecycle events for agent loop
    ├── Microbot.Memory/         # Long-term memory system
    │   ├── Chunking/            # Markdown-aware text chunking
    │   ├── Data/                # EF Core database context and entities
    │   ├── Embeddings/          # Embedding providers (OpenAI, Azure, Ollama)
    │   ├── Search/              # Vector and hybrid search
    │   ├── Sessions/            # Session transcript management
    │   ├── Skills/              # Memory Semantic Kernel plugin
    │   └── Sync/                # File watching and synchronization
    ├── Microbot.Skills/         # Skill loading infrastructure
    ├── Microbot.Skills.Outlook/ # Outlook skill (Microsoft Graph integration)
    ├── Microbot.Skills.Slack/   # Slack skill (SlackNet integration)
    ├── Microbot.Skills.Teams/   # Teams skill (Microsoft Graph integration)
    ├── Microbot.Skills.YouTrack/# YouTrack skill (JetBrains issue tracker)
    ├── Microbot.ServiceDefaults/# Aspire service defaults
    └── Microbot.AppHost/        # Aspire AppHost
```

## Key Files

| File | Purpose |
|------|---------|
| `src/Microbot.Console/Program.cs` | Application entry point and setup wizard |
| `src/Microbot.Console/Services/AgentService.cs` | AI agent orchestration with agentic loop safety |
| `src/Microbot.Console/Services/ConsoleUIService.cs` | Console UI rendering with Spectre.Console |
| `src/Microbot.Console/Services/MarkdownRenderer.cs` | Markdown to Spectre.Console formatting converter |
| `src/Microbot.Console/Filters/SafetyLimitFilter.cs` | Iteration and function call limiting |
| `src/Microbot.Console/Filters/TimeoutFilter.cs` | Function timeout enforcement |
| `src/Microbot.Core/Models/MicrobotConfig.cs` | Configuration models including AgentLoopConfig |
| `src/Microbot.Core/Events/AgentLoopEvents.cs` | Lifecycle events for agent loop monitoring |
| `src/Microbot.Skills/SkillManager.cs` | Skill loading and management |

## Current Implementation Status

### Completed Features
- ✅ Project structure and solution setup
- ✅ Configuration system with JSON serialization
- ✅ Semantic Kernel integration with ChatCompletionAgent
- ✅ MCP skill loader
- ✅ NuGet skill loader
- ✅ Console UI with Spectre.Console
- ✅ Markdown rendering in AI responses
  - Headers (H1-H6) with color-coded styling
  - Bold, italic, strikethrough text formatting
  - Code blocks with language indication panels
  - Inline code with background highlighting
  - Ordered and unordered lists with proper indentation
  - Links with clickable formatting
  - Blockquotes and horizontal rules
- ✅ First-time setup wizard
- ✅ AI Provider support: OpenAI, Azure OpenAI, Ollama
- ✅ Streaming responses (collected and rendered with markdown)
- ✅ Chat history management
- ✅ Outlook skill with Microsoft Graph integration
  - Email reading (list, get, search)
  - Email sending (send, reply, forward)
  - Calendar reading (list, get events)
  - Calendar management (create, update, delete events)
  - Permission modes: ReadOnly, ReadWriteCalendar, Full
  - Authentication: Device Code and Interactive Browser flows
- ✅ Agentic loop with safety mechanisms
  - Iteration limiting (max 10 iterations per request)
  - Function call limiting (max 50 calls per request)
  - Runtime timeout (600 seconds default)
  - Function timeout (30 seconds per function)
  - Lifecycle events for monitoring
  - Progress display in console
  - System prompt safety guidelines
  - See plans/agentic-loop-implementation.md for details
- ✅ Slack skill with SlackNet integration
  - Channel messages (list, read, send)
  - Direct messages (list, read, send)
  - Unread message tracking (local timestamp storage)
  - Permission modes: ReadOnly, Full
  - Bot Token authentication (xoxb-)
  - See plans/slack-skill-implementation.md for details
- ✅ Long-term memory system
  - SQLite database with EF Core
  - Markdown-aware text chunking (ML.Tokenizers with cl100k_base)
  - Multiple embedding providers (OpenAI, Azure OpenAI, Ollama)
  - Hybrid search (vector similarity + FTS5 full-text search)
  - Session transcript management
  - File watching and automatic sync
  - Memory Semantic Kernel plugin
  - Console commands (/memory status, sync, search, sessions, save)
  - See plans/memory-system-implementation.md for details
- ✅ YouTrack skill with JetBrains YouTrack integration
  - Issue management (list, get, search, create, update)
  - Comment management (list, add, update)
  - Project listing and details
  - Command execution (change state, assignee, priority, etc.)
  - Permission modes: ReadOnly, FullControl
  - Permanent token authentication
  - See plans/youtrack-skill-implementation.md for details
- 🔲 Teams skill with Microsoft Graph integration (planned)
  - Multi-tenant support (home + guest tenants)
  - Channel messages (read, send, reply)
  - Chat messages (read, send)
  - Unread message tracking (local timestamp storage)
  - Permission modes: ReadOnly, Full
  - See plans/teams-skill-implementation.md for details

### AI Provider Configuration

The application supports three AI providers configured in `AgentService.ConfigureAiProvider()`:

1. **OpenAI** - Direct OpenAI API
2. **Azure OpenAI** - Azure-hosted OpenAI models
3. **Ollama** - Local LLM via OpenAI-compatible API (endpoint: `http://localhost:11434/v1`)

## Development Guidelines

### Adding New AI Providers

1. Add the provider option in `Program.cs` `RunMinimalSetupAsync()` method
2. Add the provider case in `AgentService.cs` `ConfigureAiProvider()` method
3. Update `plans/implementation-plan.md` with the new provider details
4. Update this file's "Current Implementation Status" section

### Adding New Skills

1. For MCP servers: Add configuration to `skills/mcp/servers.json`
2. For NuGet skills: Place DLLs in `skills/nuget/` folder
3. Update `plans/implementation-plan.md` if adding new skill loading capabilities

### Modifying Configuration

1. Update models in `src/Microbot.Core/Models/MicrobotConfig.cs`
2. Update the setup wizard in `Program.cs` if new settings need user input
3. Update `plans/microbot-architecture.md` configuration section

## Commands

```bash
# Build the solution
dotnet build

# Run the console application
cd src/Microbot.Console
dotnet run

# Run via Aspire AppHost
cd src/Microbot.AppHost
dotnet run
```

## Technology Stack

| Component | Technology | Version |
|-----------|------------|---------|
| Runtime | .NET | 10 |
| AI Framework | Microsoft Semantic Kernel | 1.70.0+ |
| MCP SDK | ModelContextProtocol | Latest |
| Console UI | Spectre.Console | 0.54.0+ |
| Orchestration | .NET Aspire | Latest |
