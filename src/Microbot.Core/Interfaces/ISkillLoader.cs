namespace Microbot.Core.Interfaces;

using Microsoft.SemanticKernel;

/// <summary>
/// Interface for loading skills/plugins into the Semantic Kernel.
/// </summary>
public interface ISkillLoader : IAsyncDisposable
{
    /// <summary>
    /// Loads all available skills and returns them as Kernel plugins.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A collection of loaded Kernel plugins.</returns>
    Task<IEnumerable<KernelPlugin>> LoadSkillsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the name of this skill loader (e.g., "MCP", "NuGet").
    /// </summary>
    string LoaderName { get; }
}
