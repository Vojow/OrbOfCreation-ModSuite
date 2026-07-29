using System;
using System.Reflection;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

/// <summary>
/// Revalidates a worker-selected target against an immediate world read and
/// owns the feature's only native level mutation.
/// </summary>
internal sealed class AutoAgromancyCycleActionAdapter : IAutoAgromancyCycleActionPort
{
    private readonly IAutoAgromancyExactNativeMutator _native;
    private readonly IAutoAgromancyLiveWorldReader _world;
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<bool> _ownsActionFamily;
    private readonly Func<bool> _tryCaptureMutationPermit;
    private readonly Func<SuiteRuntimeConfiguration> _readConfiguration;
    private readonly Func<ConfigGeneration> _readConfigurationGeneration;
    private bool _mutationQuarantined;

    internal AutoAgromancyCycleActionAdapter(
        IAutoAgromancyExactNativeMutator native,
        IAutoAgromancyLiveWorldReader world,
        Func<long> readLifecycleEpoch,
        Func<bool> ownsActionFamily,
        Func<bool> tryCaptureMutationPermit,
        Func<SuiteRuntimeConfiguration> readConfiguration,
        Func<ConfigGeneration> readConfigurationGeneration)
    {
        _native = native ?? throw new ArgumentNullException(nameof(native));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _readLifecycleEpoch =
            readLifecycleEpoch ?? throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _ownsActionFamily =
            ownsActionFamily ?? throw new ArgumentNullException(nameof(ownsActionFamily));
        _tryCaptureMutationPermit = tryCaptureMutationPermit ??
            throw new ArgumentNullException(nameof(tryCaptureMutationPermit));
        _readConfiguration =
            readConfiguration ?? throw new ArgumentNullException(nameof(readConfiguration));
        _readConfigurationGeneration = readConfigurationGeneration ??
            throw new ArgumentNullException(nameof(readConfigurationGeneration));
    }

    internal void InvalidateLifecycle() => _mutationQuarantined = false;

    public ServiceActionResult TryExecute(
        in AutoAgromancyCycleAction action,
        in SuiteRuntimeConfiguration configuration,
        in ServiceActionContext context)
    {
        if (_mutationQuarantined)
            return ServiceActionResult.Rejected(
                AutoAgromancyActionResultCodes.MutationQuarantined);

        SuiteRuntimeConfiguration liveConfiguration;
        ConfigGeneration liveGeneration;
        long lifecycle;
        try
        {
            liveConfiguration = _readConfiguration();
            liveGeneration = _readConfigurationGeneration();
            lifecycle = _readLifecycleEpoch();
        }
        catch (Exception exception) when (
            exception is TargetInvocationException or ArgumentException or
            InvalidOperationException or TargetException or MemberAccessException)
        {
            return ServiceActionResult.Faulted(CommonActionResultCodes.AdapterFault);
        }

        if (!AutoAgromancyConfigurationPolicy.IsOperational(in liveConfiguration))
            return ServiceActionResult.Rejected(CommonActionResultCodes.ServiceDisabled);
        if (liveGeneration != context.Cycle.Config)
            return ServiceActionResult.Rejected(
                AutoAgromancyActionResultCodes.LiveConfigurationChanged);
        if (lifecycle <= 0 || lifecycle != action.CollectedAtEpoch)
            return ServiceActionResult.Rejected(CommonActionResultCodes.LifecycleReplaced);
        if (!Owns())
            return ServiceActionResult.Rejected(
                AutoAgromancyActionResultCodes.ActionFamilyUnavailable);
        if (!TryCaptureMutationPermit())
            return ServiceActionResult.Rejected(
                AutoAgromancyActionResultCodes.ActionFamilyUnavailable);
        if (!_world.TryRead(lifecycle, out var before) ||
            !WorldHarvestActionLookup.TryFind(
                before.HarvestActions, action.ActionId, action.ElementId, out var pair))
            return ServiceActionResult.Rejected(
                AutoAgromancyActionResultCodes.PairUnavailable);
        if (pair.CurrentLevel != action.ObservedLevel ||
            pair.MaximumLevel != action.MaximumLevel ||
            !pair.Visible ||
            !AutoAgromancyPlanningProjection.TryBuildFingerprint(
                before, in pair, out var fingerprint) ||
            !fingerprint.Equals(action.Fingerprint))
            return ServiceActionResult.Rejected(
                AutoAgromancyActionResultCodes.LiveFactsChanged);

        var mutation = _native.ApplyExactTarget(
            action.ActionId,
            action.ElementId,
            action.ObservedLevel,
            action.TargetLevel);
        if (mutation.Disposition == AutoAgromancyExactMutationDisposition.Rejected)
            return ServiceActionResult.Rejected(CommonActionResultCodes.NativeRejected);
        if (mutation.Disposition == AutoAgromancyExactMutationDisposition.ContractUnavailable)
            return ServiceActionResult.Faulted(CommonActionResultCodes.AdapterFault);
        if (mutation.Disposition == AutoAgromancyExactMutationDisposition.AttemptedUnverified)
        {
            _mutationQuarantined = true;
            return AttemptedFault(1);
        }

        if (!_world.TryRead(lifecycle, out var after) ||
            !LevelMatches(after, in action) ||
            action.TargetLevel > action.ObservedLevel &&
            !ConsumedRatesAreSafe(before, after, in action))
        {
            var rollback = _native.ApplyExactTarget(
                action.ActionId,
                action.ElementId,
                action.TargetLevel,
                action.ObservedLevel);
            if (rollback.Disposition != AutoAgromancyExactMutationDisposition.Committed ||
                !_world.TryRead(lifecycle, out var restored) ||
                !LevelMatches(restored, action.ActionId, action.ElementId, action.ObservedLevel))
            {
                _mutationQuarantined = true;
                return AttemptedFault(2);
            }
            return ServiceActionResult.Faulted(
                AutoAgromancyActionResultCodes.SafetyRollback,
                ServiceNativeMutationEvidence.Observed(
                    NativeMutationOutcome.PostconditionFailed,
                    new NativeMutationCallOutcome(2, 2, 0)));
        }

        return ServiceActionResult.Committed(
            CommonActionResultCodes.Committed,
            ServiceNativeMutationEvidence.Observed(
                NativeMutationOutcome.Verified,
                new NativeMutationCallOutcome(1, 1, 1)));
    }

