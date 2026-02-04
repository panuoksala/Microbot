namespace Microbot.Console.Services;

using Microbot.Core.Models;
using Spectre.Console;

/// <summary>
/// Service for configuring skills through interactive wizards.
/// </summary>
public class SkillConfigurationService
{
    private readonly ConsoleUIService _ui;

    /// <summary>
    /// Creates a new SkillConfigurationService instance.
    /// </summary>
    /// <param name="ui">The console UI service.</param>
    public SkillConfigurationService(ConsoleUIService ui)
    {
        _ui = ui;
    }

    /// <summary>
    /// Configures a skill by its ID.
    /// </summary>
    /// <param name="skillId">The skill ID to configure.</param>
    /// <param name="config">The current configuration to update.</param>
    /// <returns>True if configuration was successful, false if cancelled or failed.</returns>
    public bool ConfigureSkill(string skillId, MicrobotConfig config)
    {
        return skillId.ToLowerInvariant() switch
        {
            "outlook" => ConfigureOutlookSkill(config),
            "teams" => ConfigureTeamsSkill(config),
            "slack" => ConfigureSlackSkill(config),
            "youtrack" => ConfigureYouTrackSkill(config),
            _ => HandleUnknownSkill(skillId)
        };
    }

    /// <summary>
    /// Configures the Outlook skill.
    /// </summary>
    /// <param name="config">The configuration to update.</param>
    /// <returns>True if configuration was successful, false if cancelled.</returns>
    private bool ConfigureOutlookSkill(MicrobotConfig config)
    {
        AnsiConsole.Write(new Rule("[cyan]Outlook Skill Configuration[/]").RuleStyle("grey"));
        AnsiConsole.WriteLine();

        // Show current configuration if exists
        if (config.Skills.Outlook?.Enabled == true || !string.IsNullOrEmpty(config.Skills.Outlook?.ClientId))
        {
            DisplayCurrentOutlookConfig(config.Skills.Outlook);

            if (!AnsiConsole.Confirm("[yellow]Do you want to reconfigure?[/]", false))
            {
                return false;
            }
            AnsiConsole.WriteLine();
        }

        // Ensure Outlook config exists
        config.Skills.Outlook ??= new OutlookSkillConfig();

        // Enable/Disable
        var enable = AnsiConsole.Confirm(
            "[cyan]Enable Outlook skill?[/] (requires Azure AD app registration)",
            config.Skills.Outlook.Enabled);

        if (!enable)
        {
            config.Skills.Outlook.Enabled = false;
            _ui.DisplaySuccess("Outlook skill disabled.");
            return true;
        }

        config.Skills.Outlook.Enabled = true;

        // Mode selection
        var mode = _ui.SelectOption(
            "Select Outlook skill [green]permission mode[/]:",
            new[] { "ReadOnly", "ReadWriteCalendar", "Full" });
        config.Skills.Outlook.Mode = mode;

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

        // Show Azure AD setup instructions
        DisplayAzureAdSetupInstructions(mode);

        // Client ID
        var currentClientId = config.Skills.Outlook.ClientId;
        var clientIdPrompt = string.IsNullOrEmpty(currentClientId)
            ? "Enter your Azure AD Application (Client) ID:"
            : $"Enter your Azure AD Application (Client) ID [grey](current: {MaskString(currentClientId)})[/]:";
        
        config.Skills.Outlook.ClientId = _ui.PromptText(
            clientIdPrompt,
            currentClientId);

        // Tenant ID
        config.Skills.Outlook.TenantId = _ui.PromptText(
            "Enter your Tenant ID (or 'common' for multi-tenant):",
            config.Skills.Outlook.TenantId ?? "common");

        // Authentication method
        var authMethod = _ui.SelectOption(
            "Select authentication method:",
            new[] { "DeviceCode", "InteractiveBrowser" });
        config.Skills.Outlook.AuthenticationMethod = authMethod;

        if (authMethod == "InteractiveBrowser")
        {
            config.Skills.Outlook.RedirectUri = _ui.PromptText(
                "Enter Redirect URI:",
                config.Skills.Outlook.RedirectUri ?? "http://localhost");
        }

        AnsiConsole.WriteLine();
        _ui.DisplaySuccess("Outlook skill configured!");
        _ui.DisplayInfo("Note: You will be prompted to authenticate when the skill is first used.");
        AnsiConsole.WriteLine();

        return true;
    }

