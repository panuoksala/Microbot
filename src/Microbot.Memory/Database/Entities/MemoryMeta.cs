namespace Microbot.Memory.Database.Entities;

/// <summary>
/// Key-value store for memory index metadata.
/// </summary>
public class MemoryMeta
{
    /// <summary>
    /// The metadata key.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// The metadata value.
    /// </summary>
    public string Value { get; set; } = string.Empty;
}
