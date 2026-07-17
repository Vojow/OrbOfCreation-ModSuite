using System;
using System.Collections.Generic;
using System.Linq;

namespace OrbAutomata;

internal sealed class ReservePolicy
{
    private readonly AutomataConfig _config;

    public ReservePolicy(AutomataConfig config)
    {
        _config = config;
    }

    public ReserveDecision Evaluate(IReadOnlyList<ResourceAdmissionCost> costs)
    {
        if (costs.Count == 0)
        {
            return ReserveDecision.Accepted(0.0, "native cost list is empty");
        }

        if (!BigAmount.TryParse(_config.AbsoluteReserve.Value, out var absoluteReserve))
        {
            return ReserveDecision.Rejected(
                ReserveDecisionFailure.InvalidPolicy,
                $"invalid AbsoluteReserve '{_config.AbsoluteReserve.Value}'");
        }

        var relativeMultiplier = Math.Max(0.0, _config.RelativeReserveMultiplier.Value);
        var maxRatio = 0.0;
        List<AutoBuyResourceBlocker>? blockers = null;

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
                blockers ??= new List<AutoBuyResourceBlocker>();
                blockers.Add(new AutoBuyResourceBlocker(
                    AutoBuyResourceBlockerKind.ReserveFloor,
                    cost.ResourceId,
                    cost.ResourceName,
                    cost.Cost,
                    cost.CurrentQuantity,
                    requiredBeforeSpend));
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
                    $"reserve violation for {blocker.ResourceName}: have {blocker.AvailableQuantity}, " +
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

internal sealed class ReserveDecision
{
    private ReserveDecision(
        bool passed,
        double maxCostToQuantityRatio,
        string reason,
        ReserveDecisionFailure failure,
        IReadOnlyList<AutoBuyResourceBlocker> resourceBlockers)
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

    public IReadOnlyList<AutoBuyResourceBlocker> ResourceBlockers { get; }

    public string Summary => Passed ? $"pass; maxCostRatio={MaxCostToQuantityRatio:0.###e+0}; {Reason}" : Reason;

    public static ReserveDecision Accepted(double maxCostToQuantityRatio, string summary)
    {
        return new ReserveDecision(
            true,
            maxCostToQuantityRatio,
            summary,
            ReserveDecisionFailure.None,
            Array.Empty<AutoBuyResourceBlocker>());
    }

    public static ReserveDecision Rejected(
        ReserveDecisionFailure failure,
        string reason,
        IReadOnlyList<AutoBuyResourceBlocker>? resourceBlockers = null)
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
            resourceBlockers ?? Array.Empty<AutoBuyResourceBlocker>());
    }
}
