using System;
using OrbModding.Common;

namespace OrbAutomata;

internal sealed partial class AutoHarvestBindingResolver
{
    private bool TryResolveShared(out AutoHarvestNativeFailure failure)
    {
        if (_shared is not null)
        {
            if (_registryResolver.IsCurrent(_shared.ActiveResolution) &&
                _registryResolver.IsCurrent(_shared.ScalingResolution))
            {
                failure = default;
                return true;
            }
#if SERVICE_CYCLE_PROFILE
            _profileTemperature.ObserveUnexpectedDrift();
#endif
        }

        try
        {
            _types ??= AutoHarvestReflectionTypes.Discover();
            var activeActions = Resolve(
                KnownEntities.ActivePlotNodeActions.Uuid,
                _types.ActiveActions,
                KnownEntities.ActivePlotNodeActions.DiagnosticName,
                out var activeResolution);
            var completionScalingWeight = Resolve(
                KnownEntities.CompletionScalingWeight.Uuid,
                _types.ScalingWeight,
                KnownEntities.CompletionScalingWeight.DiagnosticName,
                out var scalingResolution);
            RequireCurrentGeneration(
                activeResolution.LifecycleGeneration,
                activeResolution,
                scalingResolution);
            _contract ??= AutoHarvestReflectionContract.Bind(_types);
            _shared = new AutoHarvestSharedBinding(
                activeActions,
                completionScalingWeight,
                activeResolution,
                scalingResolution,
                activeResolution.LifecycleGeneration);
            failure = default;
            return true;
        }
        catch (AutoHarvestRegistryNotReadyException)
        {
            failure = AutoHarvestNativeFailure.Create(
                AutoHarvestRuntimeFailureKind.Retryable,
                AutoHarvestRuntimeFailureScope.Feature);
            return false;
        }
        catch (Exception ex) when (AutoHarvestReflectionAccess.IsExpectedFailure(ex))
        {
            var kind = AutoHarvestReflectionAccess.ClassifyExpectedFailure(ex);
            failure = AutoHarvestNativeFailure.Create(
                kind,
                AutoHarvestRuntimeFailureScope.Feature);
            if (kind == AutoHarvestRuntimeFailureKind.Contract)
            {
                _contractCircuit.Block(
                    AutoHarvestPair.FruitTree,
                    AutoHarvestRuntimeFailureScope.Feature);
            }
            return false;
        }
    }

    private object Resolve(
        Guid uuid,
        Type type,
        string name,
        out TypedRegistryResolution resolution)
    {
        resolution = _registryResolver.Resolve(uuid, type);
#if SERVICE_CYCLE_PROFILE
        if (resolution.IsResolved) _profileOperations.AddStableIdRead();
#endif
        if (!resolution.IsResolved)
        {
            var message = $"{name} registry identity is unavailable: {resolution.Format()}";
            if (resolution.IsRetryable) throw new AutoHarvestRegistryNotReadyException(message);
            throw new InvalidOperationException(message);
        }
        return resolution.Value!;
    }

    private static void RequireCurrentGeneration(
        long expectedGeneration,
        TypedRegistryResolution first,
        TypedRegistryResolution second)
    {
        RequireCurrentGeneration(expectedGeneration, first);
        RequireCurrentGeneration(expectedGeneration, second);
    }

    private static void RequireCurrentGeneration(
        long expectedGeneration,
        TypedRegistryResolution resolution)
    {
        if (resolution.LifecycleGeneration != expectedGeneration)
        {
            throw new AutoHarvestRegistryNotReadyException(
                "native Auto Harvest identities crossed lifecycle generations");
        }
    }
}
