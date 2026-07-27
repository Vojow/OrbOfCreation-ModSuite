using System;

namespace OrbModding.Common.Runtime.Strategy;

/// <summary>
/// How freely the suite may spend one resource right now. Every kind either permits what
/// configuration already permitted or permits less — a stance can tighten spending, never loosen it
/// (see <c>docs/runtime-architecture/goals-and-invariants.md</c>: strategy is advisory beneath
/// cycle-pinned user policy and current native validation).
/// </summary>
internal enum SuiteResourceStanceKind
{
    /// <summary>Spend as configuration allows. The default, and the whole neutral bulletin.</summary>
    Free = 0,

    /// <summary>Leave at least an absolute quantity untouched after the spend.</summary>
    FloorAbsolute = 1,

    /// <summary>Leave at least a fraction of storage capacity untouched after the spend.</summary>
    FloorFraction = 2,

    /// <summary>
    /// Permit only spends that are trivial relative to holdings. This is the stance for the
    /// "saving toward a threshold" case: while saving 5 knowledge, a 0.1-knowledge purchase is
    /// noise worth allowing and a 2-knowledge purchase is not.
    /// </summary>
    TrivialOnly = 3,

    /// <summary>Spend nothing of this resource.</summary>
    Embargo = 4,
}

/// <summary>
/// How much the suite currently wants more of one resource. Published for domain services to rank
/// their own options against — the strategist says what is valuable, each service decides what to do
/// about it. Ordered so a comparison is meaningful.
/// </summary>
/// <remarks>
/// Nothing consumes this yet. It is published from the start because the strategist can populate it
/// truthfully today (the resource a milestone is saving for is <see cref="Critical"/>), and because
/// upgrade-effect classification — the eventual consumer — is a separate research track that should
/// not also have to change the publication shape when it lands.
/// </remarks>
internal enum SuiteResourceWant
{
    /// <summary>Actively not wanted; holding more has no current value.</summary>
    Ignored = 0,

    /// <summary>No opinion. The neutral default.</summary>
    Neutral = 1,

    /// <summary>More of this would help the current plan.</summary>
    Wanted = 2,

    /// <summary>The current plan is gated on this resource.</summary>
    Critical = 3,
}

/// <summary>
/// One resource's published spend stance and want signal. Identity is the stable resource UUID;
/// names are never identity (see <c>AGENTS.md</c>). Magnitudes stay on the game's own
/// <see cref="BigDouble"/> so no conversion happens between capture, strategy, and consumption.
/// </summary>
internal readonly struct SuiteResourceStance
{
    internal SuiteResourceStance(
        Guid resourceId,
        SuiteResourceStanceKind kind,
        BigDouble floorAbsolute,
        double floorFraction,
        double maxSpendFraction,
        SuiteResourceWant want)
    {
        ResourceId = resourceId;
        Kind = kind;
        FloorAbsolute = floorAbsolute;
        FloorFraction = floorFraction;
        MaxSpendFraction = maxSpendFraction;
        Want = want;
    }

    internal Guid ResourceId { get; }
    internal SuiteResourceStanceKind Kind { get; }

    /// <summary>The quantity that must remain after a spend under <see cref="SuiteResourceStanceKind.FloorAbsolute"/>.</summary>
    internal BigDouble FloorAbsolute { get; }

    /// <summary>The fraction of capacity that must remain under <see cref="SuiteResourceStanceKind.FloorFraction"/>.</summary>
    internal double FloorFraction { get; }

    /// <summary>The largest share of current holdings one spend may cost under <see cref="SuiteResourceStanceKind.TrivialOnly"/>.</summary>
    internal double MaxSpendFraction { get; }

    internal SuiteResourceWant Want { get; }

    internal static SuiteResourceStance Free(Guid resourceId, SuiteResourceWant want = SuiteResourceWant.Neutral) =>
        new(resourceId, SuiteResourceStanceKind.Free, default, 0d, 0d, want);

    internal static SuiteResourceStance Embargo(Guid resourceId, SuiteResourceWant want = SuiteResourceWant.Critical) =>
        new(resourceId, SuiteResourceStanceKind.Embargo, default, 0d, 0d, want);

    internal static SuiteResourceStance FloorOf(
        Guid resourceId,
        BigDouble floor,
        SuiteResourceWant want = SuiteResourceWant.Critical) =>
        new(resourceId, SuiteResourceStanceKind.FloorAbsolute, floor, 0d, 0d, want);

    internal static SuiteResourceStance FractionOfCapacity(
        Guid resourceId,
        double fraction,
        SuiteResourceWant want = SuiteResourceWant.Critical) =>
        new(resourceId, SuiteResourceStanceKind.FloorFraction, default, fraction, 0d, want);

    internal static SuiteResourceStance TrivialOnly(
        Guid resourceId,
        double maxSpendFraction,
        SuiteResourceWant want = SuiteResourceWant.Critical) =>
        new(resourceId, SuiteResourceStanceKind.TrivialOnly, default, 0d, maxSpendFraction, want);
}
