namespace Microbot.Skills.YouTrack;

using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microbot.Core.Models;
using Microbot.Skills.YouTrack.Models;
using Microbot.Skills.YouTrack.Services;

/// <summary>
/// YouTrack skill for Microbot.
/// Provides functions to interact with JetBrains YouTrack issue tracker.
/// Supports ReadOnly and FullControl modes.
/// </summary>
public class YouTrackSkill : IDisposable
{
    private readonly YouTrackSkillConfig _config;
    private readonly YouTrackApiClient _apiClient;
    private readonly ILogger<YouTrackSkill>? _logger;
    private YouTrackUser? _currentUser;
    private bool _disposed;

    /// <summary>
    /// Creates a new YouTrackSkill instance.
    /// </summary>
    public YouTrackSkill(
        YouTrackSkillConfig config,
        ILogger<YouTrackSkill>? logger = null)
    {
        _config = config;
        _logger = logger;
        _apiClient = new YouTrackApiClient(config, logger as ILogger<YouTrackApiClient>);
    }

    /// <summary>
    /// Creates a new YouTrackSkill instance with a logger factory.
    /// </summary>
    public YouTrackSkill(
        YouTrackSkillConfig config,
        ILoggerFactory? loggerFactory)
    {
        _config = config;
        _logger = loggerFactory?.CreateLogger<YouTrackSkill>();
        _apiClient = new YouTrackApiClient(config, loggerFactory?.CreateLogger<YouTrackApiClient>());
    }

