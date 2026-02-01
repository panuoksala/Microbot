# Slack Skill Implementation Plan

This document provides a detailed implementation plan for the Slack skill in Microbot.

## Overview

The Slack skill enables Microbot to interact with Slack workspaces via the Slack Web API. It supports two permission modes for different access levels.

| Mode | Capabilities |
|------|-------------|
| **ReadOnly** | Read channels, direct messages, and message history |
| **Full** | ReadOnly + send messages to channels and direct messages |

## Key Features

1. **Channel Messages**: Read and send messages in public and private channels
2. **Direct Messages**: Read and send direct messages (1:1 and group DMs)
3. **Permission Modes**: ReadOnly and Full modes for different access levels
4. **Bot Token Authentication**: Uses Slack Bot Token for API access
5. **Unread Message Tracking**: Tracks last read timestamps locally to identify new/unread messages

## Architecture

```mermaid
graph TB
    subgraph Microbot Application
        Agent[ChatCompletionAgent]
        Kernel[Semantic Kernel]
        SkillManager[Skill Manager]
    end

    subgraph Slack Skill
        SlackSkill[SlackSkill Plugin]
        AuthService[SlackAuthenticationService]
        ModeChecker[Permission Mode Checker]
        ReadTracker[ReadStateTracker]
    end

    subgraph Slack API
        SlackClient[SlackClient]
        ConversationsAPI[Conversations API]
        ChatAPI[Chat API]
        UsersAPI[Users API]
    end

    subgraph Local Storage
        ReadState[slack-read-state.json]
    end

    Agent --> Kernel
    Kernel --> SkillManager
    SkillManager --> SlackSkill
    SlackSkill --> ModeChecker
    SlackSkill --> SlackClient
    SlackSkill --> ReadTracker
    ReadTracker --> ReadState
    AuthService --> SlackClient
    SlackClient --> ConversationsAPI
    SlackClient --> ChatAPI
    SlackClient --> UsersAPI
```

## Prerequisites

### Slack App Setup

Before using the Slack skill, you need to create a Slack App:

