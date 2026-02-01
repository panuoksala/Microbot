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
