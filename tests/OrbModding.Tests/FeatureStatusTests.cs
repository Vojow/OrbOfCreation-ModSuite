using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

public sealed class FeatureStatusTests
{
    private static readonly FeatureStatusKey Key = new("plugin.test", "feature.test");

    [Fact]
    public void PublicCodes_AreAppendOnlyAndStable()
    {
        Assert.Equal(
            new Dictionary<string, int>
            {
                ["ConfigurationDisabled"] = 0,
                ["Locked"] = 1,
                ["NotReady"] = 2,
                ["Operational"] = 3,
                ["TemporarilyBlocked"] = 4,
                ["ContractUnavailable"] = 5,
                ["Degraded"] = 6,
                ["Faulted"] = 7,
            },
            Enum.GetValues<FeatureStatusState>().ToDictionary(value => value.ToString(), value => (int)value));
        Assert.Equal(
            new Dictionary<string, int>
            {
                ["None"] = 0,
                ["ConfigurationDisabled"] = 100,
                ["ParentFeatureDisabled"] = 101,
                ["EmergencyDisabled"] = 102,
                ["ProgressionLocked"] = 200,
                ["GameplayNotReady"] = 300,
                ["RegistryNotReady"] = 301,
                ["LifecycleTransition"] = 302,
                ["QueueNotReady"] = 303,
                ["Initializing"] = 304,
                ["TemporarySafetyBlock"] = 400,
                ["QueueFull"] = 401,
                ["NativeBusy"] = 402,
                ["ManualPause"] = 403,
                ["TargetingInProgress"] = 404,
                ["CapacityExceeded"] = 405,
                ["MutationQuarantined"] = 406,
                ["ActionFamilyConflict"] = 407,
                ["ContractUnavailable"] = 500,
                ["ContractMismatch"] = 501,
                ["IdentityMismatch"] = 502,
                ["EvidenceUnavailable"] = 503,
                ["PartialCapabilityUnavailable"] = 600,
                ["NativeMutationFailed"] = 700,
                ["PostconditionFailed"] = 701,
                ["RuntimeFailure"] = 702,
                ["InvariantViolation"] = 703,
            },
            Enum.GetValues<FeatureStatusReasonCode>().ToDictionary(value => value.ToString(), value => (int)value));
    }

    [Theory]
    [InlineData(FeatureStatusState.ConfigurationDisabled, false, FeatureStatusReasonCode.ConfigurationDisabled)]
    [InlineData(FeatureStatusState.Locked, true, FeatureStatusReasonCode.ProgressionLocked)]
    [InlineData(FeatureStatusState.NotReady, true, FeatureStatusReasonCode.GameplayNotReady)]
    [InlineData(FeatureStatusState.Operational, true, FeatureStatusReasonCode.None)]
    [InlineData(FeatureStatusState.TemporarilyBlocked, true, FeatureStatusReasonCode.TemporarySafetyBlock)]
    [InlineData(FeatureStatusState.ContractUnavailable, true, FeatureStatusReasonCode.ContractUnavailable)]
    [InlineData(FeatureStatusState.Degraded, true, FeatureStatusReasonCode.PartialCapabilityUnavailable)]
    [InlineData(FeatureStatusState.Faulted, true, FeatureStatusReasonCode.RuntimeFailure)]
    public void Snapshot_RepresentsEveryRequiredRuntimeState(
        FeatureStatusState state,
        bool configuredEnabled,
        FeatureStatusReasonCode reasonCode)
    {
        var reason = reasonCode == FeatureStatusReasonCode.None
            ? default
            : new FeatureStatusReason(reasonCode, "evidence");

        var status = new FeatureStatusSnapshot(Key, "Feature", configuredEnabled, state, reason, 3);

        Assert.Equal(state, status.State);
        Assert.Equal(configuredEnabled, status.ConfiguredEnabled);
        Assert.Equal(reasonCode, status.Reason.Code);
        Assert.Contains(configuredEnabled ? "Configured: Enabled" : "Configured: Disabled", FeatureStatusPresenter.Format(status));
    }

