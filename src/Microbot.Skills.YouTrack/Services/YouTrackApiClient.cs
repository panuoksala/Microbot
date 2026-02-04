namespace Microbot.Skills.YouTrack.Services;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microbot.Core.Models;
using Microbot.Skills.YouTrack.Models;

/// <summary>
/// HTTP client for interacting with the YouTrack REST API.
/// Uses permanent token authentication.
/// </summary>
public class YouTrackApiClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly YouTrackSkillConfig _config;
    private readonly ILogger<YouTrackApiClient>? _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _disposed;

    /// <summary>
    /// Creates a new YouTrackApiClient instance.
    /// </summary>
    public YouTrackApiClient(
        YouTrackSkillConfig config,
        ILogger<YouTrackApiClient>? logger = null,
        HttpClient? httpClient = null)
    {
        _config = config;
        _logger = logger;

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        _httpClient = httpClient ?? new HttpClient();
        ConfigureHttpClient();
    }

    private void ConfigureHttpClient()
    {
        if (string.IsNullOrEmpty(_config.BaseUrl))
        {
            throw new InvalidOperationException("YouTrack BaseUrl is not configured.");
        }

        if (string.IsNullOrEmpty(_config.PermanentToken))
        {
            throw new InvalidOperationException("YouTrack PermanentToken is not configured.");
        }

        var baseUrl = _config.BaseUrl.TrimEnd('/');
        _httpClient.BaseAddress = new Uri($"{baseUrl}/api/");
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _config.PermanentToken);
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>
    /// Gets the current user information.
    /// </summary>
    public async Task<YouTrackUser?> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                "users/me?fields=id,login,fullName,email,banned,guest,avatarUrl",
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            return MapUser(json);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to get current user");
            throw;
        }
    }

    #region Projects

    /// <summary>
    /// Lists all accessible projects.
    /// </summary>
    public async Task<List<YouTrackProject>> ListProjectsAsync(
        int skip = 0,
        int top = 100,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"admin/projects?fields=id,shortName,name,description,archived,leader(login),createdDate&$skip={skip}&$top={top}",
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            var projects = new List<YouTrackProject>();

            if (json.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in json.EnumerateArray())
                {
                    projects.Add(MapProject(item));
                }
            }

            return projects;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to list projects");
            throw;
        }
    }

    /// <summary>
    /// Gets a specific project by ID or short name.
    /// </summary>
    public async Task<YouTrackProject?> GetProjectAsync(
        string projectId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"admin/projects/{Uri.EscapeDataString(projectId)}?fields=id,shortName,name,description,archived,leader(login),createdDate",
                cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            return MapProject(json);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to get project {ProjectId}", projectId);
            throw;
        }
    }

    #endregion

    #region Issues

    /// <summary>
    /// Gets an issue by ID.
    /// </summary>
    public async Task<YouTrackIssue?> GetIssueAsync(
        string issueId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var fields = "id,idReadable,summary,description,created,updated,resolved,commentsCount," +
                        "project(id,shortName),reporter(login),customFields(name,value(name,login,text))," +
                        "tags(name)";

            var response = await _httpClient.GetAsync(
                $"issues/{Uri.EscapeDataString(issueId)}?fields={fields}",
                cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            return MapIssue(json);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to get issue {IssueId}", issueId);
            throw;
        }
    }

    /// <summary>
    /// Searches for issues using YouTrack query syntax.
    /// </summary>
    public async Task<List<YouTrackIssue>> SearchIssuesAsync(
        string query,
        int skip = 0,
        int top = 50,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var fields = "id,idReadable,summary,description,created,updated,resolved,commentsCount," +
                        "project(id,shortName),reporter(login),customFields(name,value(name,login,text))," +
                        "tags(name)";

            var encodedQuery = Uri.EscapeDataString(query);
            var response = await _httpClient.GetAsync(
                $"issues?query={encodedQuery}&fields={fields}&$skip={skip}&$top={top}",
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            var issues = new List<YouTrackIssue>();

            if (json.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in json.EnumerateArray())
                {
                    issues.Add(MapIssue(item));
                }
            }

            return issues;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to search issues with query: {Query}", query);
            throw;
        }
    }

    /// <summary>
    /// Lists issues in a project.
    /// </summary>
    public async Task<List<YouTrackIssue>> ListProjectIssuesAsync(
        string projectId,
        int skip = 0,
        int top = 50,
        CancellationToken cancellationToken = default)
    {
        return await SearchIssuesAsync($"project: {projectId}", skip, top, cancellationToken);
    }

    /// <summary>
    /// Creates a new issue.
    /// </summary>
    public async Task<YouTrackIssue> CreateIssueAsync(
        string projectId,
        string summary,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new
            {
                project = new { id = projectId },
                summary,
                description
            };

            var content = new StringContent(
                JsonSerializer.Serialize(payload, _jsonOptions),
                Encoding.UTF8,
                "application/json");

            var fields = "id,idReadable,summary,description,created,updated,resolved,commentsCount," +
                        "project(id,shortName),reporter(login),customFields(name,value(name,login,text))," +
                        "tags(name)";

            var response = await _httpClient.PostAsync(
                $"issues?fields={fields}",
                content,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            return MapIssue(json);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to create issue in project {ProjectId}", projectId);
            throw;
        }
    }

    /// <summary>
    /// Updates an existing issue.
    /// </summary>
    public async Task<YouTrackIssue> UpdateIssueAsync(
        string issueId,
        string? summary = null,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new Dictionary<string, object?>();
            if (summary != null) payload["summary"] = summary;
            if (description != null) payload["description"] = description;

            var content = new StringContent(
                JsonSerializer.Serialize(payload, _jsonOptions),
                Encoding.UTF8,
                "application/json");

            var fields = "id,idReadable,summary,description,created,updated,resolved,commentsCount," +
                        "project(id,shortName),reporter(login),customFields(name,value(name,login,text))," +
                        "tags(name)";

            var response = await _httpClient.PostAsync(
                $"issues/{Uri.EscapeDataString(issueId)}?fields={fields}",
                content,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            return MapIssue(json);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to update issue {IssueId}", issueId);
            throw;
        }
    }

    /// <summary>
    /// Applies a command to an issue (e.g., change state, assignee, etc.).
    /// </summary>
    public async Task ApplyCommandAsync(
        string issueId,
        string command,
        string? comment = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new
            {
                query = command,
                comment
            };

            var content = new StringContent(
                JsonSerializer.Serialize(payload, _jsonOptions),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync(
                $"issues/{Uri.EscapeDataString(issueId)}/commands",
                content,
                cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to apply command to issue {IssueId}: {Command}", issueId, command);
            throw;
        }
    }

    #endregion

    #region Comments

    /// <summary>
    /// Lists comments on an issue.
    /// </summary>
    public async Task<List<YouTrackComment>> ListCommentsAsync(
        string issueId,
        int skip = 0,
        int top = 100,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"issues/{Uri.EscapeDataString(issueId)}/comments?fields=id,text,author(login,fullName),created,updated,deleted&$skip={skip}&$top={top}",
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            var comments = new List<YouTrackComment>();

            if (json.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in json.EnumerateArray())
                {
                    var comment = MapComment(item);
                    comment.IssueId = issueId;
                    comments.Add(comment);
                }
            }

            return comments;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to list comments for issue {IssueId}", issueId);
            throw;
        }
    }

    /// <summary>
    /// Adds a comment to an issue.
    /// </summary>
    public async Task<YouTrackComment> AddCommentAsync(
        string issueId,
        string text,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new { text };

            var content = new StringContent(
                JsonSerializer.Serialize(payload, _jsonOptions),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync(
                $"issues/{Uri.EscapeDataString(issueId)}/comments?fields=id,text,author(login,fullName),created,updated,deleted",
                content,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            var comment = MapComment(json);
            comment.IssueId = issueId;
            return comment;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to add comment to issue {IssueId}", issueId);
            throw;
        }
    }

    /// <summary>
    /// Updates an existing comment.
    /// </summary>
    public async Task<YouTrackComment> UpdateCommentAsync(
        string issueId,
        string commentId,
        string text,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new { text };

            var content = new StringContent(
                JsonSerializer.Serialize(payload, _jsonOptions),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync(
                $"issues/{Uri.EscapeDataString(issueId)}/comments/{Uri.EscapeDataString(commentId)}?fields=id,text,author(login,fullName),created,updated,deleted",
                content,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            var comment = MapComment(json);
            comment.IssueId = issueId;
            return comment;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to update comment {CommentId} on issue {IssueId}", commentId, issueId);
            throw;
        }
    }

    #endregion

    #region Mapping Helpers

    private static YouTrackProject MapProject(JsonElement json)
    {
        return new YouTrackProject
        {
            Id = json.GetPropertyOrDefault("id", string.Empty),
            ShortName = json.GetPropertyOrDefault("shortName", string.Empty),
            Name = json.GetPropertyOrDefault("name", string.Empty),
            Description = json.GetPropertyOrNull("description"),
            Archived = json.GetPropertyOrDefault("archived", false),
            Leader = json.TryGetProperty("leader", out var leader) && leader.ValueKind == JsonValueKind.Object
                ? leader.GetPropertyOrNull("login")
                : null,
            CreatedDate = json.TryGetProperty("createdDate", out var created) && created.ValueKind == JsonValueKind.Number
                ? DateTimeOffset.FromUnixTimeMilliseconds(created.GetInt64()).DateTime
                : null
        };
    }

    private static YouTrackIssue MapIssue(JsonElement json)
    {
        var issue = new YouTrackIssue
        {
            Id = json.GetPropertyOrDefault("idReadable", json.GetPropertyOrDefault("id", string.Empty)),
            IdReadable = json.GetPropertyOrNull("idReadable"),
            Summary = json.GetPropertyOrDefault("summary", string.Empty),
            Description = json.GetPropertyOrNull("description"),
            CommentsCount = json.GetPropertyOrDefault("commentsCount", 0),
            Created = json.TryGetProperty("created", out var created) && created.ValueKind == JsonValueKind.Number
                ? DateTimeOffset.FromUnixTimeMilliseconds(created.GetInt64()).DateTime
                : null,
            Updated = json.TryGetProperty("updated", out var updated) && updated.ValueKind == JsonValueKind.Number
                ? DateTimeOffset.FromUnixTimeMilliseconds(updated.GetInt64()).DateTime
                : null,
            Resolved = json.TryGetProperty("resolved", out var resolved) && resolved.ValueKind == JsonValueKind.Number
                ? DateTimeOffset.FromUnixTimeMilliseconds(resolved.GetInt64()).DateTime
                : null
        };

        // Map project
        if (json.TryGetProperty("project", out var project) && project.ValueKind == JsonValueKind.Object)
        {
            issue.ProjectId = project.GetPropertyOrNull("id");
            issue.ProjectShortName = project.GetPropertyOrNull("shortName");
        }

        // Map reporter
        if (json.TryGetProperty("reporter", out var reporter) && reporter.ValueKind == JsonValueKind.Object)
        {
            issue.Reporter = reporter.GetPropertyOrNull("login");
        }

        // Map tags
        if (json.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array)
        {
            foreach (var tag in tags.EnumerateArray())
            {
                var name = tag.GetPropertyOrNull("name");
                if (name != null)
                    issue.Tags.Add(name);
            }
        }

        // Map custom fields
        if (json.TryGetProperty("customFields", out var customFields) && customFields.ValueKind == JsonValueKind.Array)
        {
            foreach (var field in customFields.EnumerateArray())
            {
                var fieldName = field.GetPropertyOrNull("name");
                if (fieldName == null) continue;

                string? fieldValue = null;
                if (field.TryGetProperty("value", out var value))
                {
                    if (value.ValueKind == JsonValueKind.Object)
                    {
                        fieldValue = value.GetPropertyOrNull("name")
                            ?? value.GetPropertyOrNull("login")
                            ?? value.GetPropertyOrNull("text");
                    }
                    else if (value.ValueKind == JsonValueKind.String)
                    {
                        fieldValue = value.GetString();
                    }
                    else if (value.ValueKind == JsonValueKind.Array)
                    {
                        var values = new List<string>();
                        foreach (var v in value.EnumerateArray())
                        {
                            if (v.ValueKind == JsonValueKind.Object)
                            {
                                var vName = v.GetPropertyOrNull("name") ?? v.GetPropertyOrNull("login");
                                if (vName != null) values.Add(vName);
                            }
                            else if (v.ValueKind == JsonValueKind.String)
                            {
                                var vStr = v.GetString();
                                if (vStr != null) values.Add(vStr);
                            }
                        }
                        fieldValue = string.Join(", ", values);
                    }
                }

                issue.CustomFields[fieldName] = fieldValue;

                // Map common fields
                switch (fieldName.ToLowerInvariant())
                {
                    case "state":
                        issue.State = fieldValue;
                        break;
                    case "priority":
                        issue.Priority = fieldValue;
                        break;
                    case "type":
                        issue.Type = fieldValue;
                        break;
                    case "assignee":
                        issue.Assignee = fieldValue;
                        break;
                }
            }
        }

        return issue;
    }

    private static YouTrackComment MapComment(JsonElement json)
    {
        return new YouTrackComment
        {
            Id = json.GetPropertyOrDefault("id", string.Empty),
            Text = json.GetPropertyOrDefault("text", string.Empty),
            Author = json.TryGetProperty("author", out var author) && author.ValueKind == JsonValueKind.Object
                ? author.GetPropertyOrNull("login")
                : null,
            AuthorFullName = json.TryGetProperty("author", out var authorFull) && authorFull.ValueKind == JsonValueKind.Object
                ? authorFull.GetPropertyOrNull("fullName")
                : null,
            Created = json.TryGetProperty("created", out var created) && created.ValueKind == JsonValueKind.Number
                ? DateTimeOffset.FromUnixTimeMilliseconds(created.GetInt64()).DateTime
                : null,
            Updated = json.TryGetProperty("updated", out var updated) && updated.ValueKind == JsonValueKind.Number
                ? DateTimeOffset.FromUnixTimeMilliseconds(updated.GetInt64()).DateTime
                : null,
            Deleted = json.GetPropertyOrDefault("deleted", false)
        };
    }

    private static YouTrackUser MapUser(JsonElement json)
    {
        return new YouTrackUser
        {
            Id = json.GetPropertyOrDefault("id", string.Empty),
            Login = json.GetPropertyOrDefault("login", string.Empty),
            FullName = json.GetPropertyOrNull("fullName"),
            Email = json.GetPropertyOrNull("email"),
            Banned = json.GetPropertyOrDefault("banned", false),
            Guest = json.GetPropertyOrDefault("guest", false),
            AvatarUrl = json.GetPropertyOrNull("avatarUrl")
        };
    }

    #endregion

    public void Dispose()
    {
        if (!_disposed)
        {
            _httpClient.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Extension methods for JsonElement.
/// </summary>
internal static class JsonElementExtensions
{
    public static string GetPropertyOrDefault(this JsonElement element, string propertyName, string defaultValue)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            return prop.GetString() ?? defaultValue;
        }
        return defaultValue;
    }

    public static bool GetPropertyOrDefault(this JsonElement element, string propertyName, bool defaultValue)
    {
        if (element.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.True) return true;
            if (prop.ValueKind == JsonValueKind.False) return false;
        }
        return defaultValue;
    }

    public static int GetPropertyOrDefault(this JsonElement element, string propertyName, int defaultValue)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number)
        {
            return prop.GetInt32();
        }
        return defaultValue;
    }

    public static string? GetPropertyOrNull(this JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            return prop.GetString();
        }
        return null;
    }
}
