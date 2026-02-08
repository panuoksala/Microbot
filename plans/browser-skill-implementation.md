# Browser Skill Implementation Plan

This document describes the implementation of the built-in Browser feature for Microbot using Playwright MCP.

## Overview

The Browser skill provides web automation capabilities through the Playwright MCP server. Unlike other MCP servers that are configured externally, the Browser skill is a **built-in feature** that is automatically configured and managed by Microbot.

### Key Features

- **Web Navigation**: Navigate to URLs, go back/forward, refresh pages
- **Element Interaction**: Click, type, hover, drag-and-drop on page elements
- **Page Snapshots**: Capture accessibility tree snapshots for AI understanding
- **Screenshots**: Take screenshots of pages or specific elements
- **Form Filling**: Fill multiple form fields at once
- **Tab Management**: Create, close, and switch between browser tabs
- **Console/Network Monitoring**: Access browser console logs and network requests
- **PDF Generation**: Generate PDFs from web pages (optional capability)
- **Vision Mode**: Coordinate-based interactions for complex scenarios (optional)

## Architecture

### Integration Approach

The Browser skill follows the same pattern as other built-in skills (Outlook, Teams, Slack, YouTrack) but with a key difference: it uses the Playwright MCP server under the hood rather than a custom implementation.

```
┌─────────────────────────────────────────────────────────────────┐
│                        Microbot                                  │
├─────────────────────────────────────────────────────────────────┤
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐ │
│  │  AgentService   │  │  SkillManager   │  │ ConfigService   │ │
│  └────────┬────────┘  └────────┬────────┘  └────────┬────────┘ │
│           │                    │                     │          │
│           │    ┌───────────────┴───────────────┐    │          │
│           │    │                               │    │          │
│           ▼    ▼                               ▼    ▼          │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │                    BrowserSkillLoader                    │   │
│  │  - Manages Playwright MCP server lifecycle               │   │
│  │  - Configures browser options from BrowserSkillConfig    │   │
│  │  - Exposes MCP tools as Semantic Kernel functions        │   │
│  └─────────────────────────────────────────────────────────┘   │
│                              │                                  │
│                              ▼                                  │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │              Playwright MCP Server                       │   │
│  │  npx @playwright/mcp@latest                              │   │
│  │  - Browser automation via accessibility snapshots        │   │
│  │  - No vision model required                              │   │
│  └─────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
```

### Why Built-in vs External MCP?

1. **Simplified Configuration**: Users don't need to manually configure the Playwright MCP server
2. **Integrated Settings**: Browser settings are part of the main Microbot configuration
3. **Automatic Lifecycle Management**: Browser server starts/stops with Microbot
4. **Consistent Experience**: Same configuration pattern as other built-in skills
5. **Default Enabled**: Browser capability is available out-of-the-box

## Configuration Model

### BrowserSkillConfig

```csharp
/// <summary>
/// Configuration for the built-in Browser skill using Playwright MCP.
/// </summary>
public class BrowserSkillConfig
{
    /// <summary>
    /// Whether the Browser skill is enabled.
    /// Default: true (enabled by default as a core feature)
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Browser to use: "chromium", "firefox", or "webkit".
    /// Default: "chromium"
    /// </summary>
    public string Browser { get; set; } = "chromium";

    /// <summary>
    /// Whether to run the browser in headless mode.
    /// Default: true (headless for server/automation scenarios)
    /// </summary>
    public bool Headless { get; set; } = true;

    /// <summary>
    /// Browser viewport width in pixels.
    /// Default: 1280
    /// </summary>
    public int ViewportWidth { get; set; } = 1280;

    /// <summary>
    /// Browser viewport height in pixels.
    /// Default: 720
    /// </summary>
    public int ViewportHeight { get; set; } = 720;

    /// <summary>
    /// Timeout for browser actions in milliseconds.
    /// Default: 30000 (30 seconds)
    /// </summary>
    public int ActionTimeoutMs { get; set; } = 30000;

    /// <summary>
    /// Timeout for navigation in milliseconds.
    /// Default: 60000 (60 seconds)
    /// </summary>
    public int NavigationTimeoutMs { get; set; } = 60000;

    /// <summary>
    /// Whether to use isolated browser sessions (no persistent profile).
    /// Default: true
    /// </summary>
    public bool Isolated { get; set; } = true;

    /// <summary>
    /// Path to user data directory for persistent browser profile.
    /// Only used when Isolated is false.
    /// </summary>
    public string? UserDataDir { get; set; }

    /// <summary>
    /// Optional capabilities to enable: "pdf", "vision", "testing", "tracing".
    /// Default: empty (core capabilities only)
    /// </summary>
    public List<string> Capabilities { get; set; } = [];

    /// <summary>
    /// Output directory for screenshots, PDFs, and other browser outputs.
    /// Default: "./browser-outputs"
    /// </summary>
    public string OutputDir { get; set; } = "./browser-outputs";

    /// <summary>
    /// Optional proxy server URL.
    /// </summary>
    public string? ProxyServer { get; set; }

    /// <summary>
    /// Origins to block (e.g., ad servers).
    /// </summary>
    public List<string> BlockedOrigins { get; set; } = [];

    /// <summary>
    /// Device to emulate (e.g., "iPhone 15", "Pixel 7").
    /// </summary>
    public string? Device { get; set; }
}
```

