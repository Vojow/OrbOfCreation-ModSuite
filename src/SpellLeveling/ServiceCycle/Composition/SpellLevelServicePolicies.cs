using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class SpellLevelServicePolicies
{
    private static readonly MonotonicDuration DefaultInterval =
        MonotonicDuration.FromTimeSpan(
            TimeSpan.FromSeconds(SpellLevelConfigurationPolicy.DefaultEvaluationIntervalSeconds));
    private static readonly MonotonicDuration InitialFaultBackoff =
        MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(250));
    private static readonly MonotonicDuration MaximumFaultBackoff =
        MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(10));

    public static ServiceId ServiceId => new("orbautomata.spell-level");

    // AfterDecision anchors the next capture on this capture's time, so the cadence is measured from
    // tick start rather than from batch end. The metadata fallback only covers the cycles before the
    // worker has run once; every cycle after returns AfterDecision with the live configured interval.
    public static WakePolicy DefaultWakePolicy => WakePolicy.AfterDecision(DefaultInterval);

    // A spell-level fault is nearly always a contract that will not come back before the next
    // lifecycle, so the ceiling climbs to ten seconds rather than Auto Buy's one. That keeps a
    // permanently blocked runtime from re-probing every second forever.
    public static ServiceFaultRecoveryPolicy FaultRecoveryPolicy =>
        new(InitialFaultBackoff, MaximumFaultBackoff);
}
