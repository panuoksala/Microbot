namespace Microbot.Core.Services;

using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using Microbot.Core.Models.McpRegistry;

/// <summary>
/// HTTP client for the official MCP Registry API.
/// </summary>
public class McpRegistryClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _disposed;

    /// <summary>
    /// Base URL for the MCP Registry API.
    /// Note: Must end with trailing slash for proper relative URL resolution.
    /// </summary>
    public const string BaseUrl = "https://registry.modelcontextprotocol.io/v0.1/";

    /// <summary>
    /// Creates a new MCP Registry client.
    /// </summary>
    public McpRegistryClient()
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(BaseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Microbot/1.0");

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
    }

    /// <summary>
    /// Lists MCP servers from the registry with pagination.
    /// </summary>
    /// <param name="limit">Maximum number of servers to return (default: 50).</param>
    /// <param name="cursor">Pagination cursor for the next page.</param>
    /// <param name="search">Optional search query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List response with servers and pagination metadata.</returns>
    public async Task<McpRegistryListResponse> ListServersAsync(
        int limit = 50,
        string? cursor = null,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        var queryParams = new List<string> { $"limit={limit}" };

        if (!string.IsNullOrEmpty(cursor))
        {
            queryParams.Add($"cursor={HttpUtility.UrlEncode(cursor)}");
        }

        if (!string.IsNullOrEmpty(search))
        {
            queryParams.Add($"search={HttpUtility.UrlEncode(search)}");
        }

        var url = $"servers?{string.Join("&", queryParams)}";

        var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<McpRegistryListResponse>(_jsonOptions, cancellationToken);
        return result ?? new McpRegistryListResponse();
    }

    /// <summary>
    /// Gets a specific MCP server by name.
    /// </summary>
    /// <param name="serverName">The server name (e.g., "io.github/github-mcp").</param>
    /// <param name="version">Version to get, or "latest" for the latest version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The server details, or null if not found.</returns>
    public async Task<McpRegistryServer?> GetServerAsync(
        string serverName,
        string version = "latest",
        CancellationToken cancellationToken = default)
    {
        var encodedName = HttpUtility.UrlEncode(serverName);
        var url = $"servers/{encodedName}/versions/{version}";

        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<McpRegistryServerResponse>(_jsonOptions, cancellationToken);
            return result?.Server;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>
    /// Gets all versions of a specific MCP server.
    /// </summary>
    /// <param name="serverName">The server name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of all versions.</returns>
    public async Task<List<McpRegistryServer>> GetServerVersionsAsync(
        string serverName,
        CancellationToken cancellationToken = default)
    {
        var encodedName = HttpUtility.UrlEncode(serverName);
        var url = $"servers/{encodedName}/versions";

        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return [];
            }

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<McpRegistryVersionsResponse>(_jsonOptions, cancellationToken);
            return result?.Versions.Select(v => v.Server).ToList() ?? [];
        }
        catch (HttpRequestException)
        {
            return [];
        }
    }

    /// <summary>
    /// Searches for MCP servers by query.
    /// </summary>
    /// <param name="query">Search query.</param>
    /// <param name="limit">Maximum results to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of matching servers.</returns>
    public async Task<List<McpRegistryServer>> SearchServersAsync(
        string query,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var response = await ListServersAsync(limit, null, query, cancellationToken);
        return response.Servers.Select(s => s.Server).ToList();
    }

    /// <summary>
    /// Disposes the HTTP client.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _httpClient.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
