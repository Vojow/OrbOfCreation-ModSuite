using System;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;

namespace OrbAutomata;

internal sealed class AutoHarvestServiceCycleDependencies
{
    public AutoHarvestServiceCycleDependencies(
        Func<long> readFrameIdentity,
        Func<long> readLifecycleEpoch,
        TypedRegistryResolver registryResolver,
        Func<bool> ownsActionFamily,
        Func<bool> tryCaptureMutationPermit,
        IServiceCyclePumpTimingSink? pumpTiming = null,
        RuntimeDiagnosticsRegistry? runtimeDiagnostics = null,
        AutomataFeatureStatusReporter? featureStatus = null,
        AutomataReplayCaptureOptions replay = default,
        AutomataServiceCycleObservabilityOptions observability = default)
    {
        ReadFrameIdentity = readFrameIdentity ?? throw new ArgumentNullException(nameof(readFrameIdentity));
        ReadLifecycleEpoch = readLifecycleEpoch ?? throw new ArgumentNullException(nameof(readLifecycleEpoch));
        RegistryResolver = registryResolver ?? throw new ArgumentNullException(nameof(registryResolver));
        OwnsActionFamily = ownsActionFamily ?? throw new ArgumentNullException(nameof(ownsActionFamily));
        TryCaptureMutationPermit = tryCaptureMutationPermit ??
            throw new ArgumentNullException(nameof(tryCaptureMutationPermit));
        PumpTiming = pumpTiming;
        RuntimeDiagnostics = runtimeDiagnostics;
        FeatureStatus = featureStatus;
        Replay = replay;
        Observability = observability;
    }

    public Func<long> ReadFrameIdentity { get; }
    public Func<long> ReadLifecycleEpoch { get; }
    public TypedRegistryResolver RegistryResolver { get; }
    public Func<bool> OwnsActionFamily { get; }
    public Func<bool> TryCaptureMutationPermit { get; }
    public IServiceCyclePumpTimingSink? PumpTiming { get; }
    public RuntimeDiagnosticsRegistry? RuntimeDiagnostics { get; }
    public AutomataFeatureStatusReporter? FeatureStatus { get; }
    public AutomataReplayCaptureOptions Replay { get; }
    internal AutomataServiceCycleObservabilityOptions Observability { get; }
}