    private static bool LevelMatches(
        GameWorldState world,
        in AutoAgromancyCycleAction action) =>
        LevelMatches(
            world,
            action.ActionId,
            action.ElementId,
            action.TargetLevel);

    private static bool LevelMatches(
        GameWorldState world,
        Guid actionId,
        Guid elementId,
        int level)
    {
        var found = WorldHarvestActionLookup.TryFind(
            world.HarvestActions, actionId, elementId, out var pair);
        return found && pair.CurrentLevel == level;
    }

    private static bool ConsumedRatesAreSafe(
        GameWorldState before,
        GameWorldState after,
        in AutoAgromancyCycleAction action)
    {
        if (!WorldHarvestActionLookup.TryFindCosts(
                before.HarvestActionCosts,
                action.ActionId,
                action.ElementId,
                WorldHarvestActionCostKind.Base,
                out var start,
                out var count))
            return false;
        for (var index = 0; index < count; index++)
        {
            var resourceId = before.HarvestActionCosts[start + index].ResourceId;
            if (!TryFindResource(after, resourceId, out var resource) ||
                resource.TrueRate < BigDouble.Zero)
                return false;
        }
        return true;
    }

    private static bool TryFindResource(
        GameWorldState world,
        Guid resourceId,
        out WorldResource resource)
    {
        if (WorldLookup.TryFind(world.Resources, resourceId, out resource))
            return true;
        if (WorldLookup.TryFind(world.HarvestResources, resourceId, out var harvest))
        {
            resource = harvest.Resource;
            return true;
        }
        resource = default;
        return false;
    }

    private bool Owns()
    {
        try
        {
            return _ownsActionFamily();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or MemberAccessException)
        {
            return false;
        }
    }

    private bool TryCaptureMutationPermit()
    {
        try
        {
            return _tryCaptureMutationPermit();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or MemberAccessException)
        {
            return false;
        }
    }

    private static ServiceActionResult AttemptedFault(int attempts) =>
        ServiceActionResult.Faulted(
            CommonActionResultCodes.AdapterFault,
            ServiceNativeMutationEvidence.Observed(
                NativeMutationOutcome.PostconditionFailed,
                new NativeMutationCallOutcome(attempts, attempts, 0)));
}
