using System;
using System.Collections;
using System.Threading.Tasks;
using OrbAutomata;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests.Services.GenericDiscovery;

public sealed class GenericDiscoveryGameActionTests
{
    private const long Epoch = 71;
    private readonly IDictionary _registry = new Hashtable();

    [Theory]
    [InlineData("AlchemyRecipeSO")]
    [InlineData("EquipmentSO")]
    [InlineData("GlyphSO")]
    [InlineData("RitualSO")]
    [InlineData("TimeRuneSO")]
    public void Every_audited_concrete_type_pays_then_discovers_the_exact_target(string nativeType)
    {
        var target = Target(nativeType);
        var resource = Resource(90);
        Discoverable(target).GetDiscoverCost().costs.Add(
            new ResourceTuple(resource, new BigDouble(25, 0)));
        Register(target);
        using var boundary = Boundary();

        var result = Submit(boundary, target, nativeType);

        Assert.True(result.Verified, result.Reason);
        Assert.True(Discoverable(target).IsDiscovered());
        Assert.Equal(1, Discoverable(target).GetDiscoverCost().PerformCalls);
        Assert.Equal(0, resource.GetTrueQuantity().CompareTo(new BigDouble(65, 0)));
        Assert.Equal(new NativeMutationCallOutcome(2, 1, 1), result.CallOutcome);
        Assert.True(result.Receipt.PostconditionMatched);
        Assert.Single(result.Receipt.Costs);
    }

    [Fact]
    public void Visibility_native_verdict_and_already_discovered_refuse_before_payment()
    {
        var invisible = Target("GlyphSO");
        SetVisible(invisible, false);
        Register(invisible);
        var unavailable = Target("RitualSO");
        SetCanDiscover(unavailable, false);
        Register(unavailable);
        var complete = Target("TimeRuneSO");
        SetDiscovered(complete, true);
        Register(complete);
        using var boundary = Boundary();

        var hidden = Submit(boundary, invisible, "GlyphSO");
        var blocked = Submit(boundary, unavailable, "RitualSO");
        var already = Submit(boundary, complete, "TimeRuneSO");

        Assert.Equal(GenericDiscoveryPreflight.NotVisible, hidden.Preflight);
        Assert.Equal(GenericDiscoveryPreflight.DiscoveryUnavailable, blocked.Preflight);
        Assert.Equal(GenericDiscoveryPreflight.AlreadyDiscovered, already.Preflight);
        Assert.Equal(0, Discoverable(invisible).GetDiscoverCost().PerformCalls);
        Assert.Equal(0, Discoverable(unavailable).GetDiscoverCost().PerformCalls);
        Assert.Equal(0, Discoverable(complete).GetDiscoverCost().PerformCalls);
    }

    [Fact]
    public void Unaffordable_cost_refuses_before_payment_or_discovery()
    {
        var target = Target("AlchemyRecipeSO");
        var resource = Resource(4);
        Discoverable(target).GetDiscoverCost().costs.Add(
            new ResourceTuple(resource, new BigDouble(5, 0)));
        Register(target);
        using var boundary = Boundary();

        var result = Submit(boundary, target, "AlchemyRecipeSO");

        Assert.Equal(GenericDiscoveryPreflight.Unaffordable, result.Preflight);
        Assert.Equal(0, result.CallOutcome.NativeCallsAttempted);
        Assert.Equal(0, Discoverable(target).GetDiscoverCost().PerformCalls);
        Assert.False(Discoverable(target).IsDiscovered());
    }

    [Fact]
    public void Wrong_type_stale_lifecycle_and_revoked_permit_all_refuse_before_mutation()
    {
        var target = Target("AlchemyRecipeSO");
        Register(target);
        var epoch = Epoch;
        var permit = true;
        using var boundary = Boundary(() => epoch, () => permit);

        var wrongType = Submit(boundary, target, "GlyphSO");
        var stale = Submit(boundary, target, "AlchemyRecipeSO", Epoch - 1);
        permit = false;
        var revoked = Submit(boundary, target, "AlchemyRecipeSO");

        Assert.Equal(GenericDiscoveryPreflight.IdentityUnavailable, wrongType.Preflight);
        Assert.Equal(GenericDiscoveryPreflight.LifecycleReplaced, stale.Preflight);
        Assert.Equal(GenericDiscoveryPreflight.MutationPermitUnavailable, revoked.Preflight);
        Assert.Equal(0, Discoverable(target).GetDiscoverCost().PerformCalls);
    }

