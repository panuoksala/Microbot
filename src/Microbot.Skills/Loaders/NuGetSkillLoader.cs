namespace Microbot.Skills.Loaders;

using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microbot.Core.Interfaces;
using Microbot.Core.Models;

/// <summary>
/// Loads skills from NuGet packages (DLL assemblies) in the configured folder.
/// </summary>
public class NuGetSkillLoader : ISkillLoader
{
    private readonly SkillsConfig _config;
    private readonly ILogger<NuGetSkillLoader>? _logger;
    private readonly SkillAssemblyLoadContext _loadContext;
    private bool _disposed;

    /// <inheritdoc />
    public string LoaderName => "NuGet";

    /// <summary>
    /// Creates a new NuGet skill loader.
    /// </summary>
    /// <param name="config">Skills configuration.</param>
    /// <param name="logger">Optional logger.</param>
    public NuGetSkillLoader(SkillsConfig config, ILogger<NuGetSkillLoader>? logger = null)
    {
        _config = config;
        _logger = logger;
        _loadContext = new SkillAssemblyLoadContext();
    }

    /// <inheritdoc />
    public async Task<IEnumerable<KernelPlugin>> LoadSkillsAsync(
        CancellationToken cancellationToken = default)
    {
        var plugins = new List<KernelPlugin>();
        var nugetFolder = _config.NuGetFolder;

        if (!Directory.Exists(nugetFolder))
        {
            _logger?.LogWarning("NuGet folder does not exist: {Folder}", nugetFolder);
            return plugins;
        }

        // Load explicitly configured skills
        foreach (var skillConfig in _config.NuGetSkills.Where(s => s.Enabled))
        {
            try
            {
                _logger?.LogInformation("Loading NuGet skill: {SkillName}", skillConfig.Name);
                var plugin = await LoadSkillFromConfigAsync(skillConfig, cancellationToken);
                if (plugin != null)
                {
                    plugins.Add(plugin);
                    _logger?.LogInformation("Successfully loaded NuGet skill: {SkillName}", skillConfig.Name);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to load NuGet skill {SkillName}", skillConfig.Name);
            }
        }

        // Auto-discover skills from DLLs in the nuget folder
        try
        {
            var discoveredPlugins = await DiscoverSkillsAsync(nugetFolder, cancellationToken);
            plugins.AddRange(discoveredPlugins);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to auto-discover skills from {Folder}", nugetFolder);
        }

        return plugins;
    }

    private Task<KernelPlugin?> LoadSkillFromConfigAsync(
        NuGetSkillConfig config,
        CancellationToken cancellationToken)
    {
        try
        {
            var assemblyPath = Path.IsPathRooted(config.AssemblyPath)
                ? config.AssemblyPath
                : Path.Combine(_config.NuGetFolder, config.AssemblyPath);

            assemblyPath = Path.GetFullPath(assemblyPath);

            if (!File.Exists(assemblyPath))
            {
                _logger?.LogWarning("Assembly not found: {Path}", assemblyPath);
                return Task.FromResult<KernelPlugin?>(null);
            }

            var assembly = _loadContext.LoadFromAssemblyPath(assemblyPath);

            Type? skillType = null;
            if (!string.IsNullOrEmpty(config.TypeName))
            {
                skillType = assembly.GetType(config.TypeName);
                if (skillType == null)
                {
                    _logger?.LogWarning("Type {TypeName} not found in assembly {Assembly}",
                        config.TypeName, assemblyPath);
                    return Task.FromResult<KernelPlugin?>(null);
                }
            }
            else
            {
                // Find first type with KernelFunction methods
                skillType = FindSkillType(assembly);
            }

            if (skillType == null)
            {
                _logger?.LogWarning("No skill type found in assembly {Assembly}", assemblyPath);
                return Task.FromResult<KernelPlugin?>(null);
            }

            var instance = Activator.CreateInstance(skillType);
            if (instance == null)
            {
                _logger?.LogWarning("Failed to create instance of {Type}", skillType.FullName);
                return Task.FromResult<KernelPlugin?>(null);
            }

            var plugin = KernelPluginFactory.CreateFromObject(instance, config.Name);
            return Task.FromResult<KernelPlugin?>(plugin);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error loading skill {SkillName}", config.Name);
            return Task.FromResult<KernelPlugin?>(null);
        }
    }

    private Task<IEnumerable<KernelPlugin>> DiscoverSkillsAsync(
        string folder,
        CancellationToken cancellationToken)
    {
        var plugins = new List<KernelPlugin>();

        // Get all DLLs that are not already configured
        var configuredAssemblies = _config.NuGetSkills
            .Select(s => Path.GetFileName(s.AssemblyPath).ToLowerInvariant())
            .ToHashSet();

        foreach (var dllPath in Directory.GetFiles(folder, "*.dll"))
        {
            var fileName = Path.GetFileName(dllPath).ToLowerInvariant();

            // Skip if already configured
            if (configuredAssemblies.Contains(fileName))
                continue;

            // Skip common framework/dependency DLLs
            if (IsFrameworkAssembly(fileName))
                continue;

            try
            {
                var assembly = _loadContext.LoadFromAssemblyPath(dllPath);
                var skillTypes = FindAllSkillTypes(assembly);

                foreach (var skillType in skillTypes)
                {
                    try
                    {
                        var instance = Activator.CreateInstance(skillType);
                        if (instance == null)
                            continue;

                        var pluginName = skillType.Name
                            .Replace("Plugin", "")
                            .Replace("Skill", "");

                        var plugin = KernelPluginFactory.CreateFromObject(instance, pluginName);
                        plugins.Add(plugin);

                        _logger?.LogInformation("Auto-discovered skill: {SkillName} from {Assembly}",
                            pluginName, Path.GetFileName(dllPath));
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Failed to create instance of {Type}", skillType.FullName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Could not load assembly {Path} for skill discovery", dllPath);
            }
        }

        return Task.FromResult<IEnumerable<KernelPlugin>>(plugins);
    }

    private Type? FindSkillType(Assembly assembly)
    {
        return assembly.GetTypes()
            .FirstOrDefault(t => t.IsClass && !t.IsAbstract &&
                t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Any(m => m.GetCustomAttribute<KernelFunctionAttribute>() != null));
    }

    private IEnumerable<Type> FindAllSkillTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract &&
                    t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .Any(m => m.GetCustomAttribute<KernelFunctionAttribute>() != null));
        }
        catch (ReflectionTypeLoadException ex)
        {
            // Return types that could be loaded
            return ex.Types.Where(t => t != null)!;
        }
    }

    private static bool IsFrameworkAssembly(string fileName)
    {
        // Skip common framework and dependency assemblies
        var frameworkPrefixes = new[]
        {
            "system.", "microsoft.", "netstandard", "mscorlib",
            "windowsbase", "presentationcore", "presentationframework"
        };

        return frameworkPrefixes.Any(prefix =>
            fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;

        // Unload the assembly load context
        _loadContext.Unload();
        _disposed = true;

        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Custom AssemblyLoadContext for loading skill assemblies.
/// This allows for isolation and potential unloading of skill assemblies.
/// </summary>
internal class SkillAssemblyLoadContext : AssemblyLoadContext
{
    public SkillAssemblyLoadContext() : base(isCollectible: true)
    {
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Try to load from the default context first
        try
        {
            return Default.LoadFromAssemblyName(assemblyName);
        }
        catch
        {
            return null;
        }
    }
}
