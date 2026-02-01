namespace Microbot.Skills.Teams.Services;

using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microbot.Core.Models;

/// <summary>
/// Handles authentication for the Teams skill using Azure Identity.
/// Supports both Device Code and Interactive Browser authentication flows.
/// Uses multi-tenant authentication (TenantId = "common") to access teams from
/// both the home tenant and guest tenants.
/// </summary>
public class TeamsAuthenticationService
{
    private readonly TeamsSkillConfig _config;
    private readonly ILogger<TeamsAuthenticationService>? _logger;
    private GraphServiceClient? _graphClient;

    /// <summary>
    /// Creates a new TeamsAuthenticationService instance.
    /// </summary>
    /// <param name="config">Teams skill configuration.</param>
    /// <param name="logger">Optional logger.</param>
    public TeamsAuthenticationService(
        TeamsSkillConfig config,
        ILogger<TeamsAuthenticationService>? logger = null)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Gets the Microsoft Graph scopes required for the configured mode.
    /// </summary>
    /// <returns>Array of required scope strings.</returns>
    public string[] GetRequiredScopes()
    {
        var mode = Enum.Parse<TeamsSkillMode>(_config.Mode, ignoreCase: true);

        return mode switch
        {
            TeamsSkillMode.ReadOnly =>
            [
                "User.Read",
                "Team.ReadBasic.All",
                "Channel.ReadBasic.All",
                "ChannelMessage.Read.All",
                "Chat.Read",
                "ChatMessage.Read"
            ],
            TeamsSkillMode.Full =>
            [
                "User.Read",
                "Team.ReadBasic.All",
                "Channel.ReadBasic.All",
                "ChannelMessage.Read.All",
                "ChannelMessage.Send",
                "Chat.Read",
                "ChatMessage.Read",
                "ChatMessage.Send"
            ],
            _ => throw new ArgumentException($"Unknown mode: {_config.Mode}")
        };
    }

    /// <summary>
    /// Creates and returns an authenticated GraphServiceClient.
    /// </summary>
    /// <param name="deviceCodeCallback">Callback to display device code message to user (for Device Code flow).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Authenticated GraphServiceClient instance.</returns>
    public async Task<GraphServiceClient> GetGraphClientAsync(
        Action<string>? deviceCodeCallback = null,
        CancellationToken cancellationToken = default)
    {
        if (_graphClient != null)
            return _graphClient;

        if (string.IsNullOrEmpty(_config.ClientId))
            throw new InvalidOperationException("ClientId is required for Teams skill authentication");

        var scopes = GetRequiredScopes();

        _logger?.LogInformation(
            "Authenticating with method: {Method}, TenantId: {TenantId}, Scopes: {Scopes}",
            _config.AuthenticationMethod,
            _config.TenantId,
            string.Join(", ", scopes));

        TokenCredential credential;
        var authMethod = _config.AuthenticationMethod.ToLowerInvariant();
        
        if (authMethod == "devicecode")
        {
            credential = CreateDeviceCodeCredential(deviceCodeCallback);
        }
        else if (authMethod == "interactivebrowser")
        {
            credential = CreateInteractiveBrowserCredential();
        }
        else
        {
            throw new ArgumentException(
                $"Unknown authentication method: {_config.AuthenticationMethod}. " +
                "Supported methods: DeviceCode, InteractiveBrowser");
        }

        _graphClient = new GraphServiceClient(credential, scopes);

        // Verify authentication by getting user info
        try
        {
            var user = await _graphClient.Me.GetAsync(cancellationToken: cancellationToken);
            _logger?.LogInformation(
                "Successfully authenticated as: {DisplayName} ({Email})",
                user?.DisplayName ?? "Unknown",
                user?.UserPrincipalName ?? user?.Mail ?? "Unknown");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to verify authentication");
            _graphClient = null;
            throw;
        }

        return _graphClient;
    }

    /// <summary>
    /// Creates a DeviceCodeCredential for authentication.
    /// Uses "common" tenant for multi-tenant support (home + guest tenants).
    /// </summary>
    private DeviceCodeCredential CreateDeviceCodeCredential(Action<string>? callback)
    {
        var options = new DeviceCodeCredentialOptions
        {
            ClientId = _config.ClientId,
            // Use "common" for multi-tenant to access teams from all tenants
            TenantId = _config.TenantId,
            DeviceCodeCallback = (info, ct) =>
            {
                var message = info.Message;
                _logger?.LogInformation("Device Code Authentication: {Message}", message);
                callback?.Invoke(message);
                return Task.CompletedTask;
            }
        };

        return new DeviceCodeCredential(options);
    }

    /// <summary>
    /// Creates an InteractiveBrowserCredential for authentication.
    /// Uses "common" tenant for multi-tenant support (home + guest tenants).
    /// </summary>
    private InteractiveBrowserCredential CreateInteractiveBrowserCredential()
    {
        var options = new InteractiveBrowserCredentialOptions
        {
            ClientId = _config.ClientId,
            // Use "common" for multi-tenant to access teams from all tenants
            TenantId = _config.TenantId,
            RedirectUri = new Uri(_config.RedirectUri)
        };

        return new InteractiveBrowserCredential(options);
    }

    /// <summary>
    /// Clears the cached GraphServiceClient, forcing re-authentication on next use.
    /// </summary>
    public void ClearCache()
    {
        _graphClient = null;
        _logger?.LogInformation("Authentication cache cleared");
    }

    /// <summary>
    /// Checks if the current mode allows sending messages.
    /// </summary>
    public bool CanSendMessages()
    {
        var mode = Enum.Parse<TeamsSkillMode>(_config.Mode, ignoreCase: true);
        return mode == TeamsSkillMode.Full;
    }
}
