namespace Microbot.Core.Interfaces;

using Microbot.Core.Models;

/// <summary>
/// Service for managing Microbot configuration.
/// </summary>
public interface IConfigurationService
{
    /// <summary>
    /// Loads the configuration from the config file.
    /// </summary>
    /// <returns>The loaded configuration, or a default configuration if the file doesn't exist.</returns>
    Task<MicrobotConfig> LoadConfigurationAsync();

    /// <summary>
    /// Saves the configuration to the config file.
    /// </summary>
    /// <param name="config">The configuration to save.</param>
    Task SaveConfigurationAsync(MicrobotConfig config);

    /// <summary>
    /// Checks if the configuration file exists.
    /// </summary>
    /// <returns>True if the configuration file exists, false otherwise.</returns>
    bool ConfigurationExists();

    /// <summary>
    /// Gets the path to the configuration file.
    /// </summary>
    string ConfigurationPath { get; }
}
