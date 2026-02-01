namespace Microbot.Console.Services;

using Spectre.Console;
using Microbot.Core.Models;
using Microbot.Skills;

/// <summary>
/// Service for handling console UI using Spectre.Console.
/// </summary>
public class ConsoleUIService
{
    private readonly Style _headerStyle = new(Color.Cyan1, decoration: Decoration.Bold);
    private readonly Style _successStyle = new(Color.Green);
    private readonly Style _errorStyle = new(Color.Red);
    private readonly Style _warningStyle = new(Color.Yellow);
    private readonly Style _infoStyle = new(Color.Blue);

    /// <summary>
    /// Displays the application header/banner.
    /// </summary>
    public void DisplayHeader()
    {
        AnsiConsole.Clear();
        
        var banner = new FigletText("Microbot")
            .LeftJustified()
            .Color(Color.Cyan1);
        
        AnsiConsole.Write(banner);
        
        AnsiConsole.Write(new Rule("[cyan]Your Personal AI Assistant[/]").RuleStyle("grey"));
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Displays a welcome message with system information.
    /// </summary>
    /// <param name="config">The current configuration.</param>
    public void DisplayWelcome(MicrobotConfig config)
    {
        var panel = new Panel(
            new Markup($"[bold]Welcome to Microbot![/]\n\n" +
                      $"[grey]AI Provider:[/] [cyan]{Markup.Escape(config.AiProvider.Provider)}[/]\n" +
                      $"[grey]Model:[/] [cyan]{Markup.Escape(config.AiProvider.ModelId)}[/]\n" +
                      $"[grey]MCP Servers:[/] [cyan]{config.Skills.McpServers.Count}[/]\n" +
                      $"[grey]NuGet Skills:[/] [cyan]{config.Skills.NuGetSkills.Count}[/]"))
        {
            Header = new PanelHeader("[cyan]System Info[/]"),
            Border = BoxBorder.Rounded,
            Padding = new Padding(2, 1)
        };
        
        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Displays the first-time setup notice.
    /// </summary>
    public void DisplayFirstTimeSetup()
    {
        var panel = new Panel(
            new Markup("[yellow]No configuration file found.[/]\n\n" +
                      "A new [cyan]Microbot.config[/] file will be created.\n" +
                      "Please complete the initial setup to get started."))
        {
            Header = new PanelHeader("[yellow]First Time Setup[/]"),
            Border = BoxBorder.Double,
            Padding = new Padding(2, 1)
        };
        
        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Displays the loaded skills summary.
    /// </summary>
    /// <param name="summaries">The skill summaries to display.</param>
    public void DisplaySkillsSummary(IEnumerable<SkillSummary> summaries)
    {
        var skillList = summaries.ToList();
        
        if (skillList.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No skills loaded.[/]");
            AnsiConsole.WriteLine();
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .AddColumn(new TableColumn("[cyan]Skill Name[/]").LeftAligned())
            .AddColumn(new TableColumn("[cyan]Functions[/]").Centered())
            .AddColumn(new TableColumn("[cyan]Description[/]").LeftAligned());

        foreach (var skill in skillList)
        {
            table.AddRow(
                $"[white]{skill.Name}[/]",
                $"[green]{skill.FunctionCount}[/]",
                $"[grey]{(string.IsNullOrEmpty(skill.Description) ? "-" : skill.Description)}[/]"
            );
        }

        AnsiConsole.Write(new Panel(table)
        {
            Header = new PanelHeader($"[cyan]Loaded Skills ({skillList.Count})[/]"),
            Border = BoxBorder.Rounded,
            Padding = new Padding(1, 0)
        });
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Displays a loading spinner while executing an async operation.
    /// </summary>
    /// <typeparam name="T">The return type of the operation.</typeparam>
    /// <param name="message">The message to display during loading.</param>
    /// <param name="operation">The async operation to execute.</param>
    /// <returns>The result of the operation.</returns>
    public async Task<T> WithSpinnerAsync<T>(string message, Func<Task<T>> operation)
    {
        return await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("cyan"))
            .StartAsync(message, async ctx => await operation());
    }

    /// <summary>
    /// Displays a loading spinner while executing an async operation.
    /// </summary>
    /// <param name="message">The message to display during loading.</param>
    /// <param name="operation">The async operation to execute.</param>
    public async Task WithSpinnerAsync(string message, Func<Task> operation)
    {
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("cyan"))
            .StartAsync(message, async ctx => await operation());
    }

    /// <summary>
    /// Gets user input with a prompt.
    /// </summary>
    /// <param name="prompt">The prompt to display.</param>
    /// <returns>The user's input.</returns>
    public string GetUserInput(string prompt = "You")
    {
        AnsiConsole.Markup($"[green]{prompt}[/] [grey]>[/] ");
        return System.Console.ReadLine() ?? string.Empty;
    }

    /// <summary>
    /// Displays the AI's response.
    /// </summary>
    /// <param name="response">The response text.</param>
    /// <param name="agentName">The name of the agent.</param>
    public void DisplayAgentResponse(string response, string agentName = "Microbot")
    {
        var panel = new Panel(new Markup(Markup.Escape(response)))
        {
            Header = new PanelHeader($"[cyan]{agentName}[/]"),
            Border = BoxBorder.Rounded,
            Padding = new Padding(2, 1),
            BorderStyle = new Style(Color.Cyan1)
        };
        
        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Displays a streaming response character by character.
    /// </summary>
    /// <param name="responseStream">The async enumerable of response chunks.</param>
    /// <param name="agentName">The name of the agent.</param>
    public async Task DisplayStreamingResponseAsync(
        IAsyncEnumerable<string> responseStream, 
        string agentName = "Microbot")
    {
        AnsiConsole.MarkupLine($"[cyan]{agentName}[/] [grey]>[/]");
        
        await foreach (var chunk in responseStream)
        {
            AnsiConsole.Write(chunk);
        }
        
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Displays a success message.
    /// </summary>
    /// <param name="message">The message to display.</param>
    public void DisplaySuccess(string message)
    {
        AnsiConsole.MarkupLine($"[green]✓[/] {Markup.Escape(message)}");
    }

    /// <summary>
    /// Displays an error message.
    /// </summary>
    /// <param name="message">The message to display.</param>
    public void DisplayError(string message)
    {
        AnsiConsole.MarkupLine($"[red]✗[/] {Markup.Escape(message)}");
    }

    /// <summary>
    /// Displays a warning message.
    /// </summary>
    /// <param name="message">The message to display.</param>
    public void DisplayWarning(string message)
    {
        AnsiConsole.MarkupLine($"[yellow]![/] {Markup.Escape(message)}");
    }

    /// <summary>
    /// Displays an info message.
    /// </summary>
    /// <param name="message">The message to display.</param>
    public void DisplayInfo(string message)
    {
        AnsiConsole.MarkupLine($"[blue]ℹ[/] {Markup.Escape(message)}");
    }

    /// <summary>
    /// Displays a horizontal rule/separator.
    /// </summary>
    /// <param name="title">Optional title for the rule.</param>
    public void DisplayRule(string? title = null)
    {
        if (string.IsNullOrEmpty(title))
        {
            AnsiConsole.Write(new Rule().RuleStyle("grey"));
        }
        else
        {
            AnsiConsole.Write(new Rule($"[grey]{title}[/]").RuleStyle("grey"));
        }
    }

    /// <summary>
    /// Displays the help/commands panel.
    /// </summary>
    public void DisplayHelp()
    {
        var table = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn("Command")
            .AddColumn("Description");

        table.AddRow("[cyan]/help[/]", "Show this help message");
        table.AddRow("[cyan]/skills[/]", "List loaded skills");
        table.AddRow("[cyan]/clear[/]", "Clear the screen");
        table.AddRow("[cyan]/config[/]", "Show current configuration");
        table.AddRow("[cyan]/exit[/]", "Exit the application");

        var panel = new Panel(table)
        {
            Header = new PanelHeader("[cyan]Available Commands[/]"),
            Border = BoxBorder.Rounded,
            Padding = new Padding(2, 1)
        };

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Asks a yes/no confirmation question.
    /// </summary>
    /// <param name="question">The question to ask.</param>
    /// <returns>True if the user confirms, false otherwise.</returns>
    public bool Confirm(string question)
    {
        return AnsiConsole.Confirm(question);
    }

    /// <summary>
    /// Prompts the user to select from a list of options.
    /// </summary>
    /// <typeparam name="T">The type of the options.</typeparam>
    /// <param name="title">The prompt title.</param>
    /// <param name="choices">The available choices.</param>
    /// <returns>The selected choice.</returns>
    public T SelectOption<T>(string title, IEnumerable<T> choices) where T : notnull
    {
        return AnsiConsole.Prompt(
            new SelectionPrompt<T>()
                .Title(title)
                .PageSize(10)
                .AddChoices(choices));
    }

    /// <summary>
    /// Prompts the user for text input with validation.
    /// </summary>
    /// <param name="prompt">The prompt text.</param>
    /// <param name="defaultValue">Optional default value.</param>
    /// <param name="allowEmpty">Whether to allow empty input.</param>
    /// <returns>The user's input.</returns>
    public string PromptText(string prompt, string? defaultValue = null, bool allowEmpty = false)
    {
        var textPrompt = new TextPrompt<string>(prompt);
        
        if (defaultValue != null)
        {
            textPrompt.DefaultValue(defaultValue);
        }
        
        if (allowEmpty)
        {
            textPrompt.AllowEmpty();
        }

        return AnsiConsole.Prompt(textPrompt);
    }

    /// <summary>
    /// Prompts the user for a secret (password) input.
    /// </summary>
    /// <param name="prompt">The prompt text.</param>
    /// <returns>The user's secret input.</returns>
    public string PromptSecret(string prompt)
    {
        return AnsiConsole.Prompt(
            new TextPrompt<string>(prompt)
                .Secret());
    }

    /// <summary>
    /// Displays a progress bar for multiple tasks.
    /// </summary>
    /// <param name="tasks">The tasks to execute with progress.</param>
    public async Task WithProgressAsync(params (string Description, Func<Task> Action)[] tasks)
    {
        await AnsiConsole.Progress()
            .AutoClear(false)
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new SpinnerColumn())
            .StartAsync(async ctx =>
            {
                foreach (var (description, action) in tasks)
                {
                    var task = ctx.AddTask(description);
                    await action();
                    task.Increment(100);
                }
            });
    }

    /// <summary>
    /// Displays the goodbye message.
    /// </summary>
    public void DisplayGoodbye()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[cyan]Goodbye![/]").RuleStyle("grey"));
        AnsiConsole.MarkupLine("[grey]Thank you for using Microbot.[/]");
    }
}
