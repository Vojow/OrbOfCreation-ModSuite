namespace OrbAutomata;

internal enum AutoItemsOperationMode
{
    Disabled = 0,
    Active = 1,
}

/// <summary>
/// Saved Auto Items intent. The suite publishes this with the rest of its configuration as one
/// immutable reading and one generation.
/// </summary>
internal sealed record AutoItemsConfiguration
{
    internal AutoItemsOperationMode Mode { get; init; }
    internal bool UseScrolls { get; init; } = true;
    internal bool UseRelics { get; init; } = true;
    internal string TemporaryItemAllowlist { get; init; } = string.Empty;
}