    [Fact]
    public void Snapshot_RejectsContradictoryConfigurationAndReasonEvidence()
    {
        Assert.Throws<ArgumentException>(() => new FeatureStatusSnapshot(
            Key,
            "Feature",
            true,
            FeatureStatusState.ConfigurationDisabled,
            new FeatureStatusReason(FeatureStatusReasonCode.ConfigurationDisabled, "off")));
        Assert.Throws<ArgumentException>(() => new FeatureStatusSnapshot(
            Key,
            "Feature",
            true,
            FeatureStatusState.Operational,
            new FeatureStatusReason(FeatureStatusReasonCode.RuntimeFailure, "failure")));
        Assert.Throws<ArgumentException>(() => new FeatureStatusSnapshot(
            Key,
            "Feature",
            true,
            FeatureStatusState.NotReady));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FeatureStatusReason((FeatureStatusReasonCode)699, "unknown"));
    }

    [Fact]
    public void Registry_PublishesOnlyCanonicalConditionTransitions()
    {
        var registry = new FeatureStatusRegistry();
        var transitions = new List<FeatureStatusTransition>();
        registry.Transitioned += transitions.Add;
        using var registration = registry.Register(Operational(1));

        Assert.False(registration.Update(Operational(1)));
        Assert.True(registration.Update(Blocked("first wording", 1)));
        Assert.False(registration.Update(Blocked("different wording", 1)));
        Assert.True(registration.Update(Blocked("same condition, new lifecycle", 2)));

        Assert.Equal(3, transitions.Count);
        Assert.Equal(
            new[] { FeatureStatusTransitionKind.Added, FeatureStatusTransitionKind.Changed, FeatureStatusTransitionKind.Changed },
            transitions.Select(transition => transition.Kind));
        Assert.Equal(new long[] { 1, 2, 3 }, transitions.Select(transition => transition.Sequence));
    }

    [Fact]
    public void Registry_OrdersSnapshotsAndKeepsSiblingFailuresIndependent()
    {
        var registry = new FeatureStatusRegistry();
        using var later = registry.Register(new FeatureStatusSnapshot(
            new FeatureStatusKey("plugin.z", "feature.b"),
            "Later",
            true,
            FeatureStatusState.Faulted,
            new FeatureStatusReason(FeatureStatusReasonCode.RuntimeFailure, "failed")));
        using var earlier = registry.Register(new FeatureStatusSnapshot(
            new FeatureStatusKey("plugin.a", "feature.a"),
            "Earlier",
            true,
            FeatureStatusState.Operational));

        var snapshot = registry.GetSnapshot();

        Assert.Equal(new[] { "plugin.a/feature.a", "plugin.z/feature.b" }, snapshot.Select(status => status.Key.ToString()));
        Assert.Equal(FeatureStatusState.Operational, snapshot[0].State);
        Assert.Equal(FeatureStatusState.Faulted, snapshot[1].State);
    }

    [Fact]
    public void Registry_RejectsDuplicatePublishersAndRemovesDisposedStatus()
    {
        var registry = new FeatureStatusRegistry();
        var transitions = new List<FeatureStatusTransition>();
        registry.Transitioned += transitions.Add;
        var registration = registry.Register(Operational(0));

        Assert.Throws<InvalidOperationException>(() => registry.Register(Operational(0)));
        registration.Dispose();

        Assert.Empty(registry.GetSnapshot());
        Assert.Equal(FeatureStatusTransitionKind.Removed, transitions[^1].Kind);
        Assert.Equal(Key, transitions[^1].Previous!.Value.Key);
    }

    [Fact]
    public void Registry_IsolatesFailingSubscribers()
    {
        var registry = new FeatureStatusRegistry();
        var received = 0;
        registry.Transitioned += _ => throw new InvalidOperationException("consumer failure");
        registry.Transitioned += _ => received++;

        using var registration = registry.Register(Operational(0));
        registration.Update(Blocked("blocked", 0));

        Assert.Equal(2, received);
    }

    [Fact]
    public async Task Registry_RejectsCrossThreadAccess()
    {
        var registry = new FeatureStatusRegistry();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(() => registry.GetSnapshot()));
    }

    [Fact]
    public void RelatedIdentity_IsPartOfConditionButDisplayNameIsNot()
    {
        var registry = new FeatureStatusRegistry();
        var first = new AutomationEntityIdentity("upgrade", "9787dbd5-58b8-4da1-ae89-f9435aa80b20", "UpgradeSO", "First name");
        var renamed = new AutomationEntityIdentity("upgrade", "9787dbd5-58b8-4da1-ae89-f9435aa80b20", "UpgradeSO", "Renamed");
        var different = new AutomationEntityIdentity("upgrade", "b5efd19a-9655-4359-ad27-f391bb86c2e4", "UpgradeSO", "Different");
        using var registration = registry.Register(Blocked("blocked", 0, first));

        Assert.False(registration.Update(Blocked("renamed", 0, renamed)));
        Assert.True(registration.Update(Blocked("different", 0, different)));
    }

    private static FeatureStatusSnapshot Operational(long generation) => new(
        Key,
        "Feature",
        true,
        FeatureStatusState.Operational,
        lifecycleGeneration: generation);

    private static FeatureStatusSnapshot Blocked(
        string summary,
        long generation,
        AutomationEntityIdentity relatedEntity = default) => new(
        Key,
        "Feature",
        true,
        FeatureStatusState.TemporarilyBlocked,
        new FeatureStatusReason(FeatureStatusReasonCode.NativeBusy, summary, relatedEntity),
        generation);
}
