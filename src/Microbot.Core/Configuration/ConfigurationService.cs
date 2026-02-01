namespace Microbot.Core.Configuration;

using System.Text.Json;
using System.Text.Json.Serialization;
using Microbot.Core.Interfaces;
using Microbot.Core.Models;

/// <summary>
/// Service for managing Microbot configuration file.
/// </summary>
public class ConfigurationService : IConfigurationService
{
    private const string ConfigFileName = "Microbot.config";
    private readonly string _configPath;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Creates a new ConfigurationService instance.
    /// </summary>
    /// <param name="basePath">Base path for the configuration file. Defaults to current directory.</param>
    public ConfigurationService(string? basePath = null)
    {
        _configPath = Path.Combine(basePath ?? Directory.GetCurrentDirectory(), ConfigFileName);
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };
    }

    /// <inheritdoc />
    public string ConfigurationPath => _configPath;

    /// <inheritdoc />
    public bool ConfigurationExists() => File.Exists(_configPath);

    /// <inheritdoc />
    public async Task<MicrobotConfig> LoadConfigurationAsync()
    {
        if (!ConfigurationExists())
        {
            return new MicrobotConfig();
        }

        try
        {
            var json = await File.ReadAllTextAsync(_configPath);
            return JsonSerializer.Deserialize<MicrobotConfig>(json, _jsonOptions)
                ?? new MicrobotConfig();
        }
        catch (JsonException)
        {
            // If the config file is corrupted, return a default config
            return new MicrobotConfig();
        }
    }

    /// <inheritdoc />
    public async Task SaveConfigurationAsync(MicrobotConfig config)
    {
        var json = JsonSerializer.Serialize(config, _jsonOptions);
        
        // Ensure the directory exists
        var directory = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(_configPath, json);
    }
}