    /// <summary>
    /// Configures the Teams skill.
    /// </summary>
    /// <param name="config">The configuration to update.</param>
    /// <returns>True if configuration was successful, false if cancelled.</returns>
    private bool ConfigureTeamsSkill(MicrobotConfig config)
    {
        AnsiConsole.Write(new Rule("[cyan]Teams Skill Configuration[/]").RuleStyle("grey"));
        AnsiConsole.WriteLine();

        // Show current configuration if exists
        if (config.Skills.Teams?.Enabled == true || !string.IsNullOrEmpty(config.Skills.Teams?.ClientId))
        {
            DisplayCurrentTeamsConfig(config.Skills.Teams);

            if (!AnsiConsole.Confirm("[yellow]Do you want to reconfigure?[/]", false))
            {
                return false;
            }
            AnsiConsole.WriteLine();
        }

        // Ensure Teams config exists
        config.Skills.Teams ??= new TeamsSkillConfig();

        // Enable/Disable
        var enable = AnsiConsole.Confirm(
            "[cyan]Enable Teams skill?[/] (requires Azure AD app registration with multi-tenant support)",
            config.Skills.Teams.Enabled);

        if (!enable)
        {
            config.Skills.Teams.Enabled = false;
            _ui.DisplaySuccess("Teams skill disabled.");
            return true;
        }

        config.Skills.Teams.Enabled = true;

        // Mode selection
        var mode = _ui.SelectOption(
            "Select Teams skill [green]permission mode[/]:",
            new[] { "ReadOnly", "Full" });
        config.Skills.Teams.Mode = mode;

        // Display mode description
        var modeDescription = mode switch
        {
            "ReadOnly" => "Read teams, channels, chats, and messages only",
            "Full" => "Read teams/channels/chats and send messages",
            _ => ""
        };
        _ui.DisplayInfo($"Mode: {modeDescription}");
        AnsiConsole.WriteLine();

        // Show Azure AD setup instructions for Teams
        DisplayTeamsAzureAdSetupInstructions(mode);

        // Client ID
        var currentClientId = config.Skills.Teams.ClientId;
        var clientIdPrompt = string.IsNullOrEmpty(currentClientId)
            ? "Enter your Azure AD Application (Client) ID:"
            : $"Enter your Azure AD Application (Client) ID [grey](current: {MaskString(currentClientId)})[/]:";
        
        config.Skills.Teams.ClientId = _ui.PromptText(
            clientIdPrompt,
            currentClientId);

        // Tenant ID - default to "common" for multi-tenant
        _ui.DisplayInfo("For multi-tenant support (home + guest tenants), use 'common' as Tenant ID.");
        config.Skills.Teams.TenantId = _ui.PromptText(
            "Enter your Tenant ID (use 'common' for multi-tenant access):",
            config.Skills.Teams.TenantId ?? "common");

        // Authentication method
        var authMethod = _ui.SelectOption(
            "Select authentication method:",
            new[] { "DeviceCode", "InteractiveBrowser" });
        config.Skills.Teams.AuthenticationMethod = authMethod;

        if (authMethod == "InteractiveBrowser")
        {
            config.Skills.Teams.RedirectUri = _ui.PromptText(
                "Enter Redirect URI:",
                config.Skills.Teams.RedirectUri ?? "http://localhost");
        }

        AnsiConsole.WriteLine();
        _ui.DisplaySuccess("Teams skill configured!");
        _ui.DisplayInfo("Note: You will be prompted to authenticate when the skill is first used.");
        _ui.DisplayInfo("The skill will automatically access teams from all tenants (home + guest).");
        AnsiConsole.WriteLine();

        return true;
    }

