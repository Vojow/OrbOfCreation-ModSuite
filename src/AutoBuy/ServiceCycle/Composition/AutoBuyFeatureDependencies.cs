using System;
using OrbModding.Common;
using OrbModding.Common.Runtime;

namespace OrbAutomata;

/// <summary>
/// Feature-scoped dependencies for the Auto Buy ServiceCycle contribution. Only the seams the
/// feature owns live here — the live native lifecycle epoch, the candidate-kind ownership this
/// instance is responsible for, and its diagnostics outputs. The
/// suite-wide seams (frame identity, pump timing, observability) belong to
/// <see cref="AutomataServiceCycleHostDependencies"/>.
/// </summary>
internal sealed class AutoBuyFeatureDependencies
{
    public AutoBuyFeatureDependencies(
        Func<long> readLifecycleEpoch,
        Func<AutoBuyCandidateKinds> ownershipMask,
        RuntimeDiagnosticsRegistry? runtimeDiagnostics = null,
        AutomataFeatureStatusReporter? featureStatus = null,
        IAutoBuyRefusalResponsePort? refusalResponse = null)
    {
        ReadLifecycleEpoch = readLifecycleEpoch ?? throw new ArgumentNullException(nameof(readLifecycleEpoch));
        OwnershipMask = ownershipMask ?? throw new ArgumentNullException(nameof(ownershipMask));
        RuntimeDiagnostics = runtimeDiagnostics;
        FeatureStatus = featureStatus;
        RefusalResponse = refusalResponse;
    }

    public Func<long> ReadLifecycleEpoch { get; }
    public Func<AutoBuyCandidateKinds> OwnershipMask { get; }

    public RuntimeDiagnosticsRegistry? RuntimeDiagnostics { get; }
    public AutomataFeatureStatusReporter? FeatureStatus { get; }

    /// <summary>
    /// What the suite does when the game refuses a purchase the worker planned. Absent in a
    /// composition that owns no configuration to stand down — the boundary still narrates and still
    /// rejects.
    /// </summary>
    public IAutoBuyRefusalResponsePort? RefusalResponse { get; }
}
