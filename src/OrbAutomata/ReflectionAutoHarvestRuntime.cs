using System;
using System.Collections;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

internal sealed class ReflectionAutoHarvestRuntime : IAutoHarvestRuntime
{
    private readonly TypedRegistryResolver _registryResolver;
    private PairBinding? _fruit;
    private PairBinding? _treasure;
    private object? _activeActions;
    private object? _completionScalingWeight;
    private object? _fruitRewardPool;
    private object? _treasureRewardPool;
    private TypedRegistryResolution? _activeResolution;
    private long _resolvedGeneration = -1;
    private string? _blockedReason;

    private Type? _plotType;
    private Type? _actionType;
    private Type? _instanceType;
    private Type? _activeType;
    private Type? _scalingWeightType;
    private Type? _rewardPoolType;
    private Type? _scalingWeightEffectModType;
    private Type? _treasurePoolEffectType;
    private Type? _instantEffectBlockType;
    private Type? _phaseInfoType;

    private FieldInfo? _plotAvailableActions;
    private FieldInfo? _plotPhaseInfos;
    private FieldInfo? _plotAutoAction;
    private MethodInfo? _plotIsVisible;
    private MethodInfo? _plotGetActionInstances;
    private MethodInfo? _plotGetRemainingQuantity;

    private FieldInfo? _actionPrerequisites;
    private FieldInfo? _actionIsGrowing;
    private FieldInfo? _actionCostType;
    private FieldInfo? _actionCostExitPhase;
    private FieldInfo? _actionElementCost;
    private FieldInfo? _actionUseSizeModForCost;
    private FieldInfo? _actionUseAnyStateForCost;
    private FieldInfo? _actionParallel;
    private FieldInfo? _actionBaseTime;
    private FieldInfo? _actionUseSpaceForTime;
    private FieldInfo? _actionDrain;
    private FieldInfo? _actionEffects;
    private FieldInfo? _actionIgnoreYield;
    private FieldInfo? _actionCompleteEffects;
    private MethodInfo? _actionGetElementCost;

    private MethodInfo? _instanceGetAction;
    private MethodInfo? _instanceGetElement;
    private MethodInfo? _instanceIsVisible;
    private MethodInfo? _instanceIsEmpty;
    private MethodInfo? _instanceIsEngaged;
    private MethodInfo? _instanceHasEnough;
    private MethodInfo? _instanceGetMaximumRemaining;
    private MethodInfo? _instanceGetActualQuantity;

    private FieldInfo? _activeValues;
    private MethodInfo? _activeGetUsedSpots;
    private MethodInfo? _activeAddInstance;

    private FieldInfo? _prerequisiteValues;
    private FieldInfo? _resourceCosts;
    private FieldInfo? _effectBlockPrerequisites;
    private FieldInfo? _effectBlockMods;
    private FieldInfo? _instantEffectScripts;
    private FieldInfo? _scalingWeightRef;
    private FieldInfo? _scalingWeight;
    private FieldInfo? _treasurePool;
    private FieldInfo? _effectType;
    private FieldInfo? _effectValue;
    private FieldInfo? _filterScaling;
    private FieldInfo? _filterListType;
    private FieldInfo? _filterListContents;
    private FieldInfo? _phaseInfoPhase;
    private FieldInfo? _phaseInfoTime;
    private FieldInfo? _phaseInfoProcessType;
    private FieldInfo? _phaseInfoExitPhase;

    public ReflectionAutoHarvestRuntime(TypedRegistryResolver? registryResolver = null)
    {
        _registryResolver = registryResolver ?? TypedRegistryResolver.Shared;
    }