### Example Configuration

```json
{
  "skills": {
    "browser": {
      "enabled": true,
      "browser": "chromium",
      "headless": true,
      "viewportWidth": 1280,
      "viewportHeight": 720,
      "actionTimeoutMs": 30000,
      "navigationTimeoutMs": 60000,
      "isolated": true,
      "capabilities": ["pdf"],
      "outputDir": "./browser-outputs",
      "blockedOrigins": ["ads.example.com"]
    }
  }
}
```

## Implementation Details

### BrowserSkillLoader

The `BrowserSkillLoader` class manages the Playwright MCP server lifecycle and exposes its tools to Semantic Kernel.

```csharp
namespace Microbot.Skills.Loaders;

using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microbot.Core.Interfaces;
using Microbot.Core.Models;
using ModelContextProtocol.Client;

/// <summary>
/// Loads the built-in Browser skill using Playwright MCP server.
/// </summary>
public class BrowserSkillLoader : ISkillLoader, IAsyncDisposable
{
    private readonly BrowserSkillConfig _config;
    private readonly ILogger<BrowserSkillLoader>? _logger;
    private McpClient? _mcpClient;
    private bool _disposed;

    public string LoaderName => "Browser";

    public BrowserSkillLoader(
        BrowserSkillConfig config,
        ILogger<BrowserSkillLoader>? logger = null)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<IEnumerable<KernelPlugin>> LoadSkillsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_config.Enabled)
        {
            _logger?.LogInformation("Browser skill is disabled");
            return [];
        }

        _logger?.LogInformation("Starting Playwright MCP server...");

        // Build command arguments
        var args = BuildCommandArgs();

        // Create MCP client transport
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = GetNpxCommand(),
            Arguments = args,
            Name = "playwright-browser"
        });

        // Create MCP client with timeout
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(60)); // 60 second startup timeout

        _mcpClient = await McpClient.CreateAsync(transport, cancellationToken: timeoutCts.Token);

        // Get available tools
        var tools = await _mcpClient.ListToolsAsync();
        var toolsList = tools.ToList();

        _logger?.LogInformation("Playwright MCP server started with {ToolCount} tools", toolsList.Count);

        // Convert MCP tools to Kernel functions
        var functions = toolsList.Select(tool => CreateKernelFunction(tool));

        return [KernelPluginFactory.CreateFromFunctions(
            "Browser",
            "Web browser automation using Playwright - navigate pages, click elements, fill forms, take screenshots",
            functions)];
    }

    private string[] BuildCommandArgs()
    {
        var args = new List<string> { "@playwright/mcp@latest" };

        // Browser selection
        args.Add($"--browser={_config.Browser}");

        // Headless mode
        if (_config.Headless)
            args.Add("--headless");

        // Viewport
        args.Add($"--viewport-size={_config.ViewportWidth}x{_config.ViewportHeight}");

        // Timeouts
        args.Add($"--timeout-action={_config.ActionTimeoutMs}");
        args.Add($"--timeout-navigation={_config.NavigationTimeoutMs}");

        // Isolated sessions
        if (_config.Isolated)
            args.Add("--isolated");
        else if (!string.IsNullOrEmpty(_config.UserDataDir))
            args.Add($"--user-data-dir={_config.UserDataDir}");

        // Capabilities
        if (_config.Capabilities.Count > 0)
            args.Add($"--caps={string.Join(",", _config.Capabilities)}");

        // Output directory
        args.Add($"--output-dir={_config.OutputDir}");

        // Proxy
        if (!string.IsNullOrEmpty(_config.ProxyServer))
            args.Add($"--proxy-server={_config.ProxyServer}");

        // Blocked origins
        if (_config.BlockedOrigins.Count > 0)
            args.Add($"--blocked-origins={string.Join(";", _config.BlockedOrigins)}");

        // Device emulation
        if (!string.IsNullOrEmpty(_config.Device))
            args.Add($"--device={_config.Device}");

        return args.ToArray();
    }

    private static string GetNpxCommand()
    {
        // On Windows, use cmd.exe to run npx
        return OperatingSystem.IsWindows() ? "cmd.exe" : "npx";
    }

    // ... CreateKernelFunction implementation similar to McpSkillLoader
}
```