    [Fact]
    public void Missing_outcome_quarantines_until_lifecycle_invalidation()
    {
        var target = Target("EquipmentSO");
        SetSuppressDiscovery(target, true);
        Register(target);
        using var boundary = Boundary();

        var failed = Submit(boundary, target, "EquipmentSO");
        var quarantined = Submit(boundary, target, "EquipmentSO");
        SetSuppressDiscovery(target, false);
        boundary.InvalidateLifecycle();
        var retry = Submit(boundary, target, "EquipmentSO");

        Assert.Equal(GenericDiscoveryPreflight.VerificationFailed, failed.Preflight);
        Assert.Equal(NativeMutationOutcome.PostconditionFailed, failed.Outcome);
        Assert.Equal(GenericDiscoveryPreflight.Quarantined, quarantined.Preflight);
        Assert.True(retry.Verified, retry.Reason);
    }

    [Fact]
    public void Exception_after_requested_outcome_commits_without_quarantine()
    {
        var target = Target("RitualSO");
        SetThrowAfterDiscovery(target, true);
        Register(target);
        using var boundary = Boundary();

        var result = Submit(boundary, target, "RitualSO");

        Assert.True(result.Verified, result.Reason);
        Assert.True(Discoverable(target).IsDiscovered());
        Assert.False(boundary.IsQuarantined);
    }

    [Fact]
    public void Partial_payment_fault_without_discovery_is_evidence_and_quarantines()
    {
        var target = Target("TimeRuneSO");
        var first = Resource(10);
        var second = Resource(10);
        var cost = Discoverable(target).GetDiscoverCost();
        cost.costs.Add(new ResourceTuple(first, new BigDouble(2, 0)));
        cost.costs.Add(new ResourceTuple(second, new BigDouble(3, 0)));
        cost.ThrowAfterCostRows = 1;
        Register(target);
        using var boundary = Boundary();

        var result = Submit(boundary, target, "TimeRuneSO");

        Assert.Equal(GenericDiscoveryPreflight.PostCommitFault, result.Preflight);
        Assert.Equal(GenericDiscoveryNativeStage.Payment, result.Stage);
        Assert.True(result.Receipt.EvidenceAvailable);
        Assert.True(result.Receipt.PaymentInvoked);
        Assert.True(result.Receipt.ResourcesCharged);
        Assert.False(Discoverable(target).IsDiscovered());
        Assert.True(boundary.IsQuarantined);
    }

    [Fact]
    public async Task Unity_thread_is_revalidated_before_identity_or_payment()
    {
        var target = Target("GlyphSO");
        Register(target);
        using var boundary = Boundary();

        var result = await Task.Run(() => Submit(boundary, target, "GlyphSO"));

        Assert.Equal(GenericDiscoveryPreflight.WrongThread, result.Preflight);
        Assert.Equal(0, Discoverable(target).GetDiscoverCost().PerformCalls);
    }

    [Fact]
    public void Every_missing_member_disables_the_complete_lifecycle_binding_set()
    {
        foreach (var missing in GenericDiscoveryNativeBindings.ContractIds)
        {
            using var boundary = Boundary(includeContract: id => id != missing);
            Assert.False(boundary.BindingsAvailable);
            Assert.Contains(missing, boundary.BindingFailure, StringComparison.Ordinal);
        }
    }

