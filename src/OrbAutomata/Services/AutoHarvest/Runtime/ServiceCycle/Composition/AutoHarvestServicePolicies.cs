using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class AutoHarvestServicePolicies
{
    private static readonly MonotonicDuration DefaultInterval =
        MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(1));
    private static readonly MonotonicDuration InitialFaultBackoff =
        MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(250));
    private static readonly MonotonicDuration MaximumFaultBackoff =
        MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(1));
    public static ServiceId ServiceId => new("orbautomata.auto-harvest");
    public static WakePolicy DefaultWakePolicy => WakePolicy.AfterBatch(DefaultInterval);
    public static ServiceFaultRecoveryPolicy FaultRecoveryPolicy =>
        new(InitialFaultBackoff, MaximumFaultBackoff);
}
