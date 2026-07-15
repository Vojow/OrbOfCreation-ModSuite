using System;
using BepInEx.Logging;

namespace OrbAchievementResonance;

internal sealed class NativeEffectBlockBuilder
{
    private readonly ResonanceConfig _config;
    private readonly ManualLogSource _log;

    public NativeEffectBlockBuilder(ResonanceConfig config, ManualLogSource log)
    {
        _config = config;
        _log = log;
    }

    public bool TryCreate(
        ResonanceTarget descriptor,
        object nativeTarget,
        double nativeRatio,
        out object block,
        out NativeEffectBinding binding)
    {
        block = null!;
        binding = null!;

        var modifier = CreateModifier(descriptor, nativeRatio);
        if (modifier is null)
        {
            _log.LogWarning($"Skipping {descriptor.Name}: could not create native ValueModifier.Stacking(Guid, BigDouble).");
            return false;
        }

        var scriptType = ResolveEffectScriptType(descriptor.Kind);
        if (scriptType is null)
        {
            _log.LogWarning($"Skipping {descriptor.Name}: could not resolve native effect script type.");
            return false;
        }

        var script = NativeReflection.CreateInstance(scriptType);
        if (script is null)
        {
            _log.LogWarning($"Skipping {descriptor.Name}: could not instantiate {scriptType.FullName}.");
            return false;
        }

        var targetAssigned = NativeReflection.TrySetSingleAssignableMember(script, nativeTarget);
        var modifierAssigned = NativeReflection.TrySetNamedAssignableMember(script, modifier, "modifier", "Modifier");
        NativeReflection.TrySetFirstStringMember(script, descriptor.PropertyName, "property", "Property", "propertyName", "PropertyName", "propertyType", "PropertyType", "targetProperty", "TargetProperty");

        if (!targetAssigned || !modifierAssigned)
        {
            LogIncompleteScript(descriptor, targetAssigned, modifierAssigned);
            return false;
        }

        var blockType = NativeReflection.FindType("PersistentEffectBlock");
        if (blockType is null)
        {
            _log.LogWarning($"Skipping {descriptor.Name}: could not resolve PersistentEffectBlock.");
            return false;
        }

        var createdBlock = NativeReflection.CreateInstance(blockType);
        if (createdBlock is null)
        {
            _log.LogWarning($"Skipping {descriptor.Name}: could not instantiate PersistentEffectBlock.");
            return false;
        }

        var scriptAssigned = NativeReflection.TryAddToNamedCollection(createdBlock, script, "effectScripts", "EffectScripts");

        if (!scriptAssigned)
        {
            _log.LogWarning($"{descriptor.Name}: PersistentEffectBlock.effectScripts did not accept the native script.");
        }

        block = createdBlock;
        binding = new NativeEffectBinding(descriptor, script);
        return scriptAssigned;
    }

    public bool TryUpdateModifier(NativeEffectBinding binding, double nativeRatio)
    {
        var modifier = CreateModifier(binding.Descriptor, nativeRatio);
        if (modifier is null)
        {
            return false;
        }

        return NativeReflection.TrySetNamedAssignableMember(binding.Script, modifier, "modifier", "Modifier");
    }

    private static Type? ResolveEffectScriptType(ResonanceTargetKind kind)
    {
        switch (kind)
        {
            case ResonanceTargetKind.NumberVariable:
                return NativeReflection.FindType("NumberVariable+PersistentEffect");
            case ResonanceTargetKind.AttributeGroup:
            case ResonanceTargetKind.ResourceTypeProperty:
                return NativeReflection.FindType("UpgradeableObject+UpgradeEffectModifier");
            default:
                return null;
        }
    }

    private object? CreateModifier(ResonanceTarget descriptor, double nativeRatio)
    {
        var nativeRate = CalculateNativePerLevelRate(descriptor, nativeRatio);
        return NativeReflection.CreateStackingModifier(descriptor.ModifierUuid, nativeRate);
    }

    private double CalculateNativePerLevelRate(ResonanceTarget descriptor, double nativeRatio)
    {
        if (double.IsNaN(nativeRatio) || double.IsInfinity(nativeRatio) || nativeRatio <= 0.0)
        {
            return 0.0;
        }

        var bonus = _config.GetBonus(descriptor.Category);
        var rate = bonus.PerStrengthRate.Value;
        if (double.IsNaN(rate) || double.IsInfinity(rate) || rate <= 0.0)
        {
            return 0.0;
        }

        var strengthDivisor = _config.StrengthDivisor.Value;
        if (double.IsNaN(strengthDivisor) || double.IsInfinity(strengthDivisor) || strengthDivisor < 1.0)
        {
            strengthDivisor = 1.0;
        }

        var totalLogMultiplier = nativeRatio / strengthDivisor * Math.Log(1.0 + rate);
        var cap = bonus.MaximumMultiplier.Value;
        if (!double.IsNaN(cap) && !double.IsInfinity(cap) && cap > 1.0)
        {
            totalLogMultiplier = Math.Min(totalLogMultiplier, Math.Log(cap));
        }

        return Math.Exp(totalLogMultiplier / nativeRatio) - 1.0;
    }

    private void LogIncompleteScript(ResonanceTarget descriptor, bool targetAssigned, bool modifierAssigned)
    {
        if (!targetAssigned)
        {
            _log.LogWarning($"Skipping {descriptor.Name}: effect script target member was missing or ambiguous.");
        }

        if (!modifierAssigned)
        {
            _log.LogWarning($"Skipping {descriptor.Name}: effect script ValueModifier member was missing or ambiguous.");
        }
    }
}

internal sealed class NativeEffectBinding
{
    public NativeEffectBinding(ResonanceTarget descriptor, object script)
    {
        Descriptor = descriptor;
        Script = script;
    }

    public ResonanceTarget Descriptor { get; }

    public object Script { get; }
}