    private GenericDiscoveryGameAction Boundary(
        Func<long>? epoch = null,
        Func<bool>? permit = null,
        Func<string, bool>? includeContract = null)
    {
        var readEpoch = epoch ?? (() => Epoch);
        var resolver = new TypedRegistryResolver(
            readEpoch,
            () => TypedRegistrySourceSnapshot.Ready(_registry),
            value => value is IHasGuid item ? item.GetGuid() : null);
        return new GenericDiscoveryGameAction(
            readEpoch,
            permit ?? (() => true),
            static () => "GenericDiscovery ownership was revoked.",
            includeContract: includeContract,
            registry: resolver);
    }

    private static GenericDiscoverySubmission Submit(
        GenericDiscoveryGameAction boundary,
        object target,
        string nativeType,
        long lifecycle = Epoch)
    {
        var action = new GenericDiscoveryAction(
            Discoverable(target).GetGuid(), nativeType, lifecycle);
        return boundary.Submit(in action);
    }

    private void Register(object target) =>
        _registry.Add(Discoverable(target).GetGuid(), target);

    private static object Target(string nativeType)
    {
        object value = nativeType switch
        {
            "AlchemyRecipeSO" => new AlchemyRecipeSO { discovered = false },
            "EquipmentSO" => new EquipmentSO { isCreated = false },
            "GlyphSO" => new GlyphSO { discovered = false },
            "RitualSO" => new RitualSO { discovered = false },
            "TimeRuneSO" => new TimeRuneSO { discovered = false },
            _ => throw new ArgumentOutOfRangeException(nameof(nativeType)),
        };
        ((IdScriptableObject)value).SetGuid(Guid.NewGuid());
        return value;
    }

    private static IDiscoverable Discoverable(object target) =>
        Assert.IsAssignableFrom<IDiscoverable>(target);

    private static ResourceSO Resource(double amount)
    {
        var resource = new ResourceSO { quantity = new BigDouble(amount, 0) };
        resource.SetGuid(Guid.NewGuid());
        return resource;
    }

    private static void SetVisible(object target, bool value)
    {
        switch (target)
        {
            case AlchemyRecipeSO item: item.NativeDiscoverVisible = value; break;
            case EquipmentSO item: item.NativeDiscoverVisible = value; break;
            case GlyphSO item: item.NativeDiscoverVisible = value; break;
            case RitualSO item: item.NativeDiscoverVisible = value; break;
            case TimeRuneSO item: item.NativeDiscoverVisible = value; break;
        }
    }

    private static void SetCanDiscover(object target, bool value)
    {
        switch (target)
        {
            case AlchemyRecipeSO item: item.NativeCanDiscover = value; break;
            case EquipmentSO item: item.NativeCanDiscover = value; break;
            case GlyphSO item: item.NativeCanDiscover = value; break;
            case RitualSO item: item.NativeCanDiscover = value; break;
            case TimeRuneSO item: item.NativeCanDiscover = value; break;
        }
    }

    private static void SetDiscovered(object target, bool value)
    {
        switch (target)
        {
            case AlchemyRecipeSO item: item.discovered = value; break;
            case EquipmentSO item: item.isCreated = value; break;
            case GlyphSO item: item.discovered = value; break;
            case RitualSO item: item.discovered = value; break;
            case TimeRuneSO item: item.discovered = value; break;
        }
    }

    private static void SetSuppressDiscovery(object target, bool value)
    {
        switch (target)
        {
            case AlchemyRecipeSO item: item.SuppressDiscovery = value; break;
            case EquipmentSO item: item.SuppressDiscovery = value; break;
            case GlyphSO item: item.SuppressDiscovery = value; break;
            case RitualSO item: item.SuppressDiscovery = value; break;
            case TimeRuneSO item: item.SuppressDiscovery = value; break;
        }
    }

    private static void SetThrowAfterDiscovery(object target, bool value)
    {
        switch (target)
        {
            case AlchemyRecipeSO item: item.ThrowAfterDiscovery = value; break;
            case EquipmentSO item: item.ThrowAfterDiscovery = value; break;
            case GlyphSO item: item.ThrowAfterDiscovery = value; break;
            case RitualSO item: item.ThrowAfterDiscovery = value; break;
            case TimeRuneSO item: item.ThrowAfterDiscovery = value; break;
        }
    }
}
