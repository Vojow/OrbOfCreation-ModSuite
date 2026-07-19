using System;
using System.Collections;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;
using UnityEngine;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutomationAdmissionAdapterTests
{
    [Fact]
    public void AutoBuyAndAutoCastAdaptersProduceEquivalentCompletePolicyFacts()
    {
        var cost = new ResourceAdmissionCost(
            "mana",
            "Mana",
            new BigAmount(2.0, 0),
            new BigAmount(10.0, 0));

        var buy = AutoBuyAdmissionAdapter.Capture(new BuyCandidate(cost));
        var cast = AutoCastAdmissionAdapter.Capture(new CastCandidate(cost));

        Assert.True(AutomationAdmissionPolicy.HasCompleteContract(buy, out var buyReason), buyReason);
        Assert.True(AutomationAdmissionPolicy.HasCompleteContract(cast, out var castReason), castReason);
        Assert.True(buy.IsAvailable);
        Assert.True(cast.IsAvailable);
        Assert.True(buy.NativeAdmissionAccepted);
        Assert.True(cast.NativeAdmissionAccepted);
        Assert.Equal(buy.ImmediateCosts, cast.ImmediateCosts);
        Assert.Equal(1, buy.RequiredQueueSlots);
        Assert.Equal(0, cast.RequiredQueueSlots);
    }

    [Fact]
    public void SharedPolicyFailsClosedWhenAnyAdapterFactIsUnknown()
    {
        var snapshot = new AutomationAdmissionSnapshot(
            AutomationActionFamily.ScrollConsumption,
            new AutomationAdmissionIdentity("scroll", "ScrollSO"),
            availabilityKnown: true,
            isAvailable: true,
            availabilityReason: string.Empty,
            nativeAdmissionKnown: true,
            nativeAdmissionAccepted: true,
            nativeAdmissionReason: string.Empty,
            immediateCostsKnown: false,
            immediateCosts: Array.Empty<ResourceAdmissionCost>(),
            drainCostsKnown: true,
            drainCosts: Array.Empty<ResourceAdmissionCost>(),
            queueRequirementKnown: true,
            requiredQueueSlots: 0);

        Assert.False(AutomationAdmissionPolicy.HasCompleteContract(snapshot, out var reason));
        Assert.Equal("immediate native costs are unknown", reason);
    }

    [Fact]
    public void SharedPolicyRejectsKnownFlagWithNullVector()
    {
        var snapshot = new AutomationAdmissionSnapshot(
            AutomationActionFamily.HarvestAction,
            new AutomationAdmissionIdentity("harvest", "HarvestSO"),
            true, true, string.Empty,
            true, true, string.Empty,
            true, null!,
            true, Array.Empty<ResourceAdmissionCost>(),
            true, 0);

        Assert.False(AutomationAdmissionPolicy.HasCompleteContract(snapshot, out var reason));
        Assert.Equal("immediate native costs are unknown", reason);
    }

    [Fact]
    public void AutoBuyAdapterDistinguishesUnknownAvailabilityFromLocked()
    {
        var snapshot = AutoBuyAdmissionAdapter.Capture(new AvailabilityUnknownBuyCandidate(
            new ResourceAdmissionCost("mana", "Mana", new BigAmount(1.0, 0), new BigAmount(10.0, 0))));

        Assert.False(snapshot.AvailabilityKnown);
        Assert.False(AutomationAdmissionPolicy.HasCompleteContract(snapshot, out var reason));
        Assert.Equal("native availability is unknown", reason);
    }

    [Fact]
    public void DerivedNativePurchaseAndSpellTypesFailClosed()
    {
        var snapshots = new AutoBuyResourceSnapshotCache(new EmptyResourceReader(), (_, _, _, _) => { });
        var purchase = new ReflectionAutoBuyCandidate(
            new DerivedStructureSO(),
            AutoBuyCandidateKind.Structure,
            snapshots);
        using var catalog = new ReflectionAutoCastCatalog();
        var cast = new ReflectionAutoCastCandidate(
            catalog,
            new DerivedSpell(new SpellRecipeSO { uuid = "33333333-3333-3333-3333-333333333333" }),
            0);

        Assert.False(purchase.HasCompleteNativeContract);
        Assert.False(cast.TryGetIdentity(out _, out var reason));
        Assert.Contains("exact audited Spell type", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void SpellCostReaderRejectsEntireVectorWhenOneEntryIsMalformed()
    {
        var resource = new CostResourceSO
        {
            uuid = "mana",
            quantity = new CostBigDouble(10.0, 0),
        };
        var container = new object[]
        {
            new CostEntry(resource, new CostBigDouble(1.0, 0)),
            new MalformedCostEntry(resource),
        };

        Assert.False(ReflectionCostReader.TryRead(container, out var costs, out var reason));
        Assert.Empty(costs);
        Assert.Contains("could not be decoded completely", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void SpellCostReaderRejectsMixedVectorWithOverDeepEntry()
    {
        var valid = CreateCostEntry(1.0);
        object deep = CreateCostEntry(2.0);
        for (var index = 0; index < 4; index++) deep = new CostWrapper(deep);

        Assert.False(ReflectionCostReader.TryRead(new[] { valid, deep }, out var costs, out var reason));
        Assert.Empty(costs);
        Assert.Contains("nesting depth", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void SpellCostReaderRejectsNullEntry()
    {
        Assert.False(ReflectionCostReader.TryRead(new object?[] { CreateCostEntry(1.0), null }, out var costs, out var reason));
        Assert.Empty(costs);
        Assert.Contains("null entry", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void SpellCostReaderRejectsWhollyOverDeepVector()
    {
        object deep = CreateCostEntry(1.0);
        for (var index = 0; index < 5; index++) deep = new CostWrapper(deep);

        Assert.False(ReflectionCostReader.TryRead(deep, out var costs, out var reason));
        Assert.Empty(costs);
        Assert.Contains("nesting depth", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void SpellCostReaderRejectsThrowingEnumeration()
    {
        Assert.False(ReflectionCostReader.TryRead(new ThrowingEnumerable(), out var costs, out var reason));
        Assert.Empty(costs);
        Assert.Contains("enumeration failed", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void SpellCostReaderAcceptsEmptyAndZeroCostVectors()
    {
        Assert.True(ReflectionCostReader.TryRead(Array.Empty<object>(), out var empty, out var emptyReason), emptyReason);
        Assert.Empty(empty);

        Assert.True(ReflectionCostReader.TryRead(new[] { CreateCostEntry(0.0) }, out var zero, out var zeroReason), zeroReason);
        Assert.True(Assert.Single(zero).Cost.IsZero);
    }

    [Fact]
    public void AssemblyHashMismatchFailsClosedBeforeNativeMutationSetup()
    {
        var matching = new AssemblyAuditResult(
            new AssemblyHashResult("main", "AA", "AA"),
            new AssemblyHashResult("first", "BB", "BB"));
        var mismatch = new AssemblyAuditResult(
            new AssemblyHashResult("main", "AA", "CC"),
            new AssemblyHashResult("first", "BB", "BB"));

        Assert.True(Plugin.AssemblyAuditAllowsMutation(matching));
        Assert.False(Plugin.AssemblyAuditAllowsMutation(mismatch));
    }

    private class BuyCandidate : IAutoBuyCandidate, IAutoBuyDirtyCandidate
    {
        private readonly IReadOnlyList<ResourceAdmissionCost> _costs;
        private readonly AutoBuyCandidateSnapshot _snapshot;

        public BuyCandidate(ResourceAdmissionCost cost)
        {
            _costs = new[] { cost };
            _snapshot = new AutoBuyCandidateSnapshot(this, "structure", "Structure", AutoBuyCandidateKind.Structure, "StructureSO");
        }

        public IReadOnlyList<string> ResourceDependencies => new[] { "mana" };
        public bool HasResolvedCosts => true;
        public AutoBuyCandidateSnapshot Snapshot() => _snapshot;
        public bool IsAvailable() => true;
        public bool CanPurchase(out string reason) { reason = string.Empty; return true; }
        public IReadOnlyList<ResourceAdmissionCost> GetCosts() => _costs;
        public bool TryPurchaseOne(out string reason) { reason = string.Empty; return true; }
        public void MarkDirty(AutoBuyDirtyReason reasons) { }
        public void SetLifecycleEvidence(AutoBuyLifecycleEvidence evidence) { }
    }

    private sealed class AvailabilityUnknownBuyCandidate : BuyCandidate, IAutoBuyAvailabilityEvidence
    {
        public AvailabilityUnknownBuyCandidate(ResourceAdmissionCost cost) : base(cost) { }
        public bool TryReadAvailability(out bool available) { available = false; return false; }
    }

    private sealed class DerivedStructureSO : global::StructureSO { }

    private sealed class DerivedSpell : global::Spell
    {
        public DerivedSpell(SpellRecipeSO recipe) : base(recipe) { }
    }

    private sealed class EmptyResourceReader : IAutoBuyResourceSnapshotReader
    {
        public bool TryRead(AutoBuyResourceDefinition definition, long epoch, out AutoBuyResourceSnapshot snapshot)
        {
            snapshot = default;
            return false;
        }
    }

    private sealed class CastCandidate : IAutoCastCandidate
    {
        private readonly IReadOnlyList<ResourceAdmissionCost> _costs;
        private readonly object _native = new();

        public CastCandidate(ResourceAdmissionCost cost) => _costs = new[] { cost };

        public int SlotIndex => 0;
        public string DisplayName => "Spell";
        public AutoCastSpellKind Kind => AutoCastSpellKind.Instant;
        public bool IsEmpty => false;
        public bool IsCharged => false;
        public bool IsCasting => false;
        public bool IsReadyingCast => false;
        public bool CanCast(out string reason) { reason = string.Empty; return true; }
        public bool TryGetImmediateCosts(out IReadOnlyList<ResourceAdmissionCost> costs) { costs = _costs; return true; }
        public bool TryGetDrainCosts(out IReadOnlyList<ResourceAdmissionCost> costs) { costs = Array.Empty<ResourceAdmissionCost>(); return true; }
        public bool HasValidTargets(out string reason) { reason = string.Empty; return true; }
        public bool TryFireAndResolveTargets(out string reason) { reason = string.Empty; return true; }
        public bool TryGetIdentity(out AutoCastCandidateIdentity identity, out string reason)
        {
            identity = new AutoCastCandidateIdentity("spell", _native, GetType(), SlotIndex);
            reason = string.Empty;
            return true;
        }
        public bool TrySetChargeHold(bool isHolding, out string reason) { reason = string.Empty; return true; }
    }

    private sealed class CostEntry
    {
        public CostEntry(CostResourceSO resource, CostBigDouble amount)
        {
            this.resource = resource;
            this.amount = amount;
        }

        public readonly CostResourceSO resource;
        public readonly CostBigDouble amount;
    }

    private static CostEntry CreateCostEntry(double amount)
    {
        return new CostEntry(
            new CostResourceSO { uuid = "mana", quantity = new CostBigDouble(10.0, 0) },
            new CostBigDouble(amount, 0));
    }

    private sealed class CostWrapper
    {
        public CostWrapper(object value) => costs = value;
        public readonly object costs;
    }

    private sealed class ThrowingEnumerable : IEnumerable
    {
        public IEnumerator GetEnumerator() => throw new InvalidOperationException("boom");
    }

    private sealed class MalformedCostEntry
    {
        public MalformedCostEntry(CostResourceSO resource) => this.resource = resource;
        public readonly CostResourceSO resource;
        public readonly string amount = "not-a-number";
    }

    private sealed class CostResourceSO : ScriptableObject
    {
        public string uuid = string.Empty;
        public CostBigDouble quantity = new(0.0, 0);
        public CostBigDouble GetTrueQuantity() => quantity;
    }

    private sealed class CostBigDouble
    {
        public CostBigDouble(double mantissa, long exponent)
        {
            this.mantissa = mantissa;
            this.exponent = exponent;
        }

        public readonly double mantissa;
        public readonly long exponent;
    }
}