    public bool TryReadCandidate(
        AutoHarvestPair pair,
        bool selected,
        out NativeAutoHarvestCandidate? candidate,
        out AutoHarvestCandidateSnapshot snapshot,
        out string reason)
    {
        candidate = null;
        snapshot = UnknownSnapshot(pair, selected);
        if (!TryInitialize(out reason)) return false;

        var binding = pair == AutoHarvestPair.FruitTree ? _fruit! : _treasure!;
        try
        {
            var availableActions = RequireList(_plotAvailableActions!.GetValue(binding.Plot), "plot available actions");
            var actionAvailable = ContainsOnly(availableActions, _actionType!) &&
                CountReference(availableActions, binding.Action) == 1;
            var prototype = FindPrototype(binding, out var prototypeKnown);
            var visible = InvokeBool(_plotIsVisible!, binding.Plot);
            var prerequisiteState = prototypeKnown && prototype is not null
                ? Evidence(InvokeBool(_instanceIsVisible!, prototype))
                : AutoHarvestEvidenceState.Unknown;
            var readinessState = AutoHarvestEvidenceState.Unknown;
            if (prototypeKnown && prototype is not null)
            {
                var ready = InvokeInt(_plotGetRemainingQuantity!, binding.Plot) > 0 &&
                    InvokeBool(_instanceHasEnough!, prototype) &&
                    InvokeInt(_instanceGetMaximumRemaining!, prototype) > 0 &&
                    InvokeInt(_actionGetElementCost!, binding.Action, binding.Plot) == 1;
                readinessState = Evidence(ready);
            }

            var safety = ReadActionSafety(binding);
            var activeState = CaptureSubmissionState(binding);
            var identity = _activeResolution is not null &&
                _registryResolver.IsCurrent(_activeResolution) &&
                IdentityMatches(binding.Plot, binding.PlotUuid) &&
                IdentityMatches(binding.Action, binding.ActionUuid)
                ? AutoHarvestEvidenceState.Verified
                : AutoHarvestEvidenceState.Unknown;
            snapshot = new AutoHarvestCandidateSnapshot(
                binding.PlotUuid,
                binding.ActionUuid,
                _resolvedGeneration,
                selected,
                identity,
                Evidence(visible),
                Evidence(actionAvailable),
                prerequisiteState,
                readinessState,
                safety,
                activeState.IsValid
                    ? Evidence(activeState.SupportedCollectCount == 0)
                    : AutoHarvestEvidenceState.Unknown,
                activeState.IsValid
                    ? Evidence(activeState.EmptySlots >= 2)
                    : AutoHarvestEvidenceState.Unknown);

            if (prototype is not null)
                candidate = new NativeAutoHarvestCandidate(pair, _resolvedGeneration, binding.Plot, binding.Action, prototype);
            reason = string.Empty;
            return true;
        }
        catch (Exception ex) when (IsExpectedFailure(ex))
        {
            _blockedReason = "native Auto Harvest state contract blocked until the next lifecycle: " + ex.GetBaseException().Message;
            reason = _blockedReason;
            return false;
        }
    }

    public AutoHarvestSubmissionResult TrySubmit(NativeAutoHarvestCandidate candidate)
    {
        if (candidate is null) return new(false, false, "candidate is missing");
        if (!TryReadCandidate(candidate.Pair, selected: true, out var refreshed, out var snapshot, out var readReason))
            return new(false, false, readReason);
        var decision = AutoHarvestPolicy.Evaluate(snapshot, GameLifecycleMonitor.Shared.Current.Generation);
        if (!decision.ShouldSubmit || refreshed is null)
            return new(false, false, $"candidate revalidation rejected: {decision.RejectionReason}");
        if (candidate.LifecycleEpoch != refreshed.LifecycleEpoch ||
            !ReferenceEquals(candidate.Plot, refreshed.Plot) ||
            !ReferenceEquals(candidate.Action, refreshed.Action) ||
            !ReferenceEquals(candidate.Prototype, refreshed.Prototype))
            return new(false, false, "candidate identity changed during revalidation");

        var binding = candidate.Pair == AutoHarvestPair.FruitTree ? _fruit! : _treasure!;
        var evidence = NativeMutationVerifier.Execute(
            "Auto Harvest",
            binding.ActionUuid,
            "one exact native plot action is engaged while one manual slot remains free",
            () => CaptureSubmissionState(binding),
            () => _activeAddInstance!.Invoke(_activeActions, new object[] { candidate.Prototype, 1 }),
            (before, after) =>
                before.IsValid &&
                before.EmptySlots >= 2 &&
                before.SupportedCollectCount == 0 &&
                before.PairMatchCount == 0 &&
                after.IsValid &&
                after.UsedSlots == before.UsedSlots + 1 &&
                after.EmptySlots == before.EmptySlots - 1 &&
                after.SupportedCollectCount == 1 &&
                after.PairMatchCount == 1 &&
                after.PairQuantity == 1 &&
                after.PairEngaged);
        return evidence.IsVerified
            ? new AutoHarvestSubmissionResult(true, true, string.Empty)
            : new AutoHarvestSubmissionResult(false, evidence.MutationWasAttempted, evidence.Format(state => state.ToString()));
    }

    public void InvalidateLifecycle()
    {
        _fruit = null;
        _treasure = null;
        _activeActions = null;
        _completionScalingWeight = null;
        _fruitRewardPool = null;
        _treasureRewardPool = null;
        _activeResolution = null;
        _resolvedGeneration = -1;
        _blockedReason = null;
    }

    public void Dispose() => InvalidateLifecycle();