1. Go to [Slack API](https://api.slack.com/apps) and click **Create New App**
2. Choose **From scratch** and give your app a name
3. Select the workspace where you want to install the app
4. Go to **OAuth & Permissions** in the sidebar
5. Add the following **Bot Token Scopes** based on your mode:

| Scope | ReadOnly | Full | Description |
|-------|----------|------|-------------|
| `channels:read` | ✅ | ✅ | View basic channel info |
| `channels:history` | ✅ | ✅ | View messages in public channels |
| `groups:read` | ✅ | ✅ | View basic private channel info |
| `groups:history` | ✅ | ✅ | View messages in private channels |
| `im:read` | ✅ | ✅ | View basic DM info |
| `im:history` | ✅ | ✅ | View messages in DMs |
| `mpim:read` | ✅ | ✅ | View basic group DM info |
| `mpim:history` | ✅ | ✅ | View messages in group DMs |
| `users:read` | ✅ | ✅ | View user info |
| `chat:write` | ❌ | ✅ | Send messages |
| `chat:write.public` | ❌ | ✅ | Send messages to public channels without joining |

6. Click **Install to Workspace** and authorize the app
7. Copy the **Bot User OAuth Token** (starts with `xoxb-`)

**Note**: For private channels and DMs, the bot must be invited to the channel/conversation.

## Project Structure

```
src/
├── Microbot.Skills.Slack/              # New project
│   ├── Microbot.Skills.Slack.csproj
│   ├── SlackSkill.cs                   # Main skill with KernelFunctions
│   ├── SlackSkillMode.cs               # Permission mode enum
│   ├── Services/
│   │   ├── SlackAuthenticationService.cs
│   │   └── SlackReadStateTracker.cs    # Tracks last read timestamps
│   └── Models/
│       ├── SlackChannel.cs
│       ├── SlackMessage.cs
│       ├── SlackUser.cs
│       ├── SlackConversation.cs
│       └── SlackReadState.cs
├── Microbot.Core/
│   └── Models/
│       └── MicrobotConfig.cs           # Updated with SlackSkillConfig
└── Microbot.Skills/
    ├── SkillManager.cs                 # Updated to load Slack skill
    └── Loaders/
        └── SlackSkillLoader.cs         # New loader
```

## Implementation Details

### Phase 1: Project Setup and Configuration

#### Step 1.1: Create Microbot.Skills.Slack Project

```bash
cd src
dotnet new classlib -n Microbot.Skills.Slack -o Microbot.Skills.Slack
cd ..
dotnet sln add src/Microbot.Skills.Slack/Microbot.Skills.Slack.csproj
```

#### Step 1.2: Project File

**Microbot.Skills.Slack.csproj:**
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Microbot.Core\Microbot.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="SlackNet" Version="0.15.0" />
    <PackageReference Include="Microsoft.SemanticKernel" Version="1.70.0" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.2" />
  </ItemGroup>

</Project>
```

#### Step 1.3: Define SlackSkillMode Enum

**SlackSkillMode.cs:**
```csharp
namespace Microbot.Skills.Slack;

/// <summary>
/// Defines the permission mode for the Slack skill.
/// </summary>
public enum SlackSkillMode
{
    /// <summary>
    /// Read-only access to channels, DMs, and messages.
    /// Scopes: channels:read, channels:history, groups:read, groups:history,
    ///         im:read, im:history, mpim:read, mpim:history, users:read
    /// </summary>
    ReadOnly,

    /// <summary>
    /// Full access: read and send messages to channels and DMs.
    /// Scopes: All ReadOnly scopes + chat:write, chat:write.public
    /// </summary>
    Full
}
```

#### Step 1.4: Update MicrobotConfig

Add to **MicrobotConfig.cs** in `Microbot.Core/Models/`:

```csharp
/// <summary>
/// Configuration for the Slack skill.
/// </summary>
public class SlackSkillConfig
{
    /// <summary>
    /// Whether the Slack skill is enabled.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// The permission mode for the Slack skill: "ReadOnly" or "Full".
    /// </summary>
    public string Mode { get; set; } = "ReadOnly";

    /// <summary>
    /// Slack Bot User OAuth Token (starts with xoxb-).
    /// </summary>
    public string? BotToken { get; set; }

    /// <summary>
    /// Optional: App-level token for Socket Mode (starts with xapp-).
    /// Not required for basic API access.
    /// </summary>
    public string? AppToken { get; set; }

    /// <summary>
    /// Path to store read state (last read timestamps).
    /// </summary>
    public string ReadStatePath { get; set; } = "./slack-read-state.json";
}
```

Update **SkillsConfig** class:
```csharp
public class SkillsConfig
{
    // ... existing properties ...

    /// <summary>
    /// Slack skill configuration.
    /// </summary>
    public SlackSkillConfig Slack { get; set; } = new();
}
```

### Phase 2: Model Classes

#### Step 2.1: SlackChannel Model

**Models/SlackChannel.cs:**
```csharp
namespace Microbot.Skills.Slack.Models;

/// <summary>
/// Represents a Slack channel (public or private).
/// </summary>
public class SlackChannel
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Topic { get; set; }
    public string? Purpose { get; set; }
    public bool IsPrivate { get; set; }
    public bool IsArchived { get; set; }
    public bool IsMember { get; set; }
    public int MemberCount { get; set; }
    public DateTime CreatedDateTime { get; set; }
}
```

#### Step 2.2: SlackMessage Model

**Models/SlackMessage.cs:**
```csharp
namespace Microbot.Skills.Slack.Models;

/// <summary>
/// Represents a message in Slack.
/// </summary>
public class SlackMessage
{
    public string Id { get; set; } = string.Empty;
    public string ChannelId { get; set; } = string.Empty;
    public string ChannelName { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public bool IsUnread { get; set; }
    public string? ThreadTs { get; set; }
    public int ReplyCount { get; set; }
}
```

#### Step 2.3: SlackUser Model

**Models/SlackUser.cs:**
```csharp
namespace Microbot.Skills.Slack.Models;

/// <summary>
/// Represents a Slack user.
/// </summary>
public class SlackUser
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string RealName { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public bool IsBot { get; set; }
    public bool IsAdmin { get; set; }
}
```

#### Step 2.4: SlackConversation Model

**Models/SlackConversation.cs:**
```csharp
namespace Microbot.Skills.Slack.Models;

/// <summary>
/// Represents a Slack conversation (DM or group DM).
/// </summary>
public class SlackConversation
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsDirectMessage { get; set; }
    public bool IsGroupMessage { get; set; }
    public List<string> MemberIds { get; set; } = [];
    public List<string> MemberNames { get; set; } = [];
    public DateTime? LastActivity { get; set; }
}
```

#### Step 2.5: SlackReadState Model

**Models/SlackReadState.cs:**
```csharp
namespace Microbot.Skills.Slack.Models;

/// <summary>
/// Tracks the last read timestamps for channels and conversations.
/// </summary>
public class SlackReadState
{
    /// <summary>
    /// Last read timestamp per channel (key: channelId).
    /// </summary>
    public Dictionary<string, DateTime> ChannelLastRead { get; set; } = [];

    /// <summary>
    /// Last read timestamp per conversation (key: conversationId).
    /// </summary>
    public Dictionary<string, DateTime> ConversationLastRead { get; set; } = [];