### SkillManager Updates

Add Browser skill loader initialization in `SkillManager`:

```csharp
// In SkillManager constructor
if (config.Browser?.Enabled == true)
{
    _browserLoader = new BrowserSkillLoader(
        config.Browser,
        loggerFactory?.CreateLogger<BrowserSkillLoader>());
}

// In LoadAllSkillsAsync
if (_browserLoader != null)
{
    try
    {
        _logger?.LogInformation("Loading Browser skill...");
        var browserPlugins = await _browserLoader.LoadSkillsAsync(cancellationToken);
        _loadedPlugins.AddRange(browserPlugins);
        _logger?.LogInformation("Loaded {Count} Browser plugins", browserPlugins.Count());
    }
    catch (Exception ex)
    {
        _logger?.LogError(ex, "Error loading Browser skill");
    }
}
```

### Available Browser Tools

The Playwright MCP server provides the following tools:

| Tool | Description |
|------|-------------|
| `browser_navigate` | Navigate to a URL |
| `browser_navigate_back` | Go back in browser history |
| `browser_snapshot` | Capture accessibility tree snapshot |
| `browser_click` | Click on an element |
| `browser_hover` | Hover over an element |
| `browser_type` | Type text into an element |
| `browser_press_key` | Press a keyboard key |
| `browser_fill_form` | Fill multiple form fields |
| `browser_select_option` | Select dropdown option |
| `browser_drag` | Drag and drop between elements |
| `browser_take_screenshot` | Take a screenshot |
| `browser_tabs` | Manage browser tabs |
| `browser_wait_for` | Wait for text or time |
| `browser_console_messages` | Get console messages |
| `browser_network_requests` | Get network requests |
| `browser_evaluate` | Execute JavaScript |
| `browser_file_upload` | Upload files |
| `browser_handle_dialog` | Handle browser dialogs |
| `browser_close` | Close the browser |
| `browser_resize` | Resize browser window |
| `browser_install` | Install browser if needed |

### Configuration Wizard

Add Browser skill configuration to `SkillConfigurationService`:

```csharp
public async Task<bool> ConfigureBrowserSkillAsync(MicrobotConfig config)
{
    AnsiConsole.MarkupLine("[bold cyan]Browser Skill Configuration[/]");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("The Browser skill provides web automation capabilities using Playwright.");
    AnsiConsole.WriteLine();

    // Enable/disable
    var enabled = AnsiConsole.Confirm("Enable Browser skill?", true);
    config.Skills.Browser.Enabled = enabled;

    if (!enabled)
    {
        return true;
    }

    // Browser selection
    var browser = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title("Select browser:")
            .AddChoices("chromium", "firefox", "webkit"));
    config.Skills.Browser.Browser = browser;

    // Headless mode
    var headless = AnsiConsole.Confirm("Run in headless mode?", true);
    config.Skills.Browser.Headless = headless;

    // Viewport
    var viewportWidth = AnsiConsole.Prompt(
        new TextPrompt<int>("Viewport width:")
            .DefaultValue(1280));
    var viewportHeight = AnsiConsole.Prompt(
        new TextPrompt<int>("Viewport height:")
            .DefaultValue(720));
    config.Skills.Browser.ViewportWidth = viewportWidth;
    config.Skills.Browser.ViewportHeight = viewportHeight;

    // Capabilities
    var capabilities = AnsiConsole.Prompt(
        new MultiSelectionPrompt<string>()
            .Title("Select optional capabilities:")
            .NotRequired()
            .AddChoices("pdf", "vision", "testing", "tracing"));
    config.Skills.Browser.Capabilities = capabilities;

    return true;
}
```

## Usage Examples

### Basic Web Navigation