    private bool TryInitialize(out string reason)
    {
        if (_blockedReason is not null)
        {
            reason = _blockedReason;
            return false;
        }
        if (_fruit is not null && _treasure is not null && _activeResolution is not null &&
            _registryResolver.IsCurrent(_activeResolution))
        {
            reason = string.Empty;
            return true;
        }

        try
        {
            _plotType = RequireLoadedType(KnownEntities.FruitTreePlot.ManagedTypeName);
            _actionType = RequireLoadedType(KnownEntities.FruitTreeCollect.ManagedTypeName);
            _instanceType = RequireLoadedType("PlotNodeActionInstance");
            _activeType = RequireLoadedType(KnownEntities.ActivePlotNodeActions.ManagedTypeName);
            _scalingWeightType = RequireLoadedType(KnownEntities.CompletionScalingWeight.ManagedTypeName);
            _rewardPoolType = RequireLoadedType(KnownEntities.FruitTreeRewardPool.ManagedTypeName);
            _scalingWeightEffectModType = RequireLoadedExactType("ScalingWeightEffectMod");
            _treasurePoolEffectType = RequireLoadedExactType("TreasurePoolSO+TreasurePoolInstantEffect");

            var fruitPlot = Resolve(KnownEntities.FruitTreePlot.Uuid, _plotType, KnownEntities.FruitTreePlot.DiagnosticName, out _);
            var fruitAction = Resolve(KnownEntities.FruitTreeCollect.Uuid, _actionType, KnownEntities.FruitTreeCollect.DiagnosticName, out _);
            var treasurePlot = Resolve(KnownEntities.TreasureTreePlot.Uuid, _plotType, KnownEntities.TreasureTreePlot.DiagnosticName, out _);
            var treasureAction = Resolve(KnownEntities.TreasureTreeCollect.Uuid, _actionType, KnownEntities.TreasureTreeCollect.DiagnosticName, out _);
            _activeActions = Resolve(KnownEntities.ActivePlotNodeActions.Uuid, _activeType, KnownEntities.ActivePlotNodeActions.DiagnosticName, out _activeResolution);
            _completionScalingWeight = Resolve(KnownEntities.CompletionScalingWeight.Uuid, _scalingWeightType, KnownEntities.CompletionScalingWeight.DiagnosticName, out _);
            _fruitRewardPool = Resolve(KnownEntities.FruitTreeRewardPool.Uuid, _rewardPoolType, KnownEntities.FruitTreeRewardPool.DiagnosticName, out _);
            _treasureRewardPool = Resolve(KnownEntities.TreasureTreeRewardPool.Uuid, _rewardPoolType, KnownEntities.TreasureTreeRewardPool.DiagnosticName, out _);
            _resolvedGeneration = _activeResolution!.LifecycleGeneration;

            BindContract();
            _fruit = new PairBinding(
                AutoHarvestPair.FruitTree,
                fruitPlot,
                fruitAction,
                KnownEntities.FruitTreePlot.Uuid.ToString("D"),
                KnownEntities.FruitTreeCollect.Uuid.ToString("D"),
                _fruitRewardPool,
                growthSeconds: 480.0,
                restSeconds: 340.0,
                actionSeconds: 3.0);
            _treasure = new PairBinding(
                AutoHarvestPair.TreasureTree,
                treasurePlot,
                treasureAction,
                KnownEntities.TreasureTreePlot.Uuid.ToString("D"),
                KnownEntities.TreasureTreeCollect.Uuid.ToString("D"),
                _treasureRewardPool,
                growthSeconds: 720.0,
                restSeconds: 360.0,
                actionSeconds: 10.0);
            reason = string.Empty;
            return true;
        }
        catch (RegistryNotReadyException ex)
        {
            reason = ex.Message;
            return false;
        }
        catch (Exception ex) when (IsExpectedFailure(ex))
        {
            _blockedReason = "native Auto Harvest contract blocked until the next lifecycle: " + ex.GetBaseException().Message;
            reason = _blockedReason;
            return false;
        }
    }

