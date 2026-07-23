using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using ArtifactCodecRole = OrbModding.Common.Runtime.ServiceCycle.Replay.Format.ServiceCycleReplayCodecRole;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

/// <summary>
/// Validates the complete production topology before any feature-owned factory is invoked. Detached
/// evaluator verification deliberately remains independent and may operate on sparse artifacts.
/// </summary>
internal static class ServiceCycleReplayProductionPreflight
{
    internal static ServiceCycleReplayExecutionResult? Validate(
        ServiceCycleReplayProductionArtifactPlan plan,
        IServiceCycleReplayExecutionRegistration?[] registrations)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        var artifact = plan.Artifact;
        if (artifact is null) throw new ArgumentNullException(nameof(artifact));
        if (registrations is null) throw new ArgumentNullException(nameof(registrations));
        var fallback = TryFirstCycle(artifact, out var first)
            ? first
            : StableFailureCycle;
        if (!artifact.IsComplete)
            return Fault(fallback, ServiceCycleReplayExecutionDetailCode.ArtifactNotComplete);

        var capacity = plan.Capacity;
        if (capacity <= 0)
            return Fault(fallback, ServiceCycleReplayExecutionDetailCode.RegistrationMissing);
        if (capacity > int.MaxValue / 3 || artifact.CodecCount != capacity * 3 ||
            !HasExactCodecCoverage(plan, capacity))
            return Fault(fallback, ServiceCycleReplayExecutionDetailCode.CodecDescriptorRejected);
        if (!HasExactRegistrationCoverage(registrations, capacity, out var missingKey))
            return Fault(
                FirstCycle(artifact, missingKey, fallback),
                missingKey > 0
                    ? ServiceCycleReplayExecutionDetailCode.RegistrationMissing
                    : ServiceCycleReplayExecutionDetailCode.RegistrationKeyGap);
        // Production reconstruction currently hydrates each service's initial configuration from
        // its first detached cycle input. A codec-only dormant service has no authoritative value to
        // hydrate, so it is detached-oracle compatible but not production-replayable.
        for (var traceServiceKey = 1; traceServiceKey <= capacity; traceServiceKey++)
            if (plan.ServiceCycleCount(traceServiceKey) == 0)
                return Fault(fallback, ServiceCycleReplayExecutionDetailCode.CaptureEvidenceMissing);

        if (plan.UnsupportedLifecycleConstruction.IsValid)
            return Fault(
                plan.UnsupportedLifecycleConstruction,
                ServiceCycleReplayExecutionDetailCode.LifecycleConstructionEvidenceUnsupported);
        return null;
    }

    private static readonly ServiceCycleReplayCycleKey StableFailureCycle =
        new(1, 1, 1, 1, 1, 1);

    private static bool TryFirstCycle(
        ServiceCycleReplayArtifactDocument artifact,
        out ServiceCycleReplayCycleKey cycle)
    {
        if (artifact.CycleCount != 0)
        {
            cycle = artifact.GetCycle(0).Key;
            return true;
        }
        cycle = default;
        return false;
    }

    internal static ServiceCycleReplayCycleKey FirstCycle(
        ServiceCycleReplayArtifactDocument artifact,
        int traceServiceKey = 0,
        ServiceCycleReplayCycleKey fallback = default)
    {
        for (var index = 0; index < artifact.CycleCount; index++)
        {
            var cycle = artifact.GetCycle(index).Key;
            if (traceServiceKey <= 0 || cycle.TraceServiceKey == traceServiceKey) return cycle;
        }
        if (fallback.IsValid) return fallback;
        return StableFailureCycle;
    }

    private static bool HasExactCodecCoverage(ServiceCycleReplayProductionArtifactPlan plan, int capacity)
    {
        for (var key = 1; key <= capacity; key++)
            if (!plan.HasExactCodecTriplet(key)) return false;
        return true;
    }

    private static bool HasExactRegistrationCoverage(
        IServiceCycleReplayExecutionRegistration?[] registrations,
        int capacity,
        out int missingKey)
    {
        missingKey = 0;
        if (registrations.Length < capacity)
        {
            missingKey = registrations.Length + 1;
            return false;
        }
        for (var index = 0; index < capacity; index++)
        {
            var registration = registrations[index];
            if (registration is null)
            {
                missingKey = index + 1;
                return false;
            }
            if (registration.TraceServiceKey != index + 1) return false;
        }
        for (var index = capacity; index < registrations.Length; index++)
            if (registrations[index] is not null) return false;
        return true;
    }

    private static ServiceCycleReplayExecutionResult Fault(
        ServiceCycleReplayCycleKey cycle,
        ServiceCycleReplayExecutionDetailCode detail) =>
        ServiceCycleReplayProductionResult.Fault(cycle, detail);
}
