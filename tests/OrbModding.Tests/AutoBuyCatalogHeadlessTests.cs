using System;
using System.Linq;
using OrbAutomata;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutoBuyCatalogHeadlessTests : IDisposable
{
    public AutoBuyCatalogHeadlessTests()
    {
        global::StructureSO.All.Clear();
        UpgradeSO.All.Clear();
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void NativeRegistry_ReconcilesUnlocksAndRecreatedObjectIdentity()
    {
        var elapsed = TimeSpan.Zero;
        var structureId = Guid.NewGuid();
        var structure = new global::StructureSO
        {
            uuid = structureId.ToString(),
            available = true,
        };
        var lockedUpgrade = new UpgradeSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = false,
        };
        global::StructureSO.All.Add(structure);
        UpgradeSO.All.Add(lockedUpgrade);
        using var catalog = new ReflectionAutoBuyCatalog(() => elapsed);

        var initial = Settle(catalog);
        var initialCandidate = Assert.Single(initial.ActiveCandidates);
        Assert.Equal(structureId.ToString(), initialCandidate.Snapshot().Uuid);
        Assert.Same(structure, Assert.IsAssignableFrom<IAutoBuyNativeIdentity>(initialCandidate).NativeIdentity);

        lockedUpgrade.available = true;
        catalog.InvalidateLifecycle();
        var unlocked = Settle(catalog);
        Assert.Equal(2, unlocked.ActiveCandidates.Count);
        Assert.Contains(unlocked.ActiveCandidates, candidate =>
            candidate.Snapshot().Uuid == lockedUpgrade.uuid);

        var replacement = new global::StructureSO
        {
            uuid = structureId.ToString(),
            available = true,
        };
        global::StructureSO.All[0] = replacement;
        elapsed = TimeSpan.FromSeconds(11);
        var replaced = Settle(catalog);
        var replacedCandidate = replaced.ActiveCandidates.Single(candidate =>
            candidate.Snapshot().Uuid == structureId.ToString());
        Assert.Same(
            replacement,
            Assert.IsAssignableFrom<IAutoBuyNativeIdentity>(replacedCandidate).NativeIdentity);
        Assert.DoesNotContain(replaced.ActiveCandidates, candidate =>
            candidate is IAutoBuyNativeIdentity native && ReferenceEquals(native.NativeIdentity, structure));
    }

    public void Dispose()
    {
        global::StructureSO.All.Clear();
        UpgradeSO.All.Clear();
    }

    private static AutoBuyEvaluationBatch Settle(ReflectionAutoBuyCatalog catalog)
    {
        AutoBuyEvaluationBatch? batch = null;
        for (var pass = 0; pass < 30; pass++)
        {
            batch = catalog.BeginEvaluation(new AutoBuyEvaluationRequest(100, true, true));
            foreach (var candidate in batch.DirtyCandidates)
            {
                _ = candidate.GetCosts();
                catalog.CompleteCandidateEvaluation(
                    candidate,
                    suppressResourceTracking: false,
                    policyExcluded: false);
            }
            if (!batch.ReconciliationPending) return batch;
        }

        Assert.Fail("Auto Buy native registry did not settle within the bounded fixture.");
        return batch!;
    }
}
