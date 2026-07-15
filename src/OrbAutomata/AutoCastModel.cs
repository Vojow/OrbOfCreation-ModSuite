using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OrbAutomata;

internal enum AutoCastSpellKind
{
    Instant,
    Aura,
    Channel,
}

internal interface IAutoCastCatalog : IDisposable
{
    IReadOnlyList<IAutoCastCandidate> DiscoverActiveLoadout();

    bool IsNativeCastBusy();

    bool IsTargeting();
}

internal interface IAutoCastCandidate
{
    int SlotIndex { get; }

    string DisplayName { get; }

    AutoCastSpellKind Kind { get; }

    bool IsEmpty { get; }

    bool IsCharged { get; }

    bool IsCasting { get; }

    bool CanCast(out string reason);

    bool TryGetImmediateCosts(out IReadOnlyList<ResourceAdmissionCost> costs);

    bool TryGetDrainCosts(out IReadOnlyList<ResourceAdmissionCost> costs);

    bool HasValidTargets(out string reason);

    bool TryFireAndResolveTargets(out string reason);
}

internal sealed class ResourceFullnessPolicy
{
    public bool Evaluate(
        IEnumerable<ResourceAdmissionCost> immediateCosts,
        IEnumerable<ResourceAdmissionCost> drainCosts,
        double minimumPercent,
        out string reason)
    {
        var minimumRatio = Math.Max(0.0, Math.Min(100.0, minimumPercent)) / 100.0;
        foreach (var resource in Combine(immediateCosts, drainCosts))
        {
            if (resource.Cost.IsNegative || resource.CurrentQuantity.IsNegative)
            {
                reason = $"negative cost or quantity for {resource.ResourceName}";
                return false;
            }

            if (!resource.Capacity.HasValue || resource.Capacity.Value.IsZero || resource.Capacity.Value.IsNegative)
            {
                continue;
            }

            var ratio = resource.CurrentQuantity.DivideApprox(resource.Capacity.Value);
            if (double.IsNaN(ratio) || ratio < minimumRatio)
            {
                reason =
                    $"{resource.ResourceName} is below the start threshold: " +
                    $"current={resource.CurrentQuantity}, capacity={resource.Capacity.Value}, " +
                    $"fullness={FormatPercent(ratio)}, required={FormatPercent(minimumRatio)}";
                return false;
            }
        }

        reason = "resource fullness passed";
        return true;
    }

    public string Describe(
        IEnumerable<ResourceAdmissionCost> immediateCosts,
        IEnumerable<ResourceAdmissionCost> drainCosts,
        double minimumPercent)
    {
        var resources = immediateCosts
            .Select(cost => Describe(cost, "immediate"))
            .Concat(drainCosts.Select(cost => Describe(cost, "drain")))
            .ToArray();
        var threshold = Math.Max(0.0, Math.Min(100.0, minimumPercent)) / 100.0;
        return $"Resources={(resources.Length == 0 ? "none" : string.Join(", ", resources))}; " +
               $"StartThreshold={FormatPercent(threshold)}";
    }

    private static string Describe(ResourceAdmissionCost resource, string costKind)
    {
        if (!resource.Capacity.HasValue || resource.Capacity.Value.IsZero || resource.Capacity.Value.IsNegative)
        {
            return $"{resource.ResourceName} {costKind} cost={resource.Cost} " +
                   $"current={resource.CurrentQuantity} capacity=unbounded fullness=n/a";
        }

        var ratio = resource.CurrentQuantity.DivideApprox(resource.Capacity.Value);
        return $"{resource.ResourceName} {costKind} cost={resource.Cost} " +
               $"current={resource.CurrentQuantity} capacity={resource.Capacity.Value} " +
               $"fullness={FormatPercent(ratio)}";
    }

    private static string FormatPercent(double ratio)
    {
        return double.IsNaN(ratio)
            ? "n/a"
            : ratio.ToString("P1", CultureInfo.InvariantCulture);
    }

    private static IEnumerable<ResourceAdmissionCost> Combine(
        IEnumerable<ResourceAdmissionCost> immediateCosts,
        IEnumerable<ResourceAdmissionCost> drainCosts)
    {
        foreach (var cost in immediateCosts)
        {
            yield return cost;
        }

        foreach (var cost in drainCosts)
        {
            yield return cost;
        }
    }
}
