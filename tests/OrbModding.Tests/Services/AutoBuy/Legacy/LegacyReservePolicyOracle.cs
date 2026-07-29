using System;
using System.Collections.Generic;
using System.Linq;
using OrbModding.Common.Runtime.Configuration;

namespace OrbAutomata;

/// <summary>
/// The reserve arithmetic the retired legacy runtimes shared, kept verbatim as a numeric oracle.
/// </summary>
/// <remarks>
/// This is no longer production code. Auto Buy and Auto Cast each compute their own reserve floor
/// against the published world in <c>BigDouble</c>, and this <c>BigAmount</c> original is what those
/// ports were checked against. It lives in the test assembly because a type whose only remaining
/// consumer is a parity test belongs with the test rather than shipping inside the plugin — and it is
/// kept rather than deleted because "the new arithmetic still agrees with the old" is a claim worth
/// being able to re-check after any change to either.
/// </remarks>
internal sealed class ReservePolicy
{
    private readonly Func<SuiteRuntimeConfiguration> _readConfig;

    public ReservePolicy(Func<SuiteRuntimeConfiguration> readConfig)
    {
        _readConfig = readConfig;
    }

    private SuiteRuntimeConfiguration Config => _readConfig();

    public ReserveDecision Evaluate(IReadOnlyList<ResourceAdmissionCost> costs)
    {
        if (costs.Count == 0)
        {
            return ReserveDecision.Accepted(0.0, "native cost list is empty");
        }

        if (!BigAmount.TryParse(Config.Reserves.AbsoluteReserve, out var absoluteReserve))
        {
            return ReserveDecision.Rejected(
                ReserveDecisionFailure.InvalidPolicy,
                $"invalid AbsoluteReserve '{Config.Reserves.AbsoluteReserve}'");
        }

        var relativeMultiplier = Math.Max(0.0, Config.Reserves.RelativeReserveMultiplier);
        var maxRatio = 0.0;
        List<ResourceReserveBlocker>? blockers = null;

        foreach (var cost in costs)
        {
            if (cost.Cost.IsNegative || cost.CurrentQuantity.IsNegative)
            {
                return ReserveDecision.Rejected(
                    ReserveDecisionFailure.InvalidResourceSnapshot,
                    $"negative cost or quantity for {cost.ResourceName}");
            }

            if (cost.Cost.IsZero)
            {
                continue;
            }

            var relativeReserve = cost.Cost.Multiply(relativeMultiplier);
            var requiredFloor = BigAmount.Max(absoluteReserve, relativeReserve);
            var requiredBeforeSpend = cost.Cost.Add(requiredFloor);
            if (cost.CurrentQuantity.CompareTo(requiredBeforeSpend) < 0)
            {
                blockers ??= new List<ResourceReserveBlocker>();
                blockers.Add(new ResourceReserveBlocker(
                    cost.ResourceId,
                    cost.ResourceName,
                    cost.Cost,
                    cost.CurrentQuantity,
                    requiredBeforeSpend,
                    cost.IsBandwidth));
                continue;
            }

            var ratio = cost.Cost.DivideApprox(cost.CurrentQuantity);
            maxRatio = Math.Max(maxRatio, ratio);
        }

        if (blockers is not null)
        {
            var reason = string.Join(
                "; ",
                blockers.Select(blocker =>
                    blocker.RequiredQuantity.CompareTo(blocker.Cost) == 0
                        ? $"insufficient {blocker.ResourceName}: have {blocker.AvailableQuantity}, " +
                          $"need {blocker.RequiredQuantity} to cover cost"
                        : $"reserve violation for {blocker.ResourceName}: have {blocker.AvailableQuantity}, " +
                          $"need {blocker.RequiredQuantity} including reserve"));
            return ReserveDecision.Rejected(ReserveDecisionFailure.ReserveViolation, reason, blockers);
        }

        var summary = string.Join("; ", costs.Select(cost => $"{cost.ResourceName} cost={cost.Cost} have={cost.CurrentQuantity}"));
        return ReserveDecision.Accepted(maxRatio, summary);
    }
}

internal enum ReserveDecisionFailure
{
    None,
    InvalidPolicy,
    InvalidResourceSnapshot,
    ReserveViolation,
}

internal readonly struct ResourceReserveBlocker
{
    public ResourceReserveBlocker(
        string resourceId,
        string resourceName,
        BigAmount cost,
        BigAmount availableQuantity,
        BigAmount requiredQuantity,
        bool isBandwidth)
    {
        ResourceId = resourceId;
        ResourceName = resourceName;
        Cost = cost;
        AvailableQuantity = availableQuantity;
        RequiredQuantity = requiredQuantity;
        IsBandwidth = isBandwidth;
    }

    public string ResourceId { get; }
    public string ResourceName { get; }
    public BigAmount Cost { get; }
    public BigAmount AvailableQuantity { get; }
    public BigAmount RequiredQuantity { get; }
    public bool IsBandwidth { get; }
}

internal sealed class ReserveDecision
{
    private ReserveDecision(
        bool passed,
        double maxCostToQuantityRatio,
        string reason,
        ReserveDecisionFailure failure,
        IReadOnlyList<ResourceReserveBlocker> resourceBlockers)
    {
        Passed = passed;
        MaxCostToQuantityRatio = maxCostToQuantityRatio;
        Reason = reason;
        Failure = failure;
        ResourceBlockers = resourceBlockers;
    }

    public bool Passed { get; }

    public double MaxCostToQuantityRatio { get; }

    public string Reason { get; }

    public ReserveDecisionFailure Failure { get; }

    public IReadOnlyList<ResourceReserveBlocker> ResourceBlockers { get; }

    public string Summary => Passed ? $"pass; maxCostRatio={MaxCostToQuantityRatio:0.###e+0}; {Reason}" : Reason;

    public static ReserveDecision Accepted(double maxCostToQuantityRatio, string summary)
    {
        return new ReserveDecision(
            true,
            maxCostToQuantityRatio,
            summary,
            ReserveDecisionFailure.None,
            Array.Empty<ResourceReserveBlocker>());
    }

    public static ReserveDecision Rejected(
        ReserveDecisionFailure failure,
        string reason,
        IReadOnlyList<ResourceReserveBlocker>? resourceBlockers = null)
    {
        if (failure == ReserveDecisionFailure.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failure));
        }

        return new ReserveDecision(
            false,
            double.PositiveInfinity,
            reason,
            failure,
            resourceBlockers ?? Array.Empty<ResourceReserveBlocker>());
    }
}
