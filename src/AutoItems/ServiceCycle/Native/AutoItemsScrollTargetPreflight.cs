using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// Re-runs the exact native Scroll target selector immediately before submission. The shared
/// coverage plan is advisory; only this live check can close a target disappearing between capture
/// and mutation.
/// </summary>
internal static class AutoItemsScrollTargetPreflight
{
    private const BindingFlags PublicInstance =
        BindingFlags.Instance | BindingFlags.Public;
    private const BindingFlags PublicStatic =
        BindingFlags.Static | BindingFlags.Public;
    private const BindingFlags AnyInstance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    internal static bool TryHasValidTarget(
        object consumable,
        int plannedLevel,
        out string reason)
    {
        if (!Bindings.TryCreate(out var native, out reason) ||
            !TryGetTargeting(consumable, native!, out var targeting, out reason))
        {
            return false;
        }
        try
        {
            if (native!.StrongestLevel.Invoke(
                    consumable,
                    Array.Empty<object>()) is not int liveLevel ||
                liveLevel != plannedLevel)
            {
                reason = "The strongest live Scroll level changed after planning.";
                return false;
            }
            var count = native.Strongest.Invoke(consumable, Array.Empty<object>());
            if (count is null || count.GetType() != native.CountType)
            {
                reason = "The strongest live Scroll count was unavailable.";
                return false;
            }
            var scaling = native.CountScaling.Invoke(consumable, new[] { count });
            return TryCount(native, targeting!, scaling, out var candidates, out reason) &&
                   RequireCandidate(candidates, out reason);
        }
        catch (Exception ex) when (IsReflectionFailure(ex))
        {
            reason =
                $"The live Scroll target preflight failed: {ex.GetBaseException().Message}";
            return false;
        }
    }

    internal static bool TryCountValidTargetsAtLevel(
        object consumable,
        int level,
        out int candidates,
        out string reason)
    {
        candidates = 0;
        if (level <= 0)
        {
            reason = "The planned Scroll level was unavailable.";
            return false;
        }
        if (!Bindings.TryCreate(out var native, out reason) ||
            !TryGetTargeting(consumable, native!, out var targeting, out reason))
        {
            return false;
        }
        try
        {
            var scaling = native!.BasicScaling.Invoke(
                null,
                new object[] { new BigDouble(level) });
            return TryCount(native, targeting!, scaling, out candidates, out reason);
        }
        catch (Exception ex) when (IsReflectionFailure(ex))
        {
            reason =
                $"The live Scroll target preflight failed: {ex.GetBaseException().Message}";
            return false;
        }
    }

    private static bool TryGetTargeting(
        object consumable,
        Bindings native,
        out object? targeting,
        out string reason)
    {
        targeting = null;
        reason = "The exact audited Scroll target contract is unavailable.";
        if (consumable.GetType() != native.ConsumableType ||
            native.OnUseEffects.GetValue(consumable) is not IEnumerable blocks)
        {
            return false;
        }

        object? options = null;
        var requests = 0;
        foreach (var block in blocks)
        {
            if (block is null || block.GetType() != native.InstantBlockType ||
                native.EffectScripts.GetValue(block) is not IEnumerable scripts)
            {
                return false;
            }
            foreach (var script in scripts)
            {
                if (script is null || script.GetType() != native.RequestType) continue;
                requests++;
                options = native.TargetOptions.GetValue(script);
            }
        }
        if (requests != 1 || options is null || options.GetType() != native.OptionsType)
        {
            reason = "The live Scroll did not expose one exact target request.";
            return false;
        }
        targeting = native.GetTargeting.Invoke(options, Array.Empty<object>());
        if (targeting is null || targeting.GetType() != native.TargetStructureType)
        {
            reason = "The live Scroll target selection is not the audited structure target.";
            return false;
        }
        reason = string.Empty;
        return true;
    }

    private static bool TryCount(
        Bindings native,
        object targeting,
        object? scaling,
        out int candidates,
        out string reason)
    {
        candidates = 0;
        if (scaling is null || scaling.GetType() != native.ScalingType)
        {
            reason = "The live Scroll scaling was unavailable.";
            return false;
        }
        var list = native.GetRandomList.Invoke(targeting, new[] { scaling });
        if (list is not ICollection collection)
        {
            reason = "The live Scroll target list was unavailable.";
            return false;
        }
        candidates = collection.Count;
        reason = string.Empty;
        return true;
    }

    private static bool RequireCandidate(int candidates, out string reason)
    {
        reason = candidates > 0
            ? string.Empty
            : "The live Scroll has no valid structure target at its strongest level.";
        return candidates > 0;
    }

    private static bool IsReflectionFailure(Exception ex) =>
        ex is TargetInvocationException or MemberAccessException or ArgumentException;

