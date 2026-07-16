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
            return ReserveDecision.Rejected($"invalid AbsoluteReserve '{_config.AbsoluteReserve.Value}'");
        }

        var relativeMultiplier = Math.Max(0.0, _config.RelativeReserveMultiplier.Value);
        var maxRatio = 0.0;

        foreach (var cost in costs)
        {
            if (cost.Cost.IsNegative || cost.CurrentQuantity.IsNegative)
            {
                return ReserveDecision.Rejected($"negative cost or quantity for {cost.ResourceName}");
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
                return ReserveDecision.Rejected(
                    $"reserve violation for {cost.ResourceName}: have {cost.CurrentQuantity}, need {requiredBeforeSpend} including reserve floor {requiredFloor}");
            }

            var ratio = cost.Cost.DivideApprox(cost.CurrentQuantity);
            maxRatio = Math.Max(maxRatio, ratio);
        }

        var summary = string.Join("; ", costs.Select(cost => $"{cost.ResourceName} cost={cost.Cost} have={cost.CurrentQuantity}"));
        return ReserveDecision.Accepted(maxRatio, summary);
    }
}

internal sealed class ReserveDecision
{
    private ReserveDecision(bool passed, double maxCostToQuantityRatio, string reason)
    {
        Passed = passed;
        MaxCostToQuantityRatio = maxCostToQuantityRatio;
        Reason = reason;
    }

    public bool Passed { get; }

    public double MaxCostToQuantityRatio { get; }

    public string Reason { get; }

    public string Summary => Passed ? $"pass; maxCostRatio={MaxCostToQuantityRatio:0.###e+0}; {Reason}" : Reason;

    public static ReserveDecision Accepted(double maxCostToQuantityRatio, string summary)
    {
        return new ReserveDecision(true, maxCostToQuantityRatio, summary);
    }

    public static ReserveDecision Rejected(string reason)
    {
        return new ReserveDecision(false, double.PositiveInfinity, reason);
    }
}
