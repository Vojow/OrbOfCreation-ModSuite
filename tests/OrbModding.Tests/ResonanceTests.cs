using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using OrbAchievementResonance;
using Xunit;

namespace OrbModding.Tests;

public sealed class ResonanceTests
{
    [Fact]
    public void AssignableMemberSafety_RejectsAmbiguousTargets()
    {
        var holder = new AmbiguousTargetHolder();

        var assigned = NativeReflection.TrySetSingleAssignableMember(holder, new FakeNumberVariable());

        Assert.False(assigned);
        Assert.Null(holder.First);
        Assert.Null(holder.Second);
    }

    [Fact]
    public void Builder_ConstructsOwnedNativeNumberBlockAndEnforcesCapAcrossStrengthChanges()
    {
        var config = new ResonanceConfig(new ConfigFile());
        config.Speed.PerStrengthRate.Value = 0.10;
        config.Speed.MaximumMultiplier.Value = 1.25;
        var builder = new NativeEffectBlockBuilder(config, new ManualLogSource());
        var target = CreateTarget(ResonanceTargetKind.NumberVariable);

        var created = builder.TryCreate(target, new FakeNumberVariable(), 10.0, out var rawBlock, out var binding);

        Assert.True(created);
        var block = Assert.IsType<PersistentEffectBlock>(rawBlock);
        var script = Assert.IsType<NumberVariable.PersistentEffect>(Assert.Single(block.effectScripts));
        Assert.Equal(Guid.Parse(ResonanceModifierIds.GlobalSpeed), script.modifier.GetGuid());
        Assert.Equal(1.25, AppliedMultiplier(script.modifier, 10.0), 10);
        Assert.True(NativeReflection.ContainsOwnedUuid(block));

        Assert.True(builder.TryUpdateModifier(binding, 2.0));
        Assert.Equal(Math.Pow(1.10, 2.0), AppliedMultiplier(script.modifier, 2.0), 10);

        Assert.True(builder.TryUpdateModifier(binding, 100.0));
        Assert.Equal(1.25, AppliedMultiplier(script.modifier, 100.0), 10);
    }

    [Fact]
    public void Builder_ConstructsNativeUpgradeableBlockWithPropertyType()
    {
        var config = new ResonanceConfig(new ConfigFile());
        var builder = new NativeEffectBlockBuilder(config, new ManualLogSource());
        var target = CreateTarget(ResonanceTargetKind.AttributeGroup);
        var nativeTarget = new UpgradeableObject();

        var created = builder.TryCreate(target, nativeTarget, 5.0, out var rawBlock, out _);

        Assert.True(created);
        var block = Assert.IsType<PersistentEffectBlock>(rawBlock);
        var script = Assert.IsType<UpgradeableObject.UpgradeEffectModifier>(Assert.Single(block.effectScripts));
        Assert.Same(nativeTarget, script.upgradeableObject);
        Assert.Equal("Value", script.propertyType);
        Assert.Equal(Guid.Parse(ResonanceModifierIds.GlobalSpeed), script.modifier.GetGuid());
    }

    [Fact]
    public void MissingLifecycleHooks_DisablePatchWithoutThrowing()
    {
        Assert.False(PlayerManagerStartPatch.TryInstall(new Harmony("test")));
    }

    [Fact]
    public void Cleanup_RemovesNestedOwnedModifierBlockOnly()
    {
        var config = new ResonanceConfig(new ConfigFile());
        var builder = new NativeEffectBlockBuilder(config, new ManualLogSource());
        Assert.True(builder.TryCreate(
            CreateTarget(ResonanceTargetKind.NumberVariable),
            new FakeNumberVariable(),
            3.0,
            out var ownedBlock,
            out _));
        var owner = new FakeBlockOwner();
        owner.persistentEffectBlocks.Add(ownedBlock);
        owner.persistentEffectBlocks.Add(new FakeOwnedBlock("11111111-1111-1111-1111-111111111111"));
        Assert.True(PersistentEffectBlockList.TryFind(owner, out var blocks));
        Assert.True(blocks.ContainsOwnedBlock(ResonanceModifierIds.GlobalSpeed));

        var removed = blocks.RemoveOwnedBlocks();

        Assert.Equal(1, removed);
        var foreign = Assert.IsType<FakeOwnedBlock>(Assert.Single(owner.persistentEffectBlocks));
        Assert.Equal("11111111-1111-1111-1111-111111111111", foreign.uuid);
    }

