using System;
using System.Collections.Generic;
using BepInEx.Logging;

namespace OrbAchievementResonance;

internal sealed class ResonanceRuntime
{
    private readonly ResonanceConfig _config;
    private readonly ManualLogSource _log;
    private readonly NativeEffectBlockBuilder _builder;
    private readonly List<NativeEffectBinding> _bindings = new List<NativeEffectBinding>();
    private object? _achievementStrength;
    private bool _loggedMutationDisabled;
    private bool _loggedModifierRefreshFailure;

    public ResonanceRuntime(ResonanceConfig config, ManualLogSource log)
    {
        _config = config;
        _log = log;
        _builder = new NativeEffectBlockBuilder(config, log);
    }

    public void LogTargetCatalog()
    {
        foreach (var target in ResonanceTargetCatalog.All)
        {
            var bonus = _config.GetBonus(target.Category);
            _log.LogInfo(
                $"Resonance target {target.Category}/{target.Name}: target={target.TargetUuid}, " +
                $"property={target.PropertyName}, modifier={target.ModifierUuid}, enabled={bonus.Enabled.Value}, " +
                $"rate={bonus.PerStrengthRate.Value:0.########}, max={bonus.MaximumMultiplier.Value:0.###}. {target.Notes}");
        }
    }

    public void BeforePlayerManagerStart(object player)
    {
        _achievementStrength = ResolveAchievementStrength(player);
        if (_achievementStrength is null)
        {
            _log.LogWarning("Achievement Resonance could not resolve Player.GetAchievementLevel() or the AchievementStrength asset; injection skipped.");
            return;
        }

        if (!PersistentEffectBlockList.TryFind(_achievementStrength, out var blockList))
        {
            _log.LogWarning("Achievement Strength did not expose persistentEffectBlocks; injection skipped.");
            return;
        }

        if (!_config.ApplyNativeEffectBlocks.Value)
        {
            if (!_loggedMutationDisabled)
            {
                _log.LogInfo("Observed Player.ManagerStart. Native effect mutation is disabled by ApplyNativeEffectBlocks=false.");
                _loggedMutationDisabled = true;
            }

            return;
        }

        if (!_config.RemoveExistingOwnedBlocksBeforeInject.Value)
        {
            _log.LogWarning(
                "Achievement Resonance native injection requires RemoveExistingOwnedBlocksBeforeInject=true " +
                "so capped modifiers can be rebound and refreshed safely; injection skipped.");
            return;
        }

        var removed = blockList.RemoveOwnedBlocks();
        if (removed > 0)
        {
            _log.LogInfo($"Removed {removed} pre-existing Resonance-owned Achievement Strength effect block(s) before injection.");
        }

        _bindings.Clear();
        var nativeRatio = ResolveNativeRatio(_achievementStrength, 1);

        var added = 0;
        var skipped = 0;
        foreach (var descriptor in ResonanceTargetCatalog.All)
        {
            if (!ShouldApply(descriptor))
            {
                skipped++;
                continue;
            }

            if (blockList.ContainsOwnedBlock(descriptor.ModifierUuid))
            {
                if (_config.LogSkippedTargets.Value)
                {
                    _log.LogInfo($"Skipping {descriptor.Name}: Resonance-owned modifier block already exists.");
                }

                skipped++;
                continue;
            }

            var nativeTarget = ResolveTarget(descriptor);
            if (nativeTarget is null)
            {
                _log.LogWarning($"Skipping {descriptor.Name}: target UUID {descriptor.TargetUuid} was not found in loaded Unity assets.");
                skipped++;
                continue;
            }

            if (!_builder.TryCreate(descriptor, nativeTarget, nativeRatio, out var block, out var binding))
            {
                skipped++;
                continue;
            }

            if (!blockList.Add(block))
            {
                _log.LogWarning($"Skipping {descriptor.Name}: persistentEffectBlocks could not accept the native block.");
                skipped++;
                continue;
            }

            _bindings.Add(binding);
            added++;
            _log.LogInfo($"Injected Achievement Resonance block {descriptor.Name} with modifier {descriptor.ModifierUuid}.");
        }

        _log.LogInfo($"Achievement Resonance injection complete. Added={added}, skipped={skipped}.");
    }

