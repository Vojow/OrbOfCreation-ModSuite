using System;

namespace OrbModding.Common.Runtime.Strategy;

/// <summary>Why one resource's share of a proposed spend was allowed or refused.</summary>
internal enum SuiteSpendOutcome
{
    /// <summary>Configuration and strategy both permit the spend.</summary>
    Allowed = 0,

    /// <summary>
    /// Permitted, but the stance could not be evaluated against the captured facts and was skipped
    /// — a fraction-of-capacity floor on a resource the game does not cap. Reported distinctly so a
    /// strategist authoring error is visible in diagnostics instead of silently doing nothing.
    /// </summary>
    AllowedStanceNotApplicable = 1,

    /// <summary>Holdings do not cover the cost itself, before any reserve applies.</summary>
    BlockedInsufficientQuantity = 2,

    /// <summary>The operator's own reserve floor refuses it. Strategy is not consulted.</summary>
    BlockedConfiguredReserve = 3,

    /// <summary>The published stance requires more to remain than the spend would leave.</summary>
    BlockedStrategyFloor = 4,

    /// <summary>The spend is too large a share of holdings for a trivial-only stance.</summary>
    BlockedStrategyRatio = 5,

    /// <summary>The resource is embargoed.</summary>
    BlockedStrategyEmbargo = 6,

    /// <summary>A negative cost or quantity. Unknown or contradictory evidence fails closed.</summary>
    BlockedInvalidSnapshot = 7,
}

internal readonly struct SuiteSpendDecision
{
    internal SuiteSpendDecision(SuiteSpendOutcome outcome) => Outcome = outcome;

    internal SuiteSpendOutcome Outcome { get; }

    internal bool Allowed =>
        Outcome is SuiteSpendOutcome.Allowed or SuiteSpendOutcome.AllowedStanceNotApplicable;

    /// <summary>True when strategy, rather than configuration or affordability, is the reason.</summary>
    internal bool BlockedByStrategy =>
        Outcome is SuiteSpendOutcome.BlockedStrategyFloor
            or SuiteSpendOutcome.BlockedStrategyRatio
            or SuiteSpendOutcome.BlockedStrategyEmbargo;
}

/// <summary>
/// Decides whether one resource may fund one proposed spend, given the operator's configured
/// reserve and the cycle-pinned strategy stance. Pure: it takes captured magnitudes rather than any
/// service's frame type, so every domain service can share it and it can be tested without the game.
/// </summary>
/// <remarks>
/// <para>
/// Order is the whole safety story. Configuration is evaluated first and independently; only if the
/// operator would have permitted the spend is the stance consulted, and the stance can then only
/// refuse. There is no path by which a bulletin permits something configuration refused, so a
/// wrong, stale, or hostile bulletin can cost throughput and nothing else. This is the concrete form
/// of the invariant that strategy is advisory beneath cycle-pinned user policy
/// (<c>docs/runtime-architecture/goals-and-invariants.md</c>).
/// </para>
/// <para>
/// The configured half reproduces the existing Auto Buy reserve arithmetic exactly — absolute floor
/// versus cost-relative floor, whichever is larger, required on top of the cost — so adopting this
/// policy with a neutral bulletin cannot change what the service already does.
/// </para>
/// </remarks>
internal static class SuiteResourceSpendPolicy
{
    internal static SuiteSpendDecision Evaluate(
        in SuiteResourceStance stance,
        in BigDouble cost,
        in BigDouble quantity,
        bool hasCapacity,
        in BigDouble capacity,
        in BigDouble configuredAbsoluteReserve,
        double configuredRelativeMultiplier)
    {
        if (IsNegative(cost) || IsNegative(quantity))
            return new SuiteSpendDecision(SuiteSpendOutcome.BlockedInvalidSnapshot);

        // A resource this purchase does not consume is never constrained by it, embargo included.
        if (IsZero(cost))
            return new SuiteSpendDecision(SuiteSpendOutcome.Allowed);

        if (quantity.CompareTo(cost) < 0)
            return new SuiteSpendDecision(SuiteSpendOutcome.BlockedInsufficientQuantity);

        var relativeReserve = cost * Math.Max(0d, configuredRelativeMultiplier);
        var configuredFloor = BigDouble.Max(configuredAbsoluteReserve, relativeReserve);
        if (quantity.CompareTo(cost + configuredFloor) < 0)
            return new SuiteSpendDecision(SuiteSpendOutcome.BlockedConfiguredReserve);

        return EvaluateStance(in stance, in cost, in quantity, hasCapacity, in capacity);
    }

    private static SuiteSpendDecision EvaluateStance(
        in SuiteResourceStance stance,
        in BigDouble cost,
        in BigDouble quantity,
        bool hasCapacity,
        in BigDouble capacity)
    {
        switch (stance.Kind)
        {
            case SuiteResourceStanceKind.Free:
                return new SuiteSpendDecision(SuiteSpendOutcome.Allowed);

            case SuiteResourceStanceKind.Embargo:
                return new SuiteSpendDecision(SuiteSpendOutcome.BlockedStrategyEmbargo);

            case SuiteResourceStanceKind.FloorAbsolute:
                return RequireRemaining(in cost, in quantity, stance.FloorAbsolute);

            case SuiteResourceStanceKind.FloorFraction:
                // A share of capacity means nothing on an uncapped resource; the strategist should
                // have used an absolute floor. Permit and report rather than invent a bound.
                if (!hasCapacity || IsNegative(capacity) || IsZero(capacity))
                    return new SuiteSpendDecision(SuiteSpendOutcome.AllowedStanceNotApplicable);
                return RequireRemaining(in cost, in quantity, capacity * Clamp01(stance.FloorFraction));

            case SuiteResourceStanceKind.TrivialOnly:
                // Guarded above: quantity >= cost > 0, so this cannot divide by zero.
                var share = (cost / quantity).ToDouble();
                return share <= Clamp01(stance.MaxSpendFraction)
                    ? new SuiteSpendDecision(SuiteSpendOutcome.Allowed)
                    : new SuiteSpendDecision(SuiteSpendOutcome.BlockedStrategyRatio);

            default:
                // An unrecognized stance is unknown policy, and unknown policy fails closed.
                return new SuiteSpendDecision(SuiteSpendOutcome.BlockedStrategyEmbargo);
        }
    }

    private static SuiteSpendDecision RequireRemaining(
        in BigDouble cost,
        in BigDouble quantity,
        in BigDouble floor) =>
        quantity.CompareTo(cost + floor) < 0
            ? new SuiteSpendDecision(SuiteSpendOutcome.BlockedStrategyFloor)
            : new SuiteSpendDecision(SuiteSpendOutcome.Allowed);

    private static double Clamp01(double value) =>
        double.IsNaN(value) ? 0d : Math.Min(1d, Math.Max(0d, value));

    private static bool IsZero(BigDouble value) => value.Mantissa == 0.0;

    private static bool IsNegative(BigDouble value) => value.Mantissa < 0.0;
}