    private void BindContract()
    {
        _phaseInfoType = RequireLoadedExactType("PlotNodeSO+PlotNodePhaseInfo");
        var plotPhaseType = RequireLoadedExactType("PlotNodeSO+PlotNodePhases");
        var timerType = RequireLoadedExactType("TimerList+TimerType");
        var actionCostType = RequireLoadedExactType("PlotNodeActionSO+CostType");
        var prerequisiteType = RequireLoadedExactType("Prerequisites+Container");
        var requirementType = RequireLoadedExactType("Requirements.IRequirementCondition");
        var resourceCostType = RequireLoadedExactType("ResourceCostList");
        var resourceTupleType = RequireLoadedExactType("ResourceTuple");
        var persistentEffectBlockType = RequireLoadedExactType("PersistentEffectBlock");
        var instantEffectBlockType = RequireLoadedExactType("InstantEffectBlock");
        var effectModType = RequireLoadedExactType("IEffectMod");
        var instantEffectScriptType = RequireLoadedExactType("IInstantEffectScript");
        var scalingWeightRefType = RequireLoadedExactType("ScalingWeightRef");
        var filterEffectType = RequireLoadedExactType("FilterEffectMod");
        var filterListType = RequireLoadedExactType("FilterEffectMod+FilterType");
        var scalingType = RequireLoadedExactType("ScalingType");

        _plotAvailableActions = RequireListField(_plotType!, "availableActions", _actionType!);
        _plotPhaseInfos = RequireListField(_plotType!, "phaseInfos", _phaseInfoType);
        _plotAutoAction = RequireField(_plotType!, "autoAction", _actionType!);
        _plotIsVisible = RequireMethod(_plotType!, "IsVisible", typeof(bool));
        _plotGetActionInstances = RequireListMethod(_plotType!, "GetActionInstances", _instanceType!);
        _plotGetRemainingQuantity = RequireMethod(_plotType!, "GetRemainingQuantity", typeof(int));

        _actionPrerequisites = RequireField(_actionType!, "prerequisites", prerequisiteType);
        _actionIsGrowing = RequireField(_actionType!, "isGrowingAction", typeof(bool));
        _actionCostType = RequireField(_actionType!, "elementCostType", actionCostType);
        _actionCostExitPhase = RequireField(_actionType!, "elementCostExitPhase", plotPhaseType);
        _actionElementCost = RequireField(_actionType!, "elementCost", typeof(int));
        _actionUseSizeModForCost = RequireField(_actionType!, "useSizeModForCost", typeof(bool));
        _actionUseAnyStateForCost = RequireField(_actionType!, "useAnyStateForCost", typeof(bool));
        _actionParallel = RequireField(_actionType!, "parallelAction", typeof(bool));
        _actionBaseTime = RequireField(_actionType!, "baseTime", typeof(double));
        _actionUseSpaceForTime = RequireField(_actionType!, "useSpaceUsageForTimeMult", typeof(bool));
        _actionDrain = RequireField(_actionType!, "actionDrain", resourceCostType);
        _actionEffects = RequireListField(_actionType!, "actionEffects", persistentEffectBlockType);
        _actionIgnoreYield = RequireField(_actionType!, "ignoreNodeYield", typeof(bool));
        _actionCompleteEffects = RequireListField(_actionType!, "completeEffects", instantEffectBlockType);
        _actionGetElementCost = RequireMethod(_actionType!, "GetElementCost", typeof(int), _plotType!);

        _instanceGetAction = RequireMethod(_instanceType!, "GetAction", _actionType!);
        _instanceGetElement = RequireMethod(_instanceType!, "GetElement", _plotType!);
        _instanceIsVisible = RequireMethod(_instanceType!, "IsVisible", typeof(bool));
        _instanceIsEmpty = RequireMethod(_instanceType!, "IsEmpty", typeof(bool));
        _instanceIsEngaged = RequireMethod(_instanceType!, "IsEngaged", typeof(bool));
        _instanceHasEnough = RequireMethod(_instanceType!, "HasEnoughForOneInstance", typeof(bool));
        _instanceGetMaximumRemaining = RequireMethod(_instanceType!, "GetMaximumRemInstances", typeof(int));
        _instanceGetActualQuantity = RequireMethod(_instanceType!, "GetActualQuantity", typeof(int));

        _activeValues = RequireListField(_activeType!, "value", _instanceType!);
        _activeGetUsedSpots = RequireMethod(_activeType!, "GetUsedSpots", typeof(int));
        _activeAddInstance = RequireMethod(_activeType!, "AddInstance", typeof(void), _instanceType!, typeof(int));

        _prerequisiteValues = RequireListField(prerequisiteType, "prerequisites", requirementType);
        _resourceCosts = RequireListField(resourceCostType, "costs", resourceTupleType);
        _instantEffectBlockType = instantEffectBlockType;
        _effectBlockPrerequisites = RequireField(_instantEffectBlockType, "prerequisites", prerequisiteType);
        _effectBlockMods = RequireListField(_instantEffectBlockType, "effectMods", effectModType);
        _instantEffectScripts = RequireListField(_instantEffectBlockType, "effectScripts", instantEffectScriptType);
        _scalingWeightRef = RequireField(_scalingWeightEffectModType!, "scalingWeightRef", scalingWeightRefType);
        _scalingWeight = RequireField(scalingWeightRefType, "scalingWeight", _scalingWeightType!);
        _treasurePool = RequireField(_treasurePoolEffectType!, "treasurePool", _rewardPoolType!);
        _effectType = RequireField(_treasurePoolEffectType!, "effectType", typeof(string));
        _effectValue = RequireField(_treasurePoolEffectType!, "effectValue", typeof(double));
        _filterScaling = RequireField(_treasurePoolEffectType!, "filterScaling", filterEffectType);
        _filterListType = RequireField(filterEffectType, "listType", filterListType);
        _filterListContents = RequireListField(filterEffectType, "listContents", scalingType);

        _phaseInfoPhase = RequireField(_phaseInfoType, "phase", plotPhaseType);
        _phaseInfoTime = RequireField(_phaseInfoType, "phaseTime", typeof(double));
        _phaseInfoProcessType = RequireField(_phaseInfoType, "processType", timerType);
        _phaseInfoExitPhase = RequireField(_phaseInfoType, "exitPhase", plotPhaseType);
    }

