using System;
using static OrbAutomata.AutoHarvestReflectionAccess;
#if SERVICE_CYCLE_PROFILE
using System.Reflection;
using OrbAutomata.Runtime.ServiceCycle.Profile;
#endif

namespace OrbAutomata;

internal sealed class AutoHarvestStaticContractAuditor
{
    private const int CostTypeExitPhase = 1;
    private const int PlotPhaseIdle = 0;
    private const int PlotPhaseGrowing = 1;
    private const int PlotPhaseResting = 2;
    private const int TimerTypeSingle = 0;
    private const int TimerTypeParallel = 1;
    private const int FilterTypeWhiteList = 1;
#if SERVICE_CYCLE_PROFILE
    private readonly AutoHarvestProfileOperations _profileOperations;

    internal AutoHarvestStaticContractAuditor(AutoHarvestProfileOperations profileOperations) =>
        _profileOperations = profileOperations ??
            throw new ArgumentNullException(nameof(profileOperations));
#endif

    public AutoHarvestActionSafetyState ReadActionSafety(
        AutoHarvestReflectionContract contract,
        AutoHarvestSharedBinding shared,
        AutoHarvestPairBinding binding)
    {
        if (GetValue(contract.PlotAutoAction, binding.Plot) is not null ||
            !ValidatePhaseCycle(contract, binding))
            return AutoHarvestActionSafetyState.UnsafeCompletionEffects;
        var action = binding.Action;
        if (ReadBool(contract.ActionIsGrowing, action) ||
            ReadInt(contract.ActionCostType, action) != CostTypeExitPhase ||
            ReadInt(contract.ActionCostExitPhase, action) != PlotPhaseResting ||
            ReadInt(contract.ActionElementCost, action) != 1 ||
            ReadBool(contract.ActionUseSizeModForCost, action) ||
            ReadBool(contract.ActionUseAnyStateForCost, action) ||
            ReadBool(contract.ActionParallel, action) ||
            ReadBool(contract.ActionUseSpaceForTime, action) ||
            ReadBool(contract.ActionIgnoreYield, action))
            return AutoHarvestActionSafetyState.Destructive;
        if (!AutoHarvestContractValues.IsFiniteNear(ReadDouble(contract.ActionBaseTime, action), binding.ActionSeconds))
            return AutoHarvestActionSafetyState.UnsafeCompletionEffects;
        if (!IsEmptyNestedList(contract.ActionPrerequisites, contract.PrerequisiteValues, action))
            return AutoHarvestActionSafetyState.UnsafeCompletionEffects;
        if (!IsEmptyNestedList(contract.ActionDrain, contract.ResourceCosts, action))
            return AutoHarvestActionSafetyState.ResourceDrain;
        if (RequireList(GetValue(contract.ActionEffects, action), "persistent action effects").Count != 0)
            return AutoHarvestActionSafetyState.UnsafeCompletionEffects;

        var complete = RequireList(GetValue(contract.ActionCompleteEffects, action), "completion effects");
        if (complete.Count != 1)
            return AutoHarvestActionSafetyState.UnsafeCompletionEffects;
#if SERVICE_CYCLE_PROFILE
        _profileOperations.AddListEntry();
#endif
        var block = complete[0];
        if (block?.GetType() != contract.InstantEffectBlockType)
            return AutoHarvestActionSafetyState.UnsafeCompletionEffects;
        if (!IsEmptyNestedList(contract.EffectBlockPrerequisites, contract.PrerequisiteValues, block))
            return AutoHarvestActionSafetyState.UnsafeCompletionEffects;
        var mods = RequireList(GetValue(contract.EffectBlockMods, block), "completion effect mods");
        var scripts = RequireList(GetValue(contract.InstantEffectScripts, block), "completion scripts");
        if (mods.Count != 1 || scripts.Count != 1)
            return AutoHarvestActionSafetyState.UnsafeCompletionEffects;
#if SERVICE_CYCLE_PROFILE
        _profileOperations.AddListEntry();
#endif
        var mod = mods[0];
#if SERVICE_CYCLE_PROFILE
        _profileOperations.AddListEntry();
#endif
        var script = scripts[0];
        if (mod?.GetType() != contract.Types.ScalingWeightEffectMod ||
            script?.GetType() != contract.Types.TreasurePoolEffect)
            return AutoHarvestActionSafetyState.UnsafeCompletionEffects;
        var scalingRef = GetValue(contract.ScalingWeightRef, mod);
        if (scalingRef is null ||
            !ReferenceEquals(GetValue(contract.ScalingWeight, scalingRef), shared.CompletionScalingWeight))
            return AutoHarvestActionSafetyState.UnsafeCompletionEffects;
        if (!ReferenceEquals(GetValue(contract.TreasurePool, script), binding.RewardPool) ||
            !string.Equals(GetValue(contract.EffectType, script) as string, "EarnTreasure", StringComparison.Ordinal) ||
            !AutoHarvestContractValues.IsFiniteNear(ReadDouble(contract.EffectValue, script), 1.0))
            return AutoHarvestActionSafetyState.UnsafeCompletionEffects;
        var filter = GetValue(contract.FilterScaling, script);
        if (filter is null || ReadInt(contract.FilterListType, filter) != FilterTypeWhiteList ||
            RequireList(GetValue(contract.FilterListContents, filter), "completion filter").Count != 0)
            return AutoHarvestActionSafetyState.UnsafeCompletionEffects;
        return AutoHarvestActionSafetyState.NativePhaseCyclePreserving;
    }