    /// <summary>
    /// Last time the read state was updated.
    /// </summary>
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}
```

### Phase 3: Services

#### Step 3.1: SlackReadStateTracker Service

**Services/SlackReadStateTracker.cs:**
```csharp
namespace Microbot.Skills.Slack.Services;

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microbot.Skills.Slack.Models;

/// <summary>
/// Tracks and persists the last read timestamps for Slack channels and conversations.
/// </summary>
public class SlackReadStateTracker
{
    private readonly string _statePath;
    private SlackReadState _state;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ILogger<SlackReadStateTracker>? _logger;

    public SlackReadStateTracker(
        string statePath = "./slack-read-state.json",
        ILogger<SlackReadStateTracker>? logger = null)
    {
        _statePath = statePath;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        _state = new SlackReadState();
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(_statePath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(_statePath, cancellationToken);
                _state = JsonSerializer.Deserialize<SlackReadState>(json, _jsonOptions) ?? new SlackReadState();
                _logger?.LogInformation("Loaded Slack read state from {Path}", _statePath);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to load Slack read state, starting fresh");
                _state = new SlackReadState();
            }
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        _state.LastUpdated = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(_state, _jsonOptions);
        await File.WriteAllTextAsync(_statePath, json, cancellationToken);
    }

    public DateTime? GetChannelLastRead(string channelId)
    {
        return _state.ChannelLastRead.TryGetValue(channelId, out var timestamp) ? timestamp : null;
    }

    public void MarkChannelAsRead(string channelId, DateTime? timestamp = null)
    {
        _state.ChannelLastRead[channelId] = timestamp ?? DateTime.UtcNow;
    }

    public bool IsChannelMessageUnread(string channelId, DateTime messageTimestamp)
    {
        var lastRead = GetChannelLastRead(channelId);
        return lastRead == null || messageTimestamp > lastRead;
    }

    public DateTime? GetConversationLastRead(string conversationId)
    {
        return _state.ConversationLastRead.TryGetValue(conversationId, out var timestamp) ? timestamp : null;
    }

    public void MarkConversationAsRead(string conversationId, DateTime? timestamp = null)
    {
        _state.ConversationLastRead[conversationId] = timestamp ?? DateTime.UtcNow;
    }

    public bool IsConversationMessageUnread(string conversationId, DateTime messageTimestamp)
    {
        var lastRead = GetConversationLastRead(conversationId);
        return lastRead == null || messageTimestamp > lastRead;
    }
}
```

#### Step 3.2: SlackAuthenticationService

**Services/SlackAuthenticationService.cs:**
```csharp
namespace Microbot.Skills.Slack.Services;

using Microsoft.Extensions.Logging;
using SlackNet;
using SlackNet.WebApi;
using Microbot.Core.Models;

/// <summary>
/// Handles authentication for the Slack skill using Bot Token.
/// </summary>
public class SlackAuthenticationService
{
    private readonly SlackSkillConfig _config;
    private readonly ILogger<SlackAuthenticationService>? _logger;
    private ISlackApiClient? _slackClient;

    public SlackAuthenticationService(
        SlackSkillConfig config,
        ILogger<SlackAuthenticationService>? logger = null)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Gets the required scopes for the configured mode.
    /// </summary>
    public string[] GetRequiredScopes()
    {
        var mode = Enum.Parse<SlackSkillMode>(_config.Mode, ignoreCase: true);

        return mode switch
        {
            SlackSkillMode.ReadOnly => new[]
            {
                "channels:read",
                "channels:history",
                "groups:read",
                "groups:history",
                "im:read",
                "im:history",
                "mpim:read",
                "mpim:history",
                "users:read"
            },
            SlackSkillMode.Full => new[]
            {
                "channels:read",
                "channels:history",
                "groups:read",
                "groups:history",
                "im:read",
                "im:history",
                "mpim:read",
                "mpim:history",
                "users:read",
                "chat:write",
                "chat:write.public"
            },
            _ => throw new ArgumentException($"Unknown mode: {_config.Mode}")
        };
    }

    /// <summary>
    /// Creates and returns an authenticated Slack API client.
    /// </summary>
    public async Task<ISlackApiClient> GetSlackClientAsync(CancellationToken cancellationToken = default)
    {
        if (_slackClient != null)
            return _slackClient;

        if (string.IsNullOrEmpty(_config.BotToken))
            throw new InvalidOperationException("BotToken is required for Slack skill");

        _logger?.LogInformation("Initializing Slack client...");

        _slackClient = new SlackServiceBuilder()
            .UseApiToken(_config.BotToken)
            .GetApiClient();

        // Verify authentication by getting bot info
        var authTest = await _slackClient.Auth.Test(cancellationToken);
        _logger?.LogInformation("Authenticated as: {BotName} in workspace: {Team}", 
            authTest.User, authTest.Team);

        return _slackClient;
    }

