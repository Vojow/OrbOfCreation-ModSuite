using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class AutoCastServicePolicies
{
    private static readonly MonotonicDuration InitialFaultBackoff =
        MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(250));
    private static readonly MonotonicDuration MaximumFaultBackoff =
        MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(10));

    public static ServiceId ServiceId => new("orbautomata.auto-cast");

    public static WakePolicy DefaultWakePolicy => WakePolicy.OnPublication;

    // A cast fault is nearly always a contract that will not come back before the next lifecycle, so
    // the ceiling climbs to ten seconds rather than Auto Buy's one. That keeps a permanently blocked
    // runtime from re-probing every second forever.
    public static ServiceFaultRecoveryPolicy FaultRecoveryPolicy =>
        new(InitialFaultBackoff, MaximumFaultBackoff);
}