```
User: Go to github.com and tell me what's on the homepage

Microbot: I'll navigate to GitHub and describe what I see.
[Calls browser_navigate with url="https://github.com"]
[Calls browser_snapshot to get page content]

The GitHub homepage shows:
- A hero section with "Let's build from here" tagline
- Sign up and sign in buttons
- Featured repositories and trending projects
- ...
```

### Form Filling

```
User: Fill out the contact form on example.com/contact with my name "John Doe" and email "john@example.com"

Microbot: I'll navigate to the contact page and fill out the form.
[Calls browser_navigate with url="https://example.com/contact"]
[Calls browser_fill_form with fields for name and email]

I've filled out the contact form with:
- Name: John Doe
- Email: john@example.com

Would you like me to submit the form?
```

### Screenshot Capture

```
User: Take a screenshot of the current page

Microbot: [Calls browser_take_screenshot]

I've captured a screenshot of the current page. The screenshot has been saved to ./browser-outputs/page-{timestamp}.png
```

## Prerequisites

### Node.js Requirement

The Playwright MCP server requires Node.js to be installed. Microbot should check for Node.js availability and provide helpful error messages if not found.

```csharp
private static bool IsNodeJsAvailable()
{
    try
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "node",
            Arguments = "--version",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        });
        process?.WaitForExit();
        return process?.ExitCode == 0;
    }
    catch
    {
        return false;
    }
}
```

### Browser Installation

On first use, Playwright may need to download browser binaries. The `browser_install` tool can be used to install browsers if needed.

## Error Handling

### Common Errors

1. **Node.js not found**: Display message to install Node.js
2. **Browser not installed**: Suggest running `browser_install` tool
3. **Timeout errors**: Increase timeout settings in configuration
4. **Element not found**: Provide guidance on using `browser_snapshot` first

### Graceful Degradation

If the Browser skill fails to initialize:
- Log the error with details
- Continue loading other skills
- Inform user that Browser features are unavailable
- Suggest troubleshooting steps

## Security Considerations

1. **Isolated Sessions**: Default to isolated sessions to prevent data leakage
2. **Blocked Origins**: Allow blocking of ad/tracking domains
3. **No Persistent Credentials**: Don't store browser credentials by default
4. **Output Directory**: Ensure output directory is within workspace

## Files to Create/Modify

| File | Action | Description |
|------|--------|-------------|
| `Microbot.Core/Models/MicrobotConfig.cs` | Modify | Add BrowserSkillConfig class and property |
| `Microbot.Skills/Loaders/BrowserSkillLoader.cs` | Create | Browser skill loader using Playwright MCP |
| `Microbot.Skills/SkillManager.cs` | Modify | Add Browser skill loader initialization |
| `Microbot.Console/Services/SkillConfigurationService.cs` | Modify | Add Browser configuration wizard |
| `Microbot.Console/Program.cs` | Modify | Handle Browser skill in /skills commands |
| `AGENTS.md` | Modify | Document Browser skill feature |
| `plans/browser-skill-implementation.md` | Create | This document |
| `plans/implementation-plan.md` | Modify | Add Browser skill phase |

## Implementation Phases

### Phase 1: Core Implementation
1. Add `BrowserSkillConfig` to `MicrobotConfig.cs`
2. Create `BrowserSkillLoader.cs`
3. Update `SkillManager.cs` to load Browser skill
4. Test basic browser operations

### Phase 2: Configuration & UI
1. Add Browser configuration wizard
2. Update `/skills avail` to show Browser skill
3. Update `/skills config browser` command
4. Add Node.js availability check

### Phase 3: Documentation & Polish
1. Update `AGENTS.md`
2. Update `implementation-plan.md`
3. Add error handling and user guidance
4. Test across different platforms

## Testing

### Manual Testing

1. Enable Browser skill in configuration
2. Start Microbot
3. Test commands:
   - "Navigate to google.com"
   - "Take a screenshot"
   - "Click on the search box and type 'hello world'"
   - "Go back to the previous page"

### Automated Testing

Consider adding integration tests that:
- Verify Browser skill loads correctly
- Test navigation to a known URL
- Verify snapshot returns expected structure
- Test screenshot generation

## Future Enhancements

1. **Browser Extension Mode**: Support connecting to existing browser via extension
2. **Session Persistence**: Option to save/restore browser sessions
3. **Recording**: Record browser actions for replay
4. **Visual Comparison**: Compare screenshots for testing
5. **Multi-Browser**: Support running multiple browser instances