    /// <summary>
    /// Configures the Slack skill.
    /// </summary>
    /// <param name="config">The configuration to update.</param>
    /// <returns>True if configuration was successful, false if cancelled.</returns>
    private bool ConfigureSlackSkill(MicrobotConfig config)
    {
        AnsiConsole.Write(new Rule("[cyan]Slack Skill Configuration[/]").RuleStyle("grey"));
        AnsiConsole.WriteLine();

        // Show current configuration if exists
        if (config.Skills.Slack?.Enabled == true || !string.IsNullOrEmpty(config.Skills.Slack?.BotToken))
        {
            DisplayCurrentSlackConfig(config.Skills.Slack);

            if (!AnsiConsole.Confirm("[yellow]Do you want to reconfigure?[/]", false))
            {
                return false;
            }
            AnsiConsole.WriteLine();
        }

        // Ensure Slack config exists
        config.Skills.Slack ??= new SlackSkillConfig();

        // Enable/Disable
        var enable = AnsiConsole.Confirm(
            "[cyan]Enable Slack skill?[/] (requires Slack Bot Token)",
            config.Skills.Slack.Enabled);

        if (!enable)
        {
            config.Skills.Slack.Enabled = false;
            _ui.DisplaySuccess("Slack skill disabled.");
            return true;
        }

        config.Skills.Slack.Enabled = true;

        // Mode selection
        var mode = _ui.SelectOption(
            "Select Slack skill [green]permission mode[/]:",
            new[] { "ReadOnly", "Full" });
        config.Skills.Slack.Mode = mode;

        // Display mode description
        var modeDescription = mode switch
        {
            "ReadOnly" => "Read channels, direct messages, and message history only",
            "Full" => "Read channels/DMs and send messages",
            _ => ""
        };
        _ui.DisplayInfo($"Mode: {modeDescription}");
        AnsiConsole.WriteLine();

        // Show Slack Bot setup instructions
        DisplaySlackBotSetupInstructions(mode);

        // Bot Token
        var currentBotToken = config.Skills.Slack.BotToken;
        var botTokenPrompt = string.IsNullOrEmpty(currentBotToken)
            ? "Enter your Slack Bot User OAuth Token (xoxb-...):"
            : $"Enter your Slack Bot User OAuth Token [grey](current: {MaskString(currentBotToken)})[/]:";
        
        config.Skills.Slack.BotToken = _ui.PromptText(
            botTokenPrompt,
            currentBotToken);

        AnsiConsole.WriteLine();
        _ui.DisplaySuccess("Slack skill configured!");
        _ui.DisplayInfo("Note: The bot token will be used immediately to connect to Slack.");
        AnsiConsole.WriteLine();

        return true;
    }

