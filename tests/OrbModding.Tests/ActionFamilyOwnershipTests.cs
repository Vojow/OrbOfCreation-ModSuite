using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

public sealed class ActionFamilyOwnershipTests
{
    [Fact]
    public void IndependentFamiliesCanBeOwnedTogether()
    {
        var registry = new ActionFamilyOwnershipRegistry();
        Assert.True(registry.TryClaimSet(Owner("buy"), new[] { AutomationActionFamily.StructurePurchase }, out var buy, out _));
        Assert.True(registry.TryClaimSet(Owner("cast"), new[] { AutomationActionFamily.SpellCast }, out var cast, out _));

        using (buy)
        using (cast)
        {
            Assert.True(buy!.IsHeld);
            Assert.True(cast!.IsHeld);
        }
    }

    [Fact]
    public void DuplicateOwnerIsRejectedWithoutDisturbingCurrentLease()
    {
        var registry = new ActionFamilyOwnershipRegistry();
        using var first = Claim(registry, "first", AutomationActionFamily.StructurePurchase);

        Assert.False(registry.TryClaimSet(Owner("second"), new[] { AutomationActionFamily.StructurePurchase }, out var second, out var conflict));
        Assert.Null(second);
        Assert.Equal(AutomationActionFamily.StructurePurchase, conflict.Family);
        Assert.Equal("first", conflict.Owner.DisplayName);
        Assert.True(first.IsHeld);
    }

    [Fact]
    public void MultiFamilyClaimRollsBackWhenAnyFamilyConflicts()
    {
        var registry = new ActionFamilyOwnershipRegistry();
        using var blocker = Claim(registry, "blocker", AutomationActionFamily.NativeMultiBuyOverride);

        Assert.False(registry.TryClaimSet(
            Owner("upgrades"),
            new[] { AutomationActionFamily.UpgradePurchase, AutomationActionFamily.NativeMultiBuyOverride },
            out _, out _));

        using var upgradeOnly = Claim(registry, "successor", AutomationActionFamily.UpgradePurchase);
        Assert.True(upgradeOnly.IsHeld);
    }

    [Fact]
    public void KnownExternalRevokesEveryFamilyInOverlappingCooperativeSet()
    {
        var registry = new ActionFamilyOwnershipRegistry();
        using var suite = Claim(registry, "upgrades",
            AutomationActionFamily.UpgradePurchase,
            AutomationActionFamily.NativeMultiBuyOverride);

        using var external = registry.RegisterKnownExternal(
            Owner("external"),
            new[] { AutomationActionFamily.NativeMultiBuyOverride });

        Assert.False(suite.IsHeld);
        Assert.False(suite.Owns(AutomationActionFamily.UpgradePurchase));
        using var replacement = Claim(registry, "replacement", AutomationActionFamily.UpgradePurchase);
        Assert.True(replacement.IsHeld);
    }

    [Fact]
    public void ReleaseAllowsExplicitReacquisition()
    {
        var registry = new ActionFamilyOwnershipRegistry();
        var first = Claim(registry, "first", AutomationActionFamily.SpellCast);
        first.Dispose();

        using var second = Claim(registry, "second", AutomationActionFamily.SpellCast);
        Assert.False(first.IsHeld);
        Assert.True(second.IsHeld);
    }

    [Fact]
    public void DisposedStaleLeaseCannotReleaseSuccessor()
    {
        var registry = new ActionFamilyOwnershipRegistry();
        var first = Claim(registry, "first", AutomationActionFamily.ConceptAssignment);
        first.Dispose();
        using var second = Claim(registry, "second", AutomationActionFamily.ConceptAssignment);

        first.Dispose();

        Assert.True(second.IsHeld);
        Assert.False(registry.TryClaimSet(Owner("third"), new[] { AutomationActionFamily.ConceptAssignment }, out _, out _));
    }

    [Fact]
    public void RemovingExternalDoesNotReviveRevokedLease()
    {
        var registry = new ActionFamilyOwnershipRegistry();
        using var suite = Claim(registry, "suite", AutomationActionFamily.StructurePurchase);
        var external = registry.RegisterKnownExternal(Owner("external"), new[] { AutomationActionFamily.StructurePurchase });

        external.Dispose();

        Assert.False(suite.IsHeld);
        using var replacement = Claim(registry, "replacement", AutomationActionFamily.StructurePurchase);
        Assert.True(replacement.IsHeld);
    }

    private static ActionFamilyLeaseSet Claim(
        ActionFamilyOwnershipRegistry registry,
        string name,
        params AutomationActionFamily[] families)
    {
        Assert.True(registry.TryClaimSet(Owner(name), families, out var lease, out var conflict),
            $"Unexpected conflict with {conflict.Owner.DisplayName} on {conflict.Family}.");
        return lease!;
    }

    private static ActionFamilyOwner Owner(string name) =>
        new(new FeatureStatusKey("tests", name), name);
}