    private AutoHarvestActionSafetyState ReadActionSafety(PairBinding binding)
    {
        if (_plotAutoAction!.GetValue(binding.Plot) is not null || !ValidatePhaseCycle(binding))
            return AutoHarvestActionSafetyState.UnsafeCompletionEffects;
        var action = binding.Action;
        if (ReadBool(_actionIsGrowing!, action) ||
            ReadInt(_actionCostType!, action) != 1 ||
            ReadInt(_actionCostExitPhase!, action) != 2 ||
            ReadInt(_actionElementCost!, action) != 1 ||
            ReadBool(_actionUseSizeModForCost!, action) ||
            ReadBool(_actionUseAnyStateForCost!, action) ||
            ReadBool(_actionParallel!, action) ||
            ReadBool(_actionUseSpaceForTime!, action) ||
            ReadBool(_actionIgnoreYield!, action))
            return AutoHarvestActionSafetyState.Destructive;
        if (!AutoHarvestContractValues.IsFiniteNear(ReadDouble(_actionBaseTime!, action), binding.ActionSeconds))
            return AutoHarvestActionSafetyState.UnsafeCompletionEffects;
        if (!IsEmptyNestedList(_actionPrerequisites!, _prerequisiteValues!, action))
            return AutoHarvestActionSafetyState.UnsafeCompletionEffects;
        if (!IsEmptyNestedList(_actionDrain!, _resourceCosts!, action))
            return AutoHarvestActionSafetyState.ResourceDrain;
        if (RequireList(_actionEffects!.GetValue(action), "persistent action effects").Count != 0)
            return AutoHarvestActionSafetyState.UnsafeCompletionEffects;

        var complete = RequireList(_actionCompleteEffects!.GetValue(action), "completion effects");
        if (complete.Count != 1 || complete[0]?.GetType() != _instantEffectBlockType)
            return AutoHarvestActionSafetyState.UnsafeCompletionEffects;
        var block = complete[0]!;
        if (!IsEmptyNestedList(_effectBlockPrerequisites!, _prerequisiteValues!, block))
            return AutoHarvestActionSafetyState.UnsafeCompletionEffects;
        var mods = RequireList(_effectBlockMods!.GetValue(block), "completion effect mods");
        var scripts = RequireList(_instantEffectScripts!.GetValue(block), "completion scripts");
        if (mods.Count != 1 || scripts.Count != 1 || mods[0]?.GetType() != _scalingWeightEffectModType || scripts[0]?.GetType() != _treasurePoolEffectType)
            return AutoHarvestActionSafetyState.UnsafeCompletionEffects;
        var scalingRef = _scalingWeightRef!.GetValue(mods[0]!);
        if (scalingRef is null || !ReferenceEquals(_scalingWeight!.GetValue(scalingRef), _completionScalingWeight))
            return AutoHarvestActionSafetyState.UnsafeCompletionEffects;
        var script = scripts[0]!;
        if (!ReferenceEquals(_treasurePool!.GetValue(script), binding.RewardPool) ||
            !string.Equals(_effectType!.GetValue(script) as string, "EarnTreasure", StringComparison.Ordinal) ||
            !AutoHarvestContractValues.IsFiniteNear(ReadDouble(_effectValue!, script), 1.0))
            return AutoHarvestActionSafetyState.UnsafeCompletionEffects;
        var filter = _filterScaling!.GetValue(script);
        if (filter is null || ReadInt(_filterListType!, filter) != 1 ||
            RequireList(_filterListContents!.GetValue(filter), "completion filter").Count != 0)
            return AutoHarvestActionSafetyState.UnsafeCompletionEffects;
        return AutoHarvestActionSafetyState.NativePhaseCyclePreserving;
    }

