using System;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;

namespace OrbAutomata;

/// <summary>
/// Host-level dependencies for the one Automata ServiceCycle runtime. These are the
/// suite-wide seams (frame identity, native lifecycle epoch, pump timing, and
/// observability) that belong to the shared host rather than to any one feature. Feature-specific seams (registry resolver, action-family ownership,
/// mutation permit, feature status, runtime diagnostics) stay with each feature
/// contribution.
/// </summary>
internal sealed class AutomataServiceCycleHostDependencies
{
    public AutomataServiceCycleHostDependencies(
        Func<long> readFrameIdentity,
        Func<long> readLifecycleEpoch,
        IServiceCyclePumpTimingSink? pumpTiming = null,
        AutomataServiceCycleObservabilityOptions observability = default)
    {
        ReadFrameIdentity = readFrameIdentity ?? throw new ArgumentNullException(nameof(readFrameIdentity));
        ReadLifecycleEpoch = readLifecycleEpoch ?? throw new ArgumentNullException(nameof(readLifecycleEpoch));
        PumpTiming = pumpTiming;
        Observability = observability;
    }

    public Func<long> ReadFrameIdentity { get; }
    public Func<long> ReadLifecycleEpoch { get; }
    public IServiceCyclePumpTimingSink? PumpTiming { get; }
    internal AutomataServiceCycleObservabilityOptions Observability { get; }
}