    /// <summary>
    /// Checks if the current mode allows sending messages.
    /// </summary>
    public bool CanSendMessages()
    {
        var mode = Enum.Parse<SlackSkillMode>(_config.Mode, ignoreCase: true);
        return mode == SlackSkillMode.Full;
    }
}
```

### Phase 4: SlackSkill Implementation

See the full implementation in the code files.

### Phase 5: Skill Loader and Integration

#### Step 5.1: SlackSkillLoader

**Loaders/SlackSkillLoader.cs** (in Microbot.Skills project):
```csharp
namespace Microbot.Skills.Loaders;

using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microbot.Core.Interfaces;
using Microbot.Core.Models;
using Microbot.Skills.Slack;

/// <summary>
/// Loads the Slack skill if configured and enabled.
/// </summary>
public class SlackSkillLoader : ISkillLoader
{
    private readonly SlackSkillConfig _config;
    private readonly ILogger<SlackSkillLoader>? _logger;
    private SlackSkill? _slackSkill;

    public string LoaderName => "Slack";

    public SlackSkillLoader(
        SlackSkillConfig config,
        ILogger<SlackSkillLoader>? logger = null)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<IEnumerable<KernelPlugin>> LoadSkillsAsync(
        CancellationToken cancellationToken = default)
    {
        var plugins = new List<KernelPlugin>();

        if (!_config.Enabled)
        {
            _logger?.LogInformation("Slack skill is disabled");
            return plugins;
        }

        if (string.IsNullOrEmpty(_config.BotToken))
        {
            _logger?.LogWarning("Slack skill is enabled but BotToken is not configured. " +
                "Please configure the BotToken in your Microbot.config file.");
            return plugins;
        }

        try
        {
            _logger?.LogInformation("Loading Slack skill in {Mode} mode", _config.Mode);

            _slackSkill = new SlackSkill(
                _config,
                _logger as ILogger<SlackSkill>);

            await _slackSkill.InitializeAsync(cancellationToken);

            var plugin = KernelPluginFactory.CreateFromObject(_slackSkill, "Slack");
            plugins.Add(plugin);

            _logger?.LogInformation(
                "Slack skill loaded successfully with {Count} functions in {Mode} mode",
                plugin.Count(),
                _config.Mode);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load Slack skill");
        }

        return plugins;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
```

## Testing

### Test Configuration

Create a test configuration in `Microbot.config`:

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
        "slack": {
            "enabled": true,
            "mode": "ReadOnly",
            "botToken": "xoxb-your-bot-token",
            "readStatePath": "./slack-read-state.json"
        }
    }
}
```

### Test Scenarios

#### ReadOnly Mode Tests

1. **List Channels**
   - "List all my Slack channels"
   - "Show me the channels I'm in"

2. **Read Channel Messages**
   - "Show me recent messages in #general"
   - "What are the unread messages in #engineering?"
   - "Get all unread channel messages"

3. **List Direct Messages**
   - "List my recent DMs"
   - "Show me my direct message conversations"

4. **Read DM Messages**
   - "Show me messages from my DM with @john"
   - "What are my unread DMs?"

5. **Mark as Read**
   - "Mark #general as read"
   - "Mark my DM with @john as read"

#### Full Mode Tests

1. All ReadOnly tests
2. **Send Channel Message**
   - "Send a message to #general saying 'Hello team!'"
   - "Post an update in #engineering"

3. **Send Direct Message**
   - "Send a DM to @john saying 'Hi!'"
   - "Message @sarah about the meeting"

## Security Considerations

1. **Token Storage**: Bot tokens should be stored securely (consider environment variables)
2. **Scope Limitation**: Only request scopes needed for the configured mode
3. **No User Tokens**: Uses bot tokens only, not user tokens
4. **Channel Access**: Bot must be invited to private channels to access them
5. **Read State**: Local read state file should be protected

## API Rate Limits

Slack has rate limits that should be considered:
- Tier 1: 1+ per minute
- Tier 2: 20+ per minute
- Tier 3: 50+ per minute
- Tier 4: 100+ per minute

Most conversation methods are Tier 3 or 4. The skill implementation includes:
- Pagination support for large result sets
- Error handling for rate limit responses
- Configurable limits on message counts

## Future Enhancements

1. **Reactions**: Add/remove reactions to messages
2. **File Uploads**: Upload and share files
3. **Threads**: Better thread support
4. **User Presence**: Show user online/offline status
5. **Search**: Search messages across channels
6. **Slash Commands**: Support for slash commands
7. **Interactive Messages**: Support for buttons and menus
8. **Socket Mode**: Real-time events via WebSocket