    private bool ValidatePhaseCycle(PairBinding binding)
    {
        var phases = RequireList(_plotPhaseInfos!.GetValue(binding.Plot), "plot phase information");
        if (phases.Count != 3) return false;
        var seen = 0;
        foreach (var phase in phases)
        {
            if (phase is null || phase.GetType() != _phaseInfoType) return false;
            var phaseId = ReadInt(_phaseInfoPhase!, phase);
            var phaseTime = ReadDouble(_phaseInfoTime!, phase);
            var processType = ReadInt(_phaseInfoProcessType!, phase);
            var exitPhase = ReadInt(_phaseInfoExitPhase!, phase);
            var valid = phaseId switch
            {
                0 => AutoHarvestContractValues.IsFiniteNear(phaseTime, 0.0) && processType == 1 && exitPhase == 0,
                1 => AutoHarvestContractValues.IsFiniteNear(phaseTime, binding.GrowthSeconds) && processType == 1 && exitPhase == 0,
                2 => AutoHarvestContractValues.IsFiniteNear(phaseTime, binding.RestSeconds) && processType == 0 && exitPhase == 1,
                _ => false,
            };
            if (!valid || (seen & (1 << phaseId)) != 0) return false;
            seen |= 1 << phaseId;
        }
        return seen == 0b111;
    }

    private object? FindPrototype(PairBinding binding, out bool known)
    {
        known = false;
        if (_plotGetActionInstances!.Invoke(binding.Plot, Array.Empty<object>()) is not IList instances) return null;
        object? match = null;
        foreach (var instance in instances)
        {
            if (instance is null || instance.GetType() != _instanceType) return null;
            var plot = _instanceGetElement!.Invoke(instance, Array.Empty<object>());
            var action = _instanceGetAction!.Invoke(instance, Array.Empty<object>());
            var observed = ClassifyPair(plot, action);
            if (observed == AutoHarvestObservedPair.Contradictory) return null;
            var expected = binding.Pair == AutoHarvestPair.FruitTree
                ? AutoHarvestObservedPair.FruitTree
                : AutoHarvestObservedPair.TreasureTree;
            if (observed != expected) continue;
            if (match is not null) return null;
            match = instance;
        }
        known = true;
        return match;
    }

    private SubmissionState CaptureSubmissionState(PairBinding target)
    {
        var values = RequireList(_activeValues!.GetValue(_activeActions), "active plot actions");
        var used = InvokeInt(_activeGetUsedSpots!, _activeActions!);
        var empty = 0;
        var supported = 0;
        var targetMatches = 0;
        var targetQuantity = 0;
        var targetEngaged = false;
        foreach (var instance in values)
        {
            if (instance is null || instance.GetType() != _instanceType) return SubmissionState.Invalid;
            if (InvokeBool(_instanceIsEmpty!, instance))
            {
                empty++;
                continue;
            }
            var plot = _instanceGetElement!.Invoke(instance, Array.Empty<object>());
            var action = _instanceGetAction!.Invoke(instance, Array.Empty<object>());
            var observed = ClassifyPair(plot, action);
            if (observed == AutoHarvestObservedPair.Contradictory) return SubmissionState.Invalid;
            if (observed == AutoHarvestObservedPair.Unrelated) continue;
            var pair = observed == AutoHarvestObservedPair.FruitTree
                ? AutoHarvestPair.FruitTree
                : AutoHarvestPair.TreasureTree;
            supported++;
            if (pair != target.Pair) continue;
            targetMatches++;
            targetQuantity += InvokeInt(_instanceGetActualQuantity!, instance);
            targetEngaged |= InvokeBool(_instanceIsEngaged!, instance);
        }
        if (used < 0 || used != values.Count - empty) return SubmissionState.Invalid;
        return new SubmissionState(true, used, empty, supported, targetMatches, targetQuantity, targetEngaged);
    }