    /// <summary>
    /// Displays the current Slack configuration.
    /// </summary>
    private void DisplayCurrentSlackConfig(SlackSkillConfig slackConfig)
    {
        var status = slackConfig.Enabled ? "[green]Enabled[/]" : "[yellow]Disabled[/]";
        
        AnsiConsole.MarkupLine($"[yellow]Slack skill is currently configured:[/]");
        AnsiConsole.MarkupLine($"  Status: {status}");
        AnsiConsole.MarkupLine($"  Mode: [cyan]{Markup.Escape(slackConfig.Mode)}[/]");
        AnsiConsole.MarkupLine($"  Bot Token: [cyan]{MaskString(slackConfig.BotToken)}[/]");
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Displays Slack Bot setup instructions.
    /// </summary>
    private void DisplaySlackBotSetupInstructions(string mode)
    {
        var scopes = mode switch
        {
            "ReadOnly" => "channels:read, channels:history, groups:read, groups:history,\n   im:read, im:history, mpim:read, mpim:history, users:read",
            "Full" => "channels:read, channels:history, groups:read, groups:history,\n   im:read, im:history, mpim:read, mpim:history, users:read,\n   chat:write, chat:write.public",
            _ => ""
        };

        var panel = new Panel(
            new Markup(
                $"[bold]Slack App & Bot Token Required[/]\n\n" +
                $"1. Go to [link]https://api.slack.com/apps[/] and create a new app\n" +
                $"2. Select [cyan]From scratch[/] and choose your workspace\n" +
                $"3. Go to [cyan]OAuth & Permissions[/] in the sidebar\n" +
                $"4. Add the following [cyan]Bot Token Scopes[/]:\n" +
                $"   [green]{scopes}[/]\n" +
                $"5. Click [cyan]Install to Workspace[/] at the top of the page\n" +
                $"6. Copy the [cyan]Bot User OAuth Token[/] (starts with xoxb-)\n\n" +
                $"[yellow]Important:[/] Invite the bot to channels you want it to access\n" +
                $"using /invite @YourBotName in each channel."))
        {
            Header = new PanelHeader("[yellow]Setup Instructions[/]"),
            Border = BoxBorder.Rounded,
            Padding = new Padding(2, 1)
        };

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Displays the current Teams configuration.
    /// </summary>
    private void DisplayCurrentTeamsConfig(TeamsSkillConfig teamsConfig)
    {
        var status = teamsConfig.Enabled ? "[green]Enabled[/]" : "[yellow]Disabled[/]";
        
        AnsiConsole.MarkupLine($"[yellow]Teams skill is currently configured:[/]");
        AnsiConsole.MarkupLine($"  Status: {status}");
        AnsiConsole.MarkupLine($"  Mode: [cyan]{Markup.Escape(teamsConfig.Mode)}[/]");
        AnsiConsole.MarkupLine($"  Client ID: [cyan]{MaskString(teamsConfig.ClientId)}[/]");
        AnsiConsole.MarkupLine($"  Tenant ID: [cyan]{Markup.Escape(teamsConfig.TenantId)}[/]");
        AnsiConsole.MarkupLine($"  Auth Method: [cyan]{Markup.Escape(teamsConfig.AuthenticationMethod)}[/]");
        if (teamsConfig.AuthenticationMethod == "InteractiveBrowser")
        {
            AnsiConsole.MarkupLine($"  Redirect URI: [cyan]{Markup.Escape(teamsConfig.RedirectUri)}[/]");
        }
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Displays Azure AD setup instructions for Teams skill.
    /// </summary>
    private void DisplayTeamsAzureAdSetupInstructions(string mode)
    {
        var permissions = mode switch
        {
            "ReadOnly" => "Team.ReadBasic.All, Channel.ReadBasic.All, ChannelMessage.Read.All,\n   Chat.Read, ChatMessage.Read, User.Read",
            "Full" => "Team.ReadBasic.All, Channel.ReadBasic.All, ChannelMessage.Read.All,\n   ChannelMessage.Send, Chat.Read, ChatMessage.Read, ChatMessage.Send, User.Read",
            _ => ""
        };

        var panel = new Panel(
            new Markup(
                $"[bold]Azure AD App Registration Required (Multi-Tenant)[/]\n\n" +
                $"1. Go to [link]https://portal.azure.com[/] > Azure Active Directory > App registrations\n" +
                $"2. Create a new registration with [cyan]Accounts in any organizational directory[/]\n" +
                $"3. Enable [cyan]Allow public client flows[/] in Authentication settings\n" +
                $"4. Add the following [cyan]Delegated permissions[/] in API permissions:\n" +
                $"   [green]{permissions}[/]\n" +
                $"5. Copy the [cyan]Application (client) ID[/] from the Overview page\n\n" +
                $"[yellow]Important:[/] For multi-tenant access, the app must be registered as multi-tenant\n" +
                $"and use TenantId = 'common' to access teams from guest tenants."))
        {
            Header = new PanelHeader("[yellow]Setup Instructions[/]"),
            Border = BoxBorder.Rounded,
            Padding = new Padding(2, 1)
        };

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Displays the current Outlook configuration.
    /// </summary>
    private void DisplayCurrentOutlookConfig(OutlookSkillConfig outlookConfig)
    {
        var status = outlookConfig.Enabled ? "[green]Enabled[/]" : "[yellow]Disabled[/]";
        
        AnsiConsole.MarkupLine($"[yellow]Outlook skill is currently configured:[/]");
        AnsiConsole.MarkupLine($"  Status: {status}");
        AnsiConsole.MarkupLine($"  Mode: [cyan]{Markup.Escape(outlookConfig.Mode)}[/]");
        AnsiConsole.MarkupLine($"  Client ID: [cyan]{MaskString(outlookConfig.ClientId)}[/]");
        AnsiConsole.MarkupLine($"  Tenant ID: [cyan]{Markup.Escape(outlookConfig.TenantId)}[/]");
        AnsiConsole.MarkupLine($"  Auth Method: [cyan]{Markup.Escape(outlookConfig.AuthenticationMethod)}[/]");
        if (outlookConfig.AuthenticationMethod == "InteractiveBrowser")
        {
            AnsiConsole.MarkupLine($"  Redirect URI: [cyan]{Markup.Escape(outlookConfig.RedirectUri)}[/]");
        }
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Displays Azure AD setup instructions.
    /// </summary>
    private void DisplayAzureAdSetupInstructions(string mode)
    {
        var permissions = mode switch
        {
            "ReadOnly" => "Mail.Read, Calendars.Read, User.Read",
            "ReadWriteCalendar" => "Mail.Read, Calendars.Read, Calendars.ReadWrite, User.Read",
            "Full" => "Mail.Read, Mail.Send, Calendars.Read, Calendars.ReadWrite, User.Read",
            _ => ""
        };

        var panel = new Panel(
            new Markup(
                $"[bold]Azure AD App Registration Required[/]\n\n" +
                $"1. Go to [link]https://portal.azure.com[/] > Azure Active Directory > App registrations\n" +
                $"2. Create a new registration or use an existing one\n" +
                $"3. Enable [cyan]Allow public client flows[/] in Authentication settings\n" +
                $"4. Add the following [cyan]Delegated permissions[/] in API permissions:\n" +
                $"   [green]{permissions}[/]\n" +
                $"5. Copy the [cyan]Application (client) ID[/] from the Overview page"))
        {
            Header = new PanelHeader("[yellow]Setup Instructions[/]"),
            Border = BoxBorder.Rounded,
            Padding = new Padding(2, 1)
        };

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Configures the YouTrack skill.
    /// </summary>
    /// <param name="config">The configuration to update.</param>
    /// <returns>True if configuration was successful, false if cancelled.</returns>
    private bool ConfigureYouTrackSkill(MicrobotConfig config)
    {
        AnsiConsole.Write(new Rule("[cyan]YouTrack Skill Configuration[/]").RuleStyle("grey"));
        AnsiConsole.WriteLine();

        // Show current configuration if exists
        if (config.Skills.YouTrack?.Enabled == true || !string.IsNullOrEmpty(config.Skills.YouTrack?.BaseUrl))
        {
            DisplayCurrentYouTrackConfig(config.Skills.YouTrack);

            if (!AnsiConsole.Confirm("[yellow]Do you want to reconfigure?[/]", false))
            {
                return false;
            }
            AnsiConsole.WriteLine();
        }

        // Ensure YouTrack config exists
        config.Skills.YouTrack ??= new YouTrackSkillConfig();

        // Enable/Disable
        var enable = AnsiConsole.Confirm(
            "[cyan]Enable YouTrack skill?[/] (requires YouTrack permanent token)",
            config.Skills.YouTrack.Enabled);

        if (!enable)
        {
            config.Skills.YouTrack.Enabled = false;
            _ui.DisplaySuccess("YouTrack skill disabled.");
            return true;
        }

        config.Skills.YouTrack.Enabled = true;

        // Mode selection
        var mode = _ui.SelectOption(
            "Select YouTrack skill [green]permission mode[/]:",
            new[] { "ReadOnly", "FullControl" });
        config.Skills.YouTrack.Mode = mode;

        // Display mode description
        var modeDescription = mode switch
        {
            "ReadOnly" => "Read projects, issues, and comments only",
            "FullControl" => "Read/create/update issues and comments, execute commands",
            _ => ""
        };
        _ui.DisplayInfo($"Mode: {modeDescription}");
        AnsiConsole.WriteLine();

        // Show YouTrack setup instructions
        DisplayYouTrackSetupInstructions(mode);

        // Base URL
        var currentBaseUrl = config.Skills.YouTrack.BaseUrl;
        var baseUrlPrompt = string.IsNullOrEmpty(currentBaseUrl)
            ? "Enter your YouTrack Base URL (e.g., https://youtrack.example.com):"
            : $"Enter your YouTrack Base URL [grey](current: {currentBaseUrl})[/]:";
        
        config.Skills.YouTrack.BaseUrl = _ui.PromptText(
            baseUrlPrompt,
            currentBaseUrl);

        // Permanent Token
        var currentToken = config.Skills.YouTrack.PermanentToken;
        var tokenPrompt = string.IsNullOrEmpty(currentToken)
            ? "Enter your YouTrack Permanent Token (perm:...):"
            : $"Enter your YouTrack Permanent Token [grey](current: {MaskString(currentToken)})[/]:";
        
        config.Skills.YouTrack.PermanentToken = _ui.PromptText(
            tokenPrompt,
            currentToken);

        AnsiConsole.WriteLine();
        _ui.DisplaySuccess("YouTrack skill configured!");
        _ui.DisplayInfo("Note: The permanent token will be used immediately to connect to YouTrack.");
        AnsiConsole.WriteLine();

        return true;
    }

    /// <summary>
    /// Displays the current YouTrack configuration.
    /// </summary>
    private void DisplayCurrentYouTrackConfig(YouTrackSkillConfig youTrackConfig)
    {
        var status = youTrackConfig.Enabled ? "[green]Enabled[/]" : "[yellow]Disabled[/]";
        
        AnsiConsole.MarkupLine($"[yellow]YouTrack skill is currently configured:[/]");
        AnsiConsole.MarkupLine($"  Status: {status}");
        AnsiConsole.MarkupLine($"  Mode: [cyan]{Markup.Escape(youTrackConfig.Mode)}[/]");
        AnsiConsole.MarkupLine($"  Base URL: [cyan]{Markup.Escape(youTrackConfig.BaseUrl ?? "(not set)")}[/]");
        AnsiConsole.MarkupLine($"  Permanent Token: [cyan]{MaskString(youTrackConfig.PermanentToken)}[/]");
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Displays YouTrack setup instructions.
    /// </summary>
    private void DisplayYouTrackSetupInstructions(string mode)
    {
        var permissions = mode switch
        {
            "ReadOnly" => "Read Service (for projects, issues, comments)",
            "FullControl" => "Read Service, Update Issue, Create Issue, Apply Command",
            _ => ""
        };

        var panel = new Panel(
            new Markup(
                $"[bold]YouTrack Permanent Token Required[/]\n\n" +
                $"1. Log in to your YouTrack instance\n" +
                $"2. Go to [cyan]Profile[/] > [cyan]Account Security[/] > [cyan]Tokens[/]\n" +
                $"3. Click [cyan]New token...[/]\n" +
                $"4. Give it a name (e.g., 'Microbot')\n" +
                $"5. Select the required scopes:\n" +
                $"   [green]{permissions}[/]\n" +
                $"6. Click [cyan]Create[/] and copy the token (starts with perm:)\n\n" +
                $"[yellow]Important:[/] Store the token securely - it won't be shown again!"))
        {
            Header = new PanelHeader("[yellow]Setup Instructions[/]"),
            Border = BoxBorder.Rounded,
            Padding = new Padding(2, 1)
        };

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Handles an unknown skill ID.
    /// </summary>
    private bool HandleUnknownSkill(string skillId)
    {
        _ui.DisplayError($"Unknown skill: '{skillId}'");
        _ui.DisplayInfo("Use /skills avail to see available skills.");
        return false;
    }

    /// <summary>
    /// Masks a string for display (shows first 4 and last 4 characters).
    /// </summary>
    private static string MaskString(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "(not set)";
        if (value.Length <= 8) return "****";
        return value[..4] + "****" + value[^4..];
    }
}
