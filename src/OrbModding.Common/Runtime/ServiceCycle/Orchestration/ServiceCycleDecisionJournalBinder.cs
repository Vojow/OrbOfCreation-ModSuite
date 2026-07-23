using System;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;
using OrbModding.Common.Runtime.ServiceCycle.Registration;

namespace OrbModding.Common.Runtime.ServiceCycle.Orchestration;

internal static class ServiceCycleDecisionJournalBinder
{
    internal static bool TryBind(
        ServiceCycleRegistry registry,
        int ordinalCount,
        IServiceCycleDecisionJournalObserver observer,
        DecisionJournalServiceBaseline[] baselines)
    {
        if (registry is null) throw new ArgumentNullException(nameof(registry));
        if (observer is null) throw new ArgumentNullException(nameof(observer));
        if (baselines is null || baselines.Length != ordinalCount)
            throw new ArgumentException(
                "Decision-journal baselines must match the registry topology.",
                nameof(baselines));

        for (var ordinal = 0; ordinal < ordinalCount; ordinal++)
        {
            var slot = registry.GetSlot(ordinal);
            if (slot.IsDisposed)
                throw new InvalidOperationException("A disposed service cannot join the decision journal.");
            if (!slot.IsBetweenCycles) return false;

            var lifecycle = slot.LifecycleSnapshot;
            if (lifecycle.ActiveLifecycle.Value == 0 ||
                lifecycle.ActiveLifecycle != lifecycle.DesiredLifecycle ||
                lifecycle.LivePositionCount != 1 ||
                !slot.TryGetRunnerSnapshot(out var runner))
            {
                return false;
            }

            baselines[ordinal] = new DecisionJournalServiceBaseline(
                lifecycle.ActiveLifecycle,
                slot.LatestConfiguration,
                slot.LatestStrategy,
                runner.Fault,
                slot.LifecycleSemanticVersion,
                lifecycle.LatestTerminal.Sequence,
                lifecycle.LatestConstructionDeferral.Sequence);
        }

        for (var ordinal = 0; ordinal < ordinalCount; ordinal++)
        {
            var baseline = baselines[ordinal];
            observer.Bind(
                ordinal,
                baseline.Lifecycle,
                baseline.Configuration,
                baseline.Strategy,
                baseline.Fault,
                baseline.LifecycleSemanticVersion,
                baseline.LifecycleTerminalSequence,
                baseline.ConstructionDeferralSequence);
            if (observer.IsFaulted) return false;
        }

        return true;
    }
}