    /// <summary>
    /// Initializes the skill by verifying authentication.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _currentUser = await _apiClient.GetCurrentUserAsync(cancellationToken);
        _logger?.LogInformation("YouTrack skill initialized. Authenticated as: {User}", _currentUser?.Login);
    }

    private bool CanWrite => _config.Mode.Equals("FullControl", StringComparison.OrdinalIgnoreCase);

    #region Project Functions

    /// <summary>
    /// Lists all accessible YouTrack projects.
    /// </summary>
    [KernelFunction("list_projects")]
    [Description("Lists all accessible YouTrack projects.")]
    public async Task<string> ListProjectsAsync(
        [Description("Number of projects to skip (default: 0)")] int skip = 0,
        [Description("Maximum number of projects to return (default: 50)")] int top = 50,
        CancellationToken cancellationToken = default)
    {
        var projects = await _apiClient.ListProjectsAsync(skip, top, cancellationToken);

        if (projects.Count == 0)
        {
            return "No projects found.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Found {projects.Count} project(s):");
        sb.AppendLine();

        foreach (var project in projects)
        {
            var archivedMarker = project.Archived ? " [Archived]" : "";
            sb.AppendLine($"**{project.ShortName}** - {project.Name}{archivedMarker}");
            sb.AppendLine($"  - ID: {project.Id}");
            if (!string.IsNullOrEmpty(project.Description))
                sb.AppendLine($"  - Description: {project.Description}");
            if (!string.IsNullOrEmpty(project.Leader))
                sb.AppendLine($"  - Leader: {project.Leader}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Gets detailed information about a specific project.
    /// </summary>
    [KernelFunction("get_project")]
    [Description("Gets detailed information about a specific YouTrack project.")]
    public async Task<string> GetProjectAsync(
        [Description("The project ID or short name (e.g., 'PROJ')")] string projectId,
        CancellationToken cancellationToken = default)
    {
        var project = await _apiClient.GetProjectAsync(projectId, cancellationToken);

        if (project == null)
        {
            return $"Project not found: {projectId}";
        }

        var sb = new StringBuilder();
        var archivedMarker = project.Archived ? " [Archived]" : "";
        sb.AppendLine($"**{project.ShortName}** - {project.Name}{archivedMarker}");
        sb.AppendLine($"- ID: {project.Id}");
        if (!string.IsNullOrEmpty(project.Description))
            sb.AppendLine($"- Description: {project.Description}");
        if (!string.IsNullOrEmpty(project.Leader))
            sb.AppendLine($"- Leader: {project.Leader}");
        if (project.CreatedDate.HasValue)
            sb.AppendLine($"- Created: {project.CreatedDate:g}");

        return sb.ToString();
    }

    #endregion

    #region Issue Functions

    /// <summary>
    /// Gets a specific issue by ID.
    /// </summary>
    [KernelFunction("get_issue")]
    [Description("Gets detailed information about a specific YouTrack issue.")]
    public async Task<string> GetIssueAsync(
        [Description("The issue ID (e.g., 'PROJ-123')")] string issueId,
        CancellationToken cancellationToken = default)
    {
        var issue = await _apiClient.GetIssueAsync(issueId, cancellationToken);

        if (issue == null)
        {
            return $"Issue not found: {issueId}";
        }

        return FormatIssue(issue, includeDescription: true);
    }

    /// <summary>
    /// Searches for issues using YouTrack query syntax.
    /// </summary>
    [KernelFunction("search_issues")]
    [Description("Searches for YouTrack issues using query syntax. Examples: 'project: PROJ', 'state: Open', 'assignee: me', '#bug', 'created: today'.")]
    public async Task<string> SearchIssuesAsync(
        [Description("YouTrack search query (e.g., 'project: PROJ state: Open')")] string query,
        [Description("Number of issues to skip (default: 0)")] int skip = 0,
        [Description("Maximum number of issues to return (default: 20)")] int top = 20,
        CancellationToken cancellationToken = default)
    {
        var issues = await _apiClient.SearchIssuesAsync(query, skip, top, cancellationToken);

        if (issues.Count == 0)
        {
            return $"No issues found matching query: {query}";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Found {issues.Count} issue(s) matching '{query}':");
        sb.AppendLine();

        foreach (var issue in issues)
        {
            sb.AppendLine(FormatIssue(issue, includeDescription: false));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Lists issues in a specific project.
    /// </summary>
    [KernelFunction("list_project_issues")]
    [Description("Lists issues in a specific YouTrack project.")]
    public async Task<string> ListProjectIssuesAsync(
        [Description("The project ID or short name (e.g., 'PROJ')")] string projectId,
        [Description("Number of issues to skip (default: 0)")] int skip = 0,
        [Description("Maximum number of issues to return (default: 20)")] int top = 20,
        CancellationToken cancellationToken = default)
    {
        var issues = await _apiClient.ListProjectIssuesAsync(projectId, skip, top, cancellationToken);

        if (issues.Count == 0)
        {
            return $"No issues found in project: {projectId}";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Found {issues.Count} issue(s) in project {projectId}:");
        sb.AppendLine();

        foreach (var issue in issues)
        {
            sb.AppendLine(FormatIssue(issue, includeDescription: false));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Creates a new issue in a project.
    /// </summary>
    [KernelFunction("create_issue")]
    [Description("Creates a new issue in a YouTrack project. Requires FullControl mode.")]
    public async Task<string> CreateIssueAsync(
        [Description("The project ID or short name (e.g., 'PROJ')")] string projectId,
        [Description("The issue summary/title")] string summary,
        [Description("The issue description (optional, supports markdown)")] string? description = null,
        CancellationToken cancellationToken = default)
    {
        if (!CanWrite)
        {
            return "Error: Creating issues requires FullControl mode. Current mode is ReadOnly.";
        }

        var issue = await _apiClient.CreateIssueAsync(projectId, summary, description, cancellationToken);

        return $"Issue created successfully: **{issue.Id}** - {issue.Summary}";
    }

    /// <summary>
    /// Updates an existing issue.
    /// </summary>
    [KernelFunction("update_issue")]
    [Description("Updates an existing YouTrack issue's summary or description. Requires FullControl mode.")]
    public async Task<string> UpdateIssueAsync(
        [Description("The issue ID (e.g., 'PROJ-123')")] string issueId,
        [Description("New summary/title (optional)")] string? summary = null,
        [Description("New description (optional, supports markdown)")] string? description = null,
        CancellationToken cancellationToken = default)
    {
        if (!CanWrite)
        {
            return "Error: Updating issues requires FullControl mode. Current mode is ReadOnly.";
        }

        if (summary == null && description == null)
        {
            return "Error: At least one of summary or description must be provided.";
        }

        var issue = await _apiClient.UpdateIssueAsync(issueId, summary, description, cancellationToken);

        return $"Issue updated successfully: **{issue.Id}** - {issue.Summary}";
    }

    /// <summary>
    /// Applies a command to an issue (change state, assignee, etc.).
    /// </summary>
    [KernelFunction("apply_command")]
    [Description("Applies a command to a YouTrack issue (e.g., change state, assignee, priority). Requires FullControl mode. Examples: 'State In Progress', 'Assignee john', 'Priority Critical', 'Type Bug'.")]
    public async Task<string> ApplyCommandAsync(
        [Description("The issue ID (e.g., 'PROJ-123')")] string issueId,
        [Description("The command to apply (e.g., 'State In Progress', 'Assignee john')")] string command,
        [Description("Optional comment to add with the command")] string? comment = null,
        CancellationToken cancellationToken = default)
    {
        if (!CanWrite)
        {
            return "Error: Applying commands requires FullControl mode. Current mode is ReadOnly.";
        }

        await _apiClient.ApplyCommandAsync(issueId, command, comment, cancellationToken);

        return $"Command applied successfully to {issueId}: {command}";
    }

    #endregion

    #region Comment Functions

    /// <summary>
    /// Lists comments on an issue.
    /// </summary>
    [KernelFunction("list_comments")]
    [Description("Lists comments on a YouTrack issue.")]
    public async Task<string> ListCommentsAsync(
        [Description("The issue ID (e.g., 'PROJ-123')")] string issueId,
        [Description("Number of comments to skip (default: 0)")] int skip = 0,
        [Description("Maximum number of comments to return (default: 50)")] int top = 50,
        CancellationToken cancellationToken = default)
    {
        var comments = await _apiClient.ListCommentsAsync(issueId, skip, top, cancellationToken);

        if (comments.Count == 0)
        {
            return $"No comments found on issue: {issueId}";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Found {comments.Count} comment(s) on {issueId}:");
        sb.AppendLine();

        foreach (var comment in comments.Where(c => !c.Deleted))
        {
            sb.AppendLine(FormatComment(comment));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Adds a comment to an issue.
    /// </summary>
    [KernelFunction("add_comment")]
    [Description("Adds a comment to a YouTrack issue. Requires FullControl mode.")]
    public async Task<string> AddCommentAsync(
        [Description("The issue ID (e.g., 'PROJ-123')")] string issueId,
        [Description("The comment text (supports markdown)")] string text,
        CancellationToken cancellationToken = default)
    {
        if (!CanWrite)
        {
            return "Error: Adding comments requires FullControl mode. Current mode is ReadOnly.";
        }

        var comment = await _apiClient.AddCommentAsync(issueId, text, cancellationToken);

        return $"Comment added successfully to {issueId} (ID: {comment.Id})";
    }

    /// <summary>
    /// Updates an existing comment.
    /// </summary>
    [KernelFunction("update_comment")]
    [Description("Updates an existing comment on a YouTrack issue. Requires FullControl mode.")]
    public async Task<string> UpdateCommentAsync(
        [Description("The issue ID (e.g., 'PROJ-123')")] string issueId,
        [Description("The comment ID to update")] string commentId,
        [Description("The new comment text (supports markdown)")] string text,
        CancellationToken cancellationToken = default)
    {
        if (!CanWrite)
        {
            return "Error: Updating comments requires FullControl mode. Current mode is ReadOnly.";
        }

        var comment = await _apiClient.UpdateCommentAsync(issueId, commentId, text, cancellationToken);

        return $"Comment updated successfully on {issueId} (ID: {comment.Id})";
    }

    #endregion

    #region User Functions

    /// <summary>
    /// Gets information about the current authenticated user.
    /// </summary>
    [KernelFunction("get_current_user")]
    [Description("Gets information about the currently authenticated YouTrack user.")]
    public async Task<string> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        var user = _currentUser ?? await _apiClient.GetCurrentUserAsync(cancellationToken);

        if (user == null)
        {
            return "Unable to retrieve current user information.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"**{user.FullName ?? user.Login}**");
        sb.AppendLine($"- Login: {user.Login}");
        if (!string.IsNullOrEmpty(user.Email))
            sb.AppendLine($"- Email: {user.Email}");
        if (user.Banned)
            sb.AppendLine("- Status: Banned");
        if (user.Guest)
            sb.AppendLine("- Type: Guest");

        return sb.ToString();
    }

    #endregion

    #region Formatting Helpers

    private static string FormatIssue(YouTrackIssue issue, bool includeDescription)
    {
        var sb = new StringBuilder();

        var stateMarker = issue.State switch
        {
            "Open" => "🔵",
            "In Progress" => "🟡",
            "Done" or "Resolved" or "Fixed" => "✅",
            "Won't fix" or "Duplicate" or "Incomplete" => "⚪",
            _ => "⚪"
        };

        var priorityMarker = issue.Priority switch
        {
            "Critical" or "Show-stopper" => "🔴",
            "Major" => "🟠",
            "Normal" => "🟢",
            "Minor" => "🔵",
            _ => ""
        };

        sb.AppendLine($"{stateMarker} {priorityMarker} **{issue.Id}**: {issue.Summary}");
        sb.AppendLine($"  - State: {issue.State ?? "Unknown"}");
        if (!string.IsNullOrEmpty(issue.Type))
            sb.AppendLine($"  - Type: {issue.Type}");
        if (!string.IsNullOrEmpty(issue.Priority))
            sb.AppendLine($"  - Priority: {issue.Priority}");
        if (!string.IsNullOrEmpty(issue.Assignee))
            sb.AppendLine($"  - Assignee: {issue.Assignee}");
        if (!string.IsNullOrEmpty(issue.Reporter))
            sb.AppendLine($"  - Reporter: {issue.Reporter}");
        if (issue.Created.HasValue)
            sb.AppendLine($"  - Created: {issue.Created:g}");
        if (issue.Updated.HasValue)
            sb.AppendLine($"  - Updated: {issue.Updated:g}");
        if (issue.Tags.Count > 0)
            sb.AppendLine($"  - Tags: {string.Join(", ", issue.Tags)}");
        if (issue.CommentsCount > 0)
            sb.AppendLine($"  - Comments: {issue.CommentsCount}");

        if (includeDescription && !string.IsNullOrEmpty(issue.Description))
        {
            sb.AppendLine();
            sb.AppendLine("**Description:**");
            sb.AppendLine(issue.Description);
        }

        sb.AppendLine();
        return sb.ToString();
    }

    private static string FormatComment(YouTrackComment comment)
    {
        var sb = new StringBuilder();
        var author = comment.AuthorFullName ?? comment.Author ?? "Unknown";
        var date = comment.Created?.ToString("g") ?? "Unknown date";

        sb.AppendLine($"**{author}** ({date}):");
        sb.AppendLine(comment.Text);
        sb.AppendLine($"[Comment ID: {comment.Id}]");
        sb.AppendLine();

        return sb.ToString();
    }

    #endregion

    public void Dispose()
    {
        if (!_disposed)
        {
            _apiClient.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