    [Fact]
    public void OwnershipScan_DoesNotTraverseNativeTargetGraph()
    {
        var block = new PersistentEffectBlock();
        block.effectScripts.Add(new NumberVariable.PersistentEffect
        {
            numberVariable = new FakeNumberVariable { uuid = ResonanceModifierIds.GlobalSpeed },
            modifier = ValueModifier.Stacking(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                new BigDouble(0.1)),
        });

        Assert.False(NativeReflection.ContainsOwnedUuid(block));
    }

    [Fact]
    public void DefaultConfiguration_KeepsNativeMutationOffAndOnlySpeedSelected()
    {
        var config = new ResonanceConfig(new ConfigFile());

        Assert.True(config.Enabled.Value);
        Assert.False(config.ApplyNativeEffectBlocks.Value);
        Assert.True(config.Speed.Enabled.Value);
        Assert.False(config.Power.Enabled.Value);
        Assert.False(config.Duration.Enabled.Value);
        Assert.False(config.Special.Enabled.Value);
        Assert.False(config.ResourceRate.Enabled.Value);
        Assert.False(config.ResourceCapacity.Enabled.Value);
        Assert.False(config.Casting.Enabled.Value);
        Assert.False(config.CastingProgression.Enabled.Value);
    }

    [Fact]
    public void ModifierOwnership_IsCaseInsensitiveAndRejectsForeignIds()
    {
        Assert.True(ResonanceModifierIds.IsOwned(ResonanceModifierIds.GlobalSpeed.ToUpperInvariant()));
        Assert.False(ResonanceModifierIds.IsOwned("11111111-1111-1111-1111-111111111111"));
        Assert.False(ResonanceModifierIds.IsOwned(null));
    }

    private static ResonanceTarget CreateTarget(ResonanceTargetKind kind)
    {
        return new ResonanceTarget(
            "test",
            "target-id",
            ResonanceModifierIds.GlobalSpeed,
            ResonanceBonusCategory.Speed,
            kind,
            "Value",
            "test target");
    }

    private static double AppliedMultiplier(ValueModifier modifier, double nativeRatio)
    {
        return Math.Pow(1.0 + modifier.PerLevelRate, nativeRatio);
    }
}

public sealed class FakeNumberVariable
{
    public string? uuid;
}

public sealed class AmbiguousTargetHolder
{
    public object? First;
    public object? Second;
}

public sealed class BigDouble
{
    public BigDouble(double value)
    {
        Value = value;
    }

    public double Value { get; }
}

public sealed class ValueModifier
{
    private ValueModifier(Guid guid, double perLevelRate)
    {
        Guid = guid;
        PerLevelRate = perLevelRate;
    }

    public Guid Guid { get; }

    public double PerLevelRate { get; }

    public Guid GetGuid() => Guid;

    public static ValueModifier Stacking(Guid guid, BigDouble rate) => new ValueModifier(guid, rate.Value);
}

public interface IPersistentEffectScript
{
}

public sealed class NumberVariable
{
    public sealed class PersistentEffect : IPersistentEffectScript
    {
        public FakeNumberVariable numberVariable = null!;
        public ValueModifier modifier = null!;
    }
}

public class UpgradeableObject
{
    public sealed class UpgradeEffectModifier : IPersistentEffectScript
    {
        public UpgradeableObject upgradeableObject = null!;
        public string propertyType = string.Empty;
        public int propertyIndex;
        public ValueModifier modifier = null!;
    }
}

public sealed class PersistentEffectBlock
{
    public List<IPersistentEffectScript> effectScripts = new List<IPersistentEffectScript>();
}

public sealed class FakeBlockOwner
{
    public List<object> persistentEffectBlocks = new List<object>();
}

public sealed class FakeOwnedBlock
{
    public FakeOwnedBlock(string uuid)
    {
        this.uuid = uuid;
    }

    public string uuid;
}