    private AutoHarvestObservedPair ClassifyPair(object? plot, object? action)
    {
        if (plot is null || action is null) return AutoHarvestObservedPair.Contradictory;
        if (plot.GetType() != _plotType || action.GetType() != _actionType)
            return AutoHarvestObservedPair.Contradictory;
        var exactFruit = _fruit is not null &&
            ReferenceEquals(plot, _fruit.Plot) && ReferenceEquals(action, _fruit.Action);
        var exactTreasure = _treasure is not null &&
            ReferenceEquals(plot, _treasure.Plot) && ReferenceEquals(action, _treasure.Action);
        var supportedActionReference = _fruit is not null && ReferenceEquals(action, _fruit.Action) ||
            _treasure is not null && ReferenceEquals(action, _treasure.Action);
        return AutoHarvestIdentityPolicy.Classify(
            ReflectionUtil.ReadStableId(plot) ?? string.Empty,
            ReflectionUtil.ReadStableId(action) ?? string.Empty,
            exactFruit,
            exactTreasure,
            supportedActionReference);
    }

    private object Resolve(Guid uuid, Type type, string name, out TypedRegistryResolution resolution)
    {
        resolution = _registryResolver.Resolve(uuid, type);
        if (!resolution.IsResolved)
        {
            var message = $"{name} registry identity is unavailable: {resolution.Format()}";
            if (resolution.IsRetryable) throw new RegistryNotReadyException(message);
            throw new InvalidOperationException(message);
        }
        return resolution.Value!;
    }

    private static AutoHarvestCandidateSnapshot UnknownSnapshot(AutoHarvestPair pair, bool selected)
    {
        var plotUuid = pair == AutoHarvestPair.FruitTree ? AutoHarvestKnownIds.FruitTreePlot : AutoHarvestKnownIds.TreasureTreePlot;
        var actionUuid = pair == AutoHarvestPair.FruitTree ? AutoHarvestKnownIds.FruitTreeCollect : AutoHarvestKnownIds.TreasureTreeCollect;
        return new AutoHarvestCandidateSnapshot(
            plotUuid, actionUuid, -1, selected,
            AutoHarvestEvidenceState.Unknown, AutoHarvestEvidenceState.Unknown,
            AutoHarvestEvidenceState.Unknown, AutoHarvestEvidenceState.Unknown,
            AutoHarvestEvidenceState.Unknown, AutoHarvestActionSafetyState.Unknown,
            AutoHarvestEvidenceState.Unknown, AutoHarvestEvidenceState.Unknown);
    }

    private static AutoHarvestEvidenceState Evidence(bool value) =>
        value ? AutoHarvestEvidenceState.Verified : AutoHarvestEvidenceState.Rejected;

    private static bool IsEmptyNestedList(FieldInfo parentField, FieldInfo listField, object owner)
    {
        var parent = parentField.GetValue(owner);
        return parent is not null && RequireList(listField.GetValue(parent), listField.Name).Count == 0;
    }

    private static int CountReference(IList values, object expected)
    {
        var count = 0;
        foreach (var value in values) if (ReferenceEquals(value, expected)) count++;
        return count;
    }

    private static bool ContainsOnly(IList values, Type expectedType)
    {
        foreach (var value in values)
            if (value is null || value.GetType() != expectedType) return false;
        return true;
    }

    private static bool IdentityMatches(object value, string expected) =>
        Guid.TryParse(ReflectionUtil.ReadStableId(value), out var actual) &&
        Guid.TryParse(expected, out var wanted) &&
        actual == wanted;

    private static IList RequireList(object? value, string name) =>
        value as IList ?? throw new InvalidOperationException($"{name} is unavailable");

    private static Type RequireLoadedType(string name) =>
        ReflectionUtil.FindLoadedType(name) ?? throw new RegistryNotReadyException($"native type {name} is not registered yet");

    private static Type RequireLoadedExactType(string fullName)
    {
        var type = Type.GetType($"{fullName}, Assembly-CSharp", throwOnError: false);
        if (type is null || !string.Equals(type.FullName, fullName, StringComparison.Ordinal))
            throw new RegistryNotReadyException($"native type {fullName} is not registered exactly");
        return type;
    }

