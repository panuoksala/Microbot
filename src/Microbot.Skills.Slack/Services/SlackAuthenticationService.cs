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
    private AuthTestResponse? _authInfo;

    /// <summary>
    /// Creates a new SlackAuthenticationService instance.
    /// </summary>
    /// <param name="config">Slack skill configuration.</param>
    /// <param name="logger">Optional logger.</param>
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
    /// <returns>Array of required OAuth scopes.</returns>
    public string[] GetRequiredScopes()
    {
        var mode = Enum.Parse<SlackSkillMode>(_config.Mode, ignoreCase: true);

        return mode switch
        {
            SlackSkillMode.ReadOnly =>
            [
                "channels:read",
                "channels:history",
                "groups:read",
                "groups:history",
                "im:read",
                "im:history",
                "mpim:read",
                "mpim:history",
                "users:read"
            ],
            SlackSkillMode.Full =>
            [
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
            ],
            _ => throw new ArgumentException($"Unknown mode: {_config.Mode}")
        };
    }

    /// <summary>
    /// Creates and returns an authenticated Slack API client.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The authenticated Slack API client.</returns>
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
        _authInfo = await _slackClient.Auth.Test(cancellationToken);
        _logger?.LogInformation("Authenticated as: {BotName} (ID: {BotId}) in workspace: {Team} (ID: {TeamId})", 
            _authInfo.User, _authInfo.UserId, _authInfo.Team, _authInfo.TeamId);

        return _slackClient;
    }

    /// <summary>
    /// Gets the authenticated bot's user ID.
    /// </summary>
    /// <returns>The bot's user ID, or null if not authenticated.</returns>
    public string? GetBotUserId() => _authInfo?.UserId;

    /// <summary>
    /// Gets the workspace/team ID.
    /// </summary>
    /// <returns>The team ID, or null if not authenticated.</returns>
    public string? GetTeamId() => _authInfo?.TeamId;

    /// <summary>
    /// Gets the workspace/team name.
    /// </summary>
    /// <returns>The team name, or null if not authenticated.</returns>
    public string? GetTeamName() => _authInfo?.Team;

    /// <summary>
    /// Checks if the current mode allows sending messages.
    /// </summary>
    /// <returns>True if sending messages is allowed.</returns>
    public bool CanSendMessages()
    {
        var mode = Enum.Parse<SlackSkillMode>(_config.Mode, ignoreCase: true);
        return mode == SlackSkillMode.Full;
    }
}
