namespace OrbModding.Common.Runtime.Strategy;

/// <summary>
/// Well-known bulletins. These live outside <see cref="SuiteStrategy"/> because a published
/// service-cycle shape may not own non-constant static storage — an ambient static on a published
/// type is exactly the kind of side channel the structural validator exists to reject, and the rule
/// does not get an exception just because this particular static happens to be immutable.
/// </summary>
internal static class SuiteStrategyDefaults
{
    /// <summary>
    /// The generation-1 fallback the publisher starts from, and the bulletin the strategist returns
    /// to when nothing constrains spending. It carries no stances, so every consumer resolves
    /// <see cref="SuiteResourceStanceKind.Free"/> for every resource: a suite whose strategist
    /// never runs, is disabled, or faults behaves exactly as it did before strategy existed.
    /// </summary>
    internal static readonly SuiteStrategy Neutral = new();
}
