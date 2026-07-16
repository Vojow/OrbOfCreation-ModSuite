using System.Collections.Generic;
using System.Linq;
using OrbAutomata;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutoBuyLifecycleTests
{
    [Fact]
    public void LockedCandidate_RemainsIndexedAndBecomesAvailableAfterUnlock()
    {
        var index = new AutoBuyCandidateIndex();
        var candidate = Candidate("upgrade", AutoBuyCandidateKind.Upgrade, available: false);

        Assert.Empty(index.Reconcile(new[] { candidate }));
        AssertState(index, candidate.Uuid, AutoBuyCandidateLifecycleState.Locked);

        candidate.Evidence = Upgrade(available: true, current: 0, queued: 0, max: false, maxQueued: false);

        Assert.Same(candidate, Assert.Single(index.Reconcile(new[] { candidate })));
        AssertState(index, candidate.Uuid, AutoBuyCandidateLifecycleState.Available);
    }

    [Fact]
    public void FiniteUpgrade_MovesFromTerminalQueueToCompletedTombstone()
    {
        var index = new AutoBuyCandidateIndex();
        var candidate = Candidate("finite", AutoBuyCandidateKind.Upgrade, available: true);
        Assert.Single(index.Reconcile(new[] { candidate }));

        candidate.Evidence = Upgrade(available: true, current: 0, queued: 1, max: false, maxQueued: true);
        Assert.Empty(index.Reconcile(new[] { candidate }));
        AssertState(index, candidate.Uuid, AutoBuyCandidateLifecycleState.TerminalQueued);

        candidate.Evidence = Upgrade(available: false, current: 1, queued: 0, max: true, maxQueued: true);
        Assert.Empty(index.Reconcile(new[] { candidate }));
        AssertState(index, candidate.Uuid, AutoBuyCandidateLifecycleState.Completed);
        Assert.True(index.TryGetCandidate(candidate.Uuid, out _));
    }

    [Fact]
    public void QueuedRepeatableStructure_RemainsHotAndReturnsToAvailable()
    {
        var index = new AutoBuyCandidateIndex();
        var candidate = Candidate("structure", AutoBuyCandidateKind.Structure, available: true);

        candidate.Evidence = Structure(available: true, current: 4, queued: 3);
        var active = Assert.Single(index.Reconcile(new[] { candidate }));
        Assert.Same(candidate, active);
        Assert.True(active.TryPurchaseOne(out _));
        Assert.Equal(1, candidate.PurchaseCalls);
        AssertState(index, candidate.Uuid, AutoBuyCandidateLifecycleState.Queued);

        candidate.Evidence = Structure(available: true, current: 7, queued: 0);
        Assert.Same(candidate, Assert.Single(index.Reconcile(new[] { candidate })));
        AssertState(index, candidate.Uuid, AutoBuyCandidateLifecycleState.Available);
    }

    [Fact]
    public void NonTerminalQueuedUpgrade_RemainsHotWhenNativeAvailabilityPermits()
    {
        var index = new AutoBuyCandidateIndex();
        var candidate = Candidate("repeatable-upgrade", AutoBuyCandidateKind.Upgrade, available: true);
        candidate.Evidence = Upgrade(available: true, current: 2, queued: 1, finite: false, max: false, maxQueued: false);

        Assert.Same(candidate, Assert.Single(index.Reconcile(new[] { candidate })));
        AssertState(index, candidate.Uuid, AutoBuyCandidateLifecycleState.Queued);
    }

    [Fact]
    public void TerminalQueueCancellation_ReturnsCandidateToAvailableWithoutCompletion()
    {
        var index = new AutoBuyCandidateIndex();
        var candidate = Candidate("cancelled", AutoBuyCandidateKind.Upgrade, available: true);
        candidate.Evidence = Upgrade(available: true, current: 0, queued: 1, max: false, maxQueued: true);
        Assert.Empty(index.Reconcile(new[] { candidate }));

        candidate.Evidence = Upgrade(available: true, current: 0, queued: 0, max: false, maxQueued: false);

        Assert.Same(candidate, Assert.Single(index.Reconcile(new[] { candidate })));
        AssertState(index, candidate.Uuid, AutoBuyCandidateLifecycleState.Available);
    }

    [Fact]
    public void LevelRollback_StartsNewEpochAndReclassifiesCompletedCandidate()
    {
        var index = new AutoBuyCandidateIndex();
        var candidate = Candidate("rollback", AutoBuyCandidateKind.Upgrade, available: false);
        candidate.Evidence = Upgrade(available: false, current: 1, queued: 0, max: true, maxQueued: true);
        Assert.Empty(index.Reconcile(new[] { candidate }));
        var completedEpoch = index.Epoch;

        candidate.Evidence = Upgrade(available: true, current: 0, queued: 0, max: false, maxQueued: false);

        Assert.Single(index.Reconcile(new[] { candidate }));
        Assert.True(index.Epoch > completedEpoch);
        AssertState(index, candidate.Uuid, AutoBuyCandidateLifecycleState.Available);
    }

    [Fact]
    public void ExplicitSaveOrNgPlusEpoch_ReclassifiesTombstonesFromLiveEvidence()
    {
        var index = new AutoBuyCandidateIndex();
        var candidate = Candidate("ng-plus", AutoBuyCandidateKind.Upgrade, available: false);
        candidate.Evidence = Upgrade(available: false, current: 1, queued: 0, max: true, maxQueued: true);
        index.Reconcile(new[] { candidate });
        var previousEpoch = index.Epoch;

        candidate.Evidence = Upgrade(available: false, current: 0, queued: 0, max: false, maxQueued: false);
        index.BeginLifecycleEpoch();

        Assert.True(index.Epoch > previousEpoch);
        AssertState(index, candidate.Uuid, AutoBuyCandidateLifecycleState.Locked);
        Assert.Empty(index.Reconcile(new[] { candidate }));
    }

    [Fact]
    public void RecreatedNativeObjectWithSameUuidAndType_ReplacesStaleReference()
    {
        var index = new AutoBuyCandidateIndex();
        var original = Candidate("recreated", AutoBuyCandidateKind.Structure, available: true);
        Assert.Same(original, Assert.Single(index.Reconcile(new[] { original })));
        var previousEpoch = index.Epoch;

        var recreated = Candidate("recreated", AutoBuyCandidateKind.Structure, available: true);

        Assert.Same(recreated, Assert.Single(index.Reconcile(new[] { recreated })));
        Assert.True(index.Epoch > previousEpoch);
        Assert.True(index.TryGetCandidate(recreated.Uuid, out var indexed));
        Assert.Same(recreated, indexed);
    }

    [Fact]
    public void DestroyedCandidate_FailsClosedUntilSameUuidIsRecreated()
    {
        var index = new AutoBuyCandidateIndex();
        var destroyed = Candidate("destroyed", AutoBuyCandidateKind.Structure, available: true);
        Assert.Single(index.Reconcile(new[] { destroyed }));

        destroyed.EvidenceReadable = false;
        Assert.Empty(index.Reconcile(new[] { destroyed }));
        AssertState(index, destroyed.Uuid, AutoBuyCandidateLifecycleState.Invalid);

        var recreated = Candidate("destroyed", AutoBuyCandidateKind.Structure, available: true);
        Assert.Same(recreated, Assert.Single(index.Reconcile(new[] { recreated })));
        AssertState(index, recreated.Uuid, AutoBuyCandidateLifecycleState.Available);
    }

    [Fact]
    public void SameUuidWithDifferentNativeType_FailsClosed()
    {
        var index = new AutoBuyCandidateIndex();
        var structure = Candidate("collision", AutoBuyCandidateKind.Structure, available: true);
        index.Reconcile(new[] { structure });
        var wrongType = new FakeLifecycleCandidate(
            "collision",
            AutoBuyCandidateKind.Upgrade,
            "UpgradeSO",
            new object(),
            Upgrade(available: true, current: 0, queued: 0, max: false, maxQueued: false));

        Assert.Empty(index.Reconcile(new[] { wrongType }));
        AssertState(index, "collision", AutoBuyCandidateLifecycleState.Invalid);
    }

    private static FakeLifecycleCandidate Candidate(string uuid, AutoBuyCandidateKind kind, bool available)
    {
        return new FakeLifecycleCandidate(
            uuid,
            kind,
            kind == AutoBuyCandidateKind.Structure ? "StructureSO" : "UpgradeSO",
            new object(),
            kind == AutoBuyCandidateKind.Structure
                ? Structure(available, 0, 0)
                : Upgrade(available, 0, 0, max: false, maxQueued: false));
    }

    private static AutoBuyLifecycleEvidence Structure(bool available, int current, int queued)
    {
        return new AutoBuyLifecycleEvidence(available, current, queued, false, false, false);
    }

    private static AutoBuyLifecycleEvidence Upgrade(
        bool available,
        int current,
        int queued,
        bool max,
        bool maxQueued,
        bool finite = true)
    {
        return new AutoBuyLifecycleEvidence(available, current, queued, finite, max, maxQueued);
    }

    private static void AssertState(
        AutoBuyCandidateIndex index,
        string uuid,
        AutoBuyCandidateLifecycleState expected)
    {
        Assert.True(index.TryGetState(uuid, out var actual));
        Assert.Equal(expected, actual);
    }

    private sealed class FakeLifecycleCandidate : IAutoBuyCandidate, IAutoBuyLifecycleCandidate, IAutoBuyNativeIdentity
    {
        private readonly AutoBuyCandidateSnapshot _snapshot;

        public FakeLifecycleCandidate(
            string uuid,
            AutoBuyCandidateKind kind,
            string reflectedType,
            object nativeIdentity,
            AutoBuyLifecycleEvidence evidence)
        {
            Uuid = uuid;
            NativeIdentity = nativeIdentity;
            Evidence = evidence;
            _snapshot = new AutoBuyCandidateSnapshot(this, uuid, uuid, kind, reflectedType);
        }

        public string Uuid { get; }

        public object NativeIdentity { get; }

        public AutoBuyLifecycleEvidence Evidence { get; set; }

        public bool EvidenceReadable { get; set; } = true;

        public int PurchaseCalls { get; private set; }

        public AutoBuyCandidateSnapshot Snapshot() => _snapshot;

        public bool TryGetLifecycleEvidence(out AutoBuyLifecycleEvidence evidence, out string reason)
        {
            evidence = Evidence;
            reason = EvidenceReadable ? string.Empty : "simulated destroyed native object";
            return EvidenceReadable;
        }

        public bool IsAvailable() => Evidence.IsAvailable;

        public bool CanPurchase(out string reason)
        {
            reason = string.Empty;
            return true;
        }

        public IReadOnlyList<ResourceAdmissionCost> GetCosts() => Enumerable.Empty<ResourceAdmissionCost>().ToArray();

        public bool TryPurchaseOne(out string reason)
        {
            PurchaseCalls++;
            reason = string.Empty;
            return true;
        }
    }
}