    private sealed class Bindings
    {
        private Bindings(
            Type consumableType,
            Type countType,
            Type scalingType,
            Type instantBlockType,
            Type requestType,
            Type optionsType,
            Type targetStructureType,
            FieldInfo onUseEffects,
            FieldInfo effectScripts,
            FieldInfo targetOptions,
            MethodInfo strongest,
            MethodInfo strongestLevel,
            MethodInfo countScaling,
            MethodInfo basicScaling,
            MethodInfo getTargeting,
            MethodInfo getRandomList)
        {
            ConsumableType = consumableType;
            CountType = countType;
            ScalingType = scalingType;
            InstantBlockType = instantBlockType;
            RequestType = requestType;
            OptionsType = optionsType;
            TargetStructureType = targetStructureType;
            OnUseEffects = onUseEffects;
            EffectScripts = effectScripts;
            TargetOptions = targetOptions;
            Strongest = strongest;
            StrongestLevel = strongestLevel;
            CountScaling = countScaling;
            BasicScaling = basicScaling;
            GetTargeting = getTargeting;
            GetRandomList = getRandomList;
        }

        internal Type ConsumableType { get; }
        internal Type CountType { get; }
        internal Type ScalingType { get; }
        internal Type InstantBlockType { get; }
        internal Type RequestType { get; }
        internal Type OptionsType { get; }
        internal Type TargetStructureType { get; }
        internal FieldInfo OnUseEffects { get; }
        internal FieldInfo EffectScripts { get; }
        internal FieldInfo TargetOptions { get; }
        internal MethodInfo Strongest { get; }
        internal MethodInfo StrongestLevel { get; }
        internal MethodInfo CountScaling { get; }
        internal MethodInfo BasicScaling { get; }
        internal MethodInfo GetTargeting { get; }
        internal MethodInfo GetRandomList { get; }

        internal static bool TryCreate(out Bindings? bindings, out string reason)
        {
            bindings = null;
            reason = "The exact audited Scroll target contract is unavailable.";
            var consumable = ReflectionUtil.FindLoadedType("ConsumableSO");
            var count = ReflectionUtil.FindLoadedType("ConsumableCount");
            var scaling = ReflectionUtil.FindLoadedType("ScalingInfo");
            var block = ReflectionUtil.FindLoadedType("InstantEffectBlock");
            var request = ReflectionUtil.FindLoadedType("RequestTargetEffectScript");
            var options = ReflectionUtil.FindLoadedType("Targeting.TargetSelectOptions");
            var selection = ReflectionUtil.FindLoadedType("Targeting.BaseTargetSelection");
            var structure = ReflectionUtil.FindLoadedType("Targeting.TargetStructure");
            if (consumable is null || count is null || scaling is null || block is null ||
                request is null || options is null || selection is null || structure is null)
            {
                return false;
            }

            var onUse = consumable.GetField("onUseEffects", AnyInstance);
            var scripts = block.GetField("effectScripts", AnyInstance);
            var targetOptions = request.GetField("targetOptions", AnyInstance);
            var strongest = ExactMethod(consumable, "GetStrongest", count);
            var strongestLevel = ExactMethod(consumable, "GetStrongestLevel", typeof(int));
            var countScaling = ExactMethod(
                consumable, "GetCountScalingInfo", scaling, count);
            var basicScaling = ExactMethod(
                scaling, "Basic", scaling, PublicStatic, typeof(BigDouble));
            var getTargeting = ExactMethod(options, "GetTargeting", selection);
            var getRandomList = structure.GetMethod(
                "GetRandomList",
                PublicInstance,
                null,
                new[] { scaling },
                null);
            if (onUse is null ||
                CollectionElementType(onUse.FieldType) != block ||
                scripts is null ||
                targetOptions?.FieldType != options ||
                strongest is null ||
                strongestLevel is null ||
                countScaling is null ||
                basicScaling is null ||
                getTargeting is null ||
                getRandomList is null ||
                !typeof(IEnumerable).IsAssignableFrom(getRandomList.ReturnType))
            {
                return false;
            }

            bindings = new Bindings(
                consumable, count, scaling, block, request, options, structure,
                onUse, scripts, targetOptions, strongest, strongestLevel,
                countScaling, basicScaling, getTargeting, getRandomList);
            reason = string.Empty;
            return true;
        }

        private static MethodInfo? ExactMethod(
            Type type,
            string name,
            Type returnType,
            params Type[] parameters) =>
            ExactMethod(type, name, returnType, PublicInstance, parameters);

        private static MethodInfo? ExactMethod(
            Type type,
            string name,
            Type returnType,
            BindingFlags flags,
            params Type[] parameters)
        {
            var method = type.GetMethod(name, flags, null, parameters, null);
            return method?.ReturnType == returnType ? method : null;
        }

        private static Type? CollectionElementType(Type type)
        {
            if (type.IsGenericType && type.GetGenericArguments().Length == 1)
                return type.GetGenericArguments()[0];
            foreach (var candidate in type.GetInterfaces())
            {
                if (candidate.IsGenericType &&
                    candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                {
                    return candidate.GetGenericArguments()[0];
                }
            }
            return null;
        }
    }
}
