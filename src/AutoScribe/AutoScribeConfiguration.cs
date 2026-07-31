namespace OrbAutomata;

internal enum AutoScribeOperationMode
{
    Disabled = 0,
    Active = 1,
}

/// <summary>
/// Saved Auto Scribe intent. Roles are stable semantic keys; an empty value selects every audited
/// producible role.
/// </summary>
internal sealed record AutoScribeConfiguration
{
    internal AutoScribeOperationMode Mode { get; init; }
    internal string Roles { get; init; } = string.Empty;
}
