namespace Microbot.Memory;

/// <summary>
/// Represents the source type of a memory entry.
/// </summary>
public enum MemorySource
{
    /// <summary>
    /// Memory from MEMORY.md files or memory/ directory.
    /// </summary>
    Memory,

    /// <summary>
    /// Memory from session transcripts.
    /// </summary>
    Sessions
}
