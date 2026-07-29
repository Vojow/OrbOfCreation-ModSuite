using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class AutoBuyServicePolicies
{
    private static readonly MonotonicDuration DefaultInterval =
        MonotonicDuration.FromTimeSpan(
            TimeSpan.FromSeconds(AutoBuyConfigurationPolicy.DefaultEvaluationIntervalSeconds));
    private static readonly MonotonicDuration InitialFaultBackoff =
        MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(250));
    private static readonly MonotonicDuration MaximumFaultBackoff =
        MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(1));
    public static ServiceId ServiceId => new("orbautomata.auto-buy");
    // AfterDecision anchors the next capture on THIS capture's time (absorbing however long the
    // action batch took to process), giving a fixed cadence measured from tick start — not from
    // batch end. This is the metadata fallback; each cycle the worker returns
    // AfterDecision(EvaluationInterval(config)) with the live configured interval.
    public static WakePolicy DefaultWakePolicy => WakePolicy.AfterDecision(DefaultInterval);
    public static ServiceFaultRecoveryPolicy FaultRecoveryPolicy =>
        new(InitialFaultBackoff, MaximumFaultBackoff);
}