    private static FieldInfo RequireField(Type type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var field = current.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field is not null) return field;
        }
        throw new InvalidOperationException($"{type.FullName}.{name} field is unavailable");
    }

    private static FieldInfo RequireField(Type type, string name, Type expectedType)
    {
        var field = RequireField(type, name);
        if (field.FieldType != expectedType)
            throw new InvalidOperationException(
                $"{type.FullName}.{name} field type is {field.FieldType.FullName}; expected {expectedType.FullName}");
        return field;
    }

    private static FieldInfo RequireListField(Type type, string name, Type? expectedElementType = null)
    {
        var field = RequireField(type, name);
        if (!typeof(IList).IsAssignableFrom(field.FieldType))
            throw new InvalidOperationException($"{type.FullName}.{name} field type is not a list");
        if (expectedElementType is not null && ElementType(field.FieldType) != expectedElementType)
            throw new InvalidOperationException(
                $"{type.FullName}.{name} list element type is not {expectedElementType.FullName}");
        return field;
    }

    private static MethodInfo RequireMethod(Type type, string name, Type? returnType, params Type[] parameters)
    {
        var method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, parameters, null);
        if (method is null || returnType is not null && method.ReturnType != returnType)
            throw new InvalidOperationException($"{type.FullName}.{name} method contract is unavailable");
        return method;
    }

    private static MethodInfo RequireListMethod(Type type, string name, Type expectedElementType)
    {
        var method = RequireMethod(type, name, null);
        if (!typeof(IList).IsAssignableFrom(method.ReturnType) || ElementType(method.ReturnType) != expectedElementType)
            throw new InvalidOperationException(
                $"{type.FullName}.{name} return type is not a list of {expectedElementType.FullName}");
        return method;
    }

    private static Type ElementType(Type collectionType) =>
        collectionType.IsArray
            ? collectionType.GetElementType()!
            : collectionType.GetGenericArguments().Length == 1
                ? collectionType.GetGenericArguments()[0]
                : throw new InvalidOperationException($"{collectionType.FullName} has no single element type");

    private static bool ReadBool(FieldInfo field, object owner) =>
        field.GetValue(owner) is bool value
            ? value
            : throw new InvalidOperationException($"{field.Name} is not Boolean");

    private static int ReadInt(FieldInfo field, object owner) => Convert.ToInt32(field.GetValue(owner));
    private static double ReadDouble(FieldInfo field, object owner) => Convert.ToDouble(field.GetValue(owner));
    private static bool InvokeBool(MethodInfo method, object owner) =>
        method.Invoke(owner, Array.Empty<object>()) is bool value
            ? value
            : throw new InvalidOperationException($"{method.Name} did not return Boolean");
    private static int InvokeInt(MethodInfo method, object owner, params object[] args) =>
        Convert.ToInt32(method.Invoke(owner, args));

    private static bool IsExpectedFailure(Exception ex) => ex is
        TargetInvocationException or
        ArgumentException or
        InvalidOperationException or
        InvalidCastException or
        FormatException or
        OverflowException or
        NullReferenceException or
        TargetException or
        TargetParameterCountException or
        MemberAccessException or
        AmbiguousMatchException or
        TypeLoadException or
        MissingMemberException;

    private sealed class RegistryNotReadyException : Exception
    {
        public RegistryNotReadyException(string message) : base(message) { }
    }

    private sealed class PairBinding
    {
        public PairBinding(
            AutoHarvestPair pair,
            object plot,
            object action,
            string plotUuid,
            string actionUuid,
            object rewardPool,
            double growthSeconds,
            double restSeconds,
            double actionSeconds)
        {
            Pair = pair;
            Plot = plot;
            Action = action;
            PlotUuid = plotUuid;
            ActionUuid = actionUuid;
            RewardPool = rewardPool;
            GrowthSeconds = growthSeconds;
            RestSeconds = restSeconds;
            ActionSeconds = actionSeconds;
        }

        public AutoHarvestPair Pair { get; }
        public object Plot { get; }
        public object Action { get; }
        public string PlotUuid { get; }
        public string ActionUuid { get; }
        public object RewardPool { get; }
        public double GrowthSeconds { get; }
        public double RestSeconds { get; }
        public double ActionSeconds { get; }
    }

    private readonly struct SubmissionState
    {
        public SubmissionState(
            bool isValid,
            int usedSlots,
            int emptySlots,
            int supportedCollectCount,
            int pairMatchCount,
            int pairQuantity,
            bool pairEngaged)
        {
            IsValid = isValid;
            UsedSlots = usedSlots;
            EmptySlots = emptySlots;
            SupportedCollectCount = supportedCollectCount;
            PairMatchCount = pairMatchCount;
            PairQuantity = pairQuantity;
            PairEngaged = pairEngaged;
        }

        public static SubmissionState Invalid => new(false, 0, 0, 0, 0, 0, false);
        public bool IsValid { get; }
        public int UsedSlots { get; }
        public int EmptySlots { get; }
        public int SupportedCollectCount { get; }
        public int PairMatchCount { get; }
        public int PairQuantity { get; }
        public bool PairEngaged { get; }

        public override string ToString() =>
            $"Valid={IsValid}, Used={UsedSlots}, Empty={EmptySlots}, Supported={SupportedCollectCount}, " +
            $"PairMatches={PairMatchCount}, PairQuantity={PairQuantity}, PairEngaged={PairEngaged}";
    }
}