    private bool ValidatePhaseCycle(
        AutoHarvestReflectionContract contract,
        AutoHarvestPairBinding binding)
    {
        var phases = RequireList(GetValue(contract.PlotPhaseInfos, binding.Plot), "plot phase information");
        if (phases.Count != 3) return false;
        var seen = 0;
        foreach (var phase in phases)
        {
#if SERVICE_CYCLE_PROFILE
            _profileOperations.AddListEntry();
#endif
            if (phase is null || phase.GetType() != contract.PhaseInfoType) return false;
            var phaseId = ReadInt(contract.PhaseInfoPhase, phase);
            var phaseTime = ReadDouble(contract.PhaseInfoTime, phase);
            var processType = ReadInt(contract.PhaseInfoProcessType, phase);
            var exitPhase = ReadInt(contract.PhaseInfoExitPhase, phase);
            var valid = phaseId switch
            {
                PlotPhaseIdle => AutoHarvestContractValues.IsFiniteNear(phaseTime, 0.0) &&
                    processType == TimerTypeParallel && exitPhase == PlotPhaseIdle,
                PlotPhaseGrowing => AutoHarvestContractValues.IsFiniteNear(phaseTime, binding.GrowthSeconds) &&
                    processType == TimerTypeParallel && exitPhase == PlotPhaseIdle,
                PlotPhaseResting => AutoHarvestContractValues.IsFiniteNear(phaseTime, binding.RestSeconds) &&
                    processType == TimerTypeSingle && exitPhase == PlotPhaseGrowing,
                _ => false,
            };
            if (!valid || (seen & (1 << phaseId)) != 0) return false;
            seen |= 1 << phaseId;
        }
        return seen == 0b111;
    }

    private bool IsEmptyNestedList(
        System.Reflection.FieldInfo parentField,
        System.Reflection.FieldInfo listField,
        object owner)
    {
        var parent = GetValue(parentField, owner);
        return parent is not null && RequireList(GetValue(listField, parent), listField.Name).Count == 0;
    }

#if SERVICE_CYCLE_PROFILE
    private object? GetValue(FieldInfo field, object owner) =>
        AutoHarvestReflectionAccess.GetValue(field, owner, _profileOperations);

    private bool ReadBool(FieldInfo field, object owner) =>
        AutoHarvestReflectionAccess.ReadBool(field, owner, _profileOperations);

    private int ReadInt(FieldInfo field, object owner) =>
        AutoHarvestReflectionAccess.ReadInt(field, owner, _profileOperations);

    private double ReadDouble(FieldInfo field, object owner) =>
        AutoHarvestReflectionAccess.ReadDouble(field, owner, _profileOperations);
#else
    private static object? GetValue(System.Reflection.FieldInfo field, object owner) =>
        field.GetValue(owner);
#endif
}