    public void BeforeNumberVariableApplyEffects(object numberVariable, int bonusLevels)
    {
        if (_achievementStrength is null || !ReferenceEquals(numberVariable, _achievementStrength) || _bindings.Count == 0)
        {
            return;
        }

        var nativeRatio = ResolveNativeRatio(numberVariable, bonusLevels);
        foreach (var binding in _bindings)
        {
            if (_builder.TryUpdateModifier(binding, nativeRatio))
            {
                continue;
            }

            if (!_loggedModifierRefreshFailure)
            {
                _loggedModifierRefreshFailure = true;
                _log.LogError("Achievement Resonance could not refresh a capped native modifier; disabling further native mutation is recommended.");
            }
        }
    }

    public void CleanupOwnedBlocks()
    {
        var achievementStrength = _achievementStrength ?? ResolveAchievementStrength(null);
        if (achievementStrength is null)
        {
            _log.LogInfo("Cleanup skipped: Achievement Strength was not resolved.");
            return;
        }

        if (!PersistentEffectBlockList.TryFind(achievementStrength, out var blockList))
        {
            _log.LogInfo("Cleanup skipped: Achievement Strength persistentEffectBlocks were not resolved.");
            return;
        }

        var removed = blockList.RemoveOwnedBlocks();
        _bindings.Clear();
        _log.LogInfo($"Cleanup removed {removed} Resonance-owned Achievement Strength effect block(s). Native active modifiers are not broadly removed.");
    }

    private bool ShouldApply(ResonanceTarget descriptor)
    {
        var bonus = _config.GetBonus(descriptor.Category);
        if (!bonus.Enabled.Value)
        {
            if (_config.LogSkippedTargets.Value)
            {
                _log.LogInfo($"Skipping {descriptor.Name}: {descriptor.Category} category is disabled.");
            }

            return false;
        }

        var rate = bonus.PerStrengthRate.Value;
        if (double.IsNaN(rate) || double.IsInfinity(rate) || rate <= 0.0)
        {
            if (_config.LogSkippedTargets.Value)
            {
                _log.LogInfo($"Skipping {descriptor.Name}: {descriptor.Category}.PerStrengthRate is not positive.");
            }

            return false;
        }

        var cap = bonus.MaximumMultiplier.Value;
        if (double.IsNaN(cap) || double.IsInfinity(cap) || cap <= 1.0)
        {
            if (_config.LogSkippedTargets.Value)
            {
                _log.LogInfo($"Skipping {descriptor.Name}: {descriptor.Category}.MaximumMultiplier must be finite and greater than 1.");
            }

            return false;
        }

        return true;
    }

    private object? ResolveAchievementStrength(object? player)
    {
        if (player is not null)
        {
            var achievement = NativeReflection.InvokeStaticParameterless(player.GetType(), "GetAchievementLevel");
            if (achievement is not null)
            {
                return achievement;
            }
        }

        return NativeReflection.FindAssetByUuid(
            ResonanceTargetCatalog.AchievementStrengthUuid,
            "IntVariable",
            "NumberVariable",
            "DoubleVariable");
    }

    private static double ResolveNativeRatio(object achievementStrength, int bonusLevels)
    {
        var level = NativeReflection.InvokeParameterless(achievementStrength, "GetLevel");
        if (!NativeReflection.TryConvertToDouble(level, out var numericLevel))
        {
            return 1.0;
        }

        return Math.Max(0.0, numericLevel - 1.0 + bonusLevels);
    }

    private static object? ResolveTarget(ResonanceTarget descriptor)
    {
        switch (descriptor.Kind)
        {
            case ResonanceTargetKind.AttributeGroup:
                return NativeReflection.FindAssetByUuid(
                    descriptor.TargetUuid,
                    "AttributeGroupSO",
                    "UpgradeableObject");
            case ResonanceTargetKind.ResourceTypeProperty:
                return NativeReflection.FindAssetByUuid(
                    descriptor.TargetUuid,
                    "ResourceTypeSO",
                    "UpgradeableObject");
            case ResonanceTargetKind.NumberVariable:
                return NativeReflection.FindAssetByUuid(
                    descriptor.TargetUuid,
                    "DoubleVariable",
                    "NumberVariable",
                    "IntVariable");
            default:
                return null;
        }
    }
}
