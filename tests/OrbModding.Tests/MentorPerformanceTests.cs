using OrbMentor;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace OrbModding.Tests;

public sealed class MentorPerformanceTests
{
    [Fact]
    public void SourceEventsCoalesceByQualifiedSourceWithoutRetroactiveQualification()
    {
        var events = new MentorSourceAccumulator();

        events.Capture("lower-source", new MentorAmount(9, 2), qualifiesAtEvent: false);
        events.Capture("lower-source", new MentorAmount(1, 3), qualifiesAtEvent: true);
        events.Capture("mentor-a", new MentorAmount(1, 3), qualifiesAtEvent: true);
        events.Capture("mentor-a", new MentorAmount(2, 3), qualifiesAtEvent: true);
        events.Capture("mentor-b", new MentorAmount(4, 3), qualifiesAtEvent: true);

        Assert.Equal(3, events.SourceCount);
        var total = events.Drain();
        Assert.Equal(8, total.Amount.Mantissa, 12);
        Assert.Equal(3, total.Amount.Exponent);
        Assert.Equal(4, total.EventCount);
        Assert.False(events.HasPending);
    }

    [Fact]
    public void CaptureQueueIsBoundedAndCoalescesOnlyIdenticalEventEvidence()
    {
        var source = new object();
        var queue = new MentorCaptureQueue(2);
        var first = new MentorCaptureKey(source, "source", 5, true, progressionEpoch: 4);

        Assert.Equal(MentorCaptureResult.Added, queue.Capture(first, new MentorAmount(1, 3)));
        Assert.Equal(MentorCaptureResult.Coalesced, queue.Capture(first, new MentorAmount(2, 3)));
        Assert.Equal(MentorCaptureResult.Added, queue.Capture(
            new MentorCaptureKey(source, "source", 6, true, progressionEpoch: 5), new MentorAmount(1, 2)));
        Assert.Equal(MentorCaptureResult.Overflow, queue.Capture(
            new MentorCaptureKey(new object(), "other", 1, true, progressionEpoch: 5), new MentorAmount(1, 2)));

        Assert.Equal(3, queue.EventCount);
        Assert.True(queue.TryTake(out var captured));
        Assert.Equal(2, captured.EventCount);
        Assert.Equal(3, captured.Amount.Mantissa, 12);
        Assert.Equal(3, captured.Amount.Exponent);
    }

    [Fact]
    public void CaptureTimeRelationshipCannotBeRequalifiedByLaterProgression()
    {
        var source = new object();
        var recipients = new[] { new MentorRecipe("recipient", 1, true) };
        var ineligibleAtCapture = new MentorRelationshipSnapshot(
            7, 6, new[] { new MentorRecipe("source", 5, true), recipients[0] }, recipients);
        var qualifiedAtCapture = new MentorRelationshipSnapshot(
            7, 5, new[] { new MentorRecipe("source", 5, true), recipients[0] }, recipients);

        Assert.Equal(
            MentorQualificationStatus.SourceIneligible,
            MentorRelationshipQualification.Evaluate(
                new MentorCaptureKey(source, "source", 5, true, 7, ineligibleAtCapture),
                relationshipEpoch: 8, highestMastery: 5, recipientCount: 2));
        Assert.Equal(
            MentorQualificationStatus.Qualified,
            MentorRelationshipQualification.Evaluate(
                new MentorCaptureKey(source, "source", 5, true, 7, qualifiedAtCapture),
                relationshipEpoch: 99, highestMastery: 99, recipientCount: 0));
    }

    [Fact]
    public void MasteryCrossingCaptureAdvancesEpochBeforeEvidenceIsQueued()
    {
        long epoch = 3;
        var dirty = false;
        var cachedMastery = 5;
        var cachedDiscovered = true;

        Assert.True(MentorProgressionObservation.Apply(
            ref epoch, ref dirty, ref cachedMastery, ref cachedDiscovered, observedMastery: 6, observedDiscovered: true));
        var relationship = new MentorRelationshipSnapshot(
            3,
            5,
            new[] { new MentorRecipe("source", 5, true), new MentorRecipe("recipient", 1, true) },
            new[] { new MentorRecipe("recipient", 1, true) });
        var captured = new MentorCaptureKey(
            new object(), "source", cachedMastery, cachedDiscovered, epoch, relationship);

        Assert.Equal(4, epoch);
        Assert.True(dirty);
        Assert.Equal(
            MentorQualificationStatus.Qualified,
            MentorRelationshipQualification.Evaluate(captured, relationshipEpoch: 99, highestMastery: 99, recipientCount: 0));
        Assert.False(MentorProgressionObservation.Apply(
            ref epoch, ref dirty, ref cachedMastery, ref cachedDiscovered, observedMastery: 6, observedDiscovered: true));
        Assert.Equal(4, epoch);
    }

    [Fact]
    public void RefreshAdvancesEpochForAnUnhookedChangeEvenWhenGrantAlreadyDirtiedRelationships()
    {
        long epoch = 11;

        Assert.True(MentorProgressionObservation.AdvanceRefreshEpoch(
            ref epoch, relationshipEpoch: 11, liveStateChanged: true));
        Assert.Equal(12, epoch);

        // A required progression hook has already accounted for this change.
        Assert.False(MentorProgressionObservation.AdvanceRefreshEpoch(
            ref epoch, relationshipEpoch: 11, liveStateChanged: true));
        Assert.Equal(12, epoch);
        Assert.False(MentorProgressionObservation.AdvanceRefreshEpoch(
            ref epoch, relationshipEpoch: 12, liveStateChanged: false));
    }

    [Fact]
    public void SuccessfulGrantStateChangeInvalidatesRelationshipsWithoutDoubleAdvancingHookedEpoch()
    {
        long epoch = 8;
        var dirty = false;
        var cachedMastery = 4;
        var cachedDiscovered = true;

        MentorProgressionObservation.AfterNativeGrant(ref dirty);
        Assert.True(dirty);
        dirty = false;

        Assert.True(MentorProgressionObservation.Apply(
            ref epoch, ref dirty, ref cachedMastery, ref cachedDiscovered, observedMastery: 5, observedDiscovered: true));
        Assert.Equal(9, epoch);
        Assert.True(dirty);

        // Artifact/alchemy progression hooks may already have advanced the epoch.
        dirty = true;
        cachedMastery = 5;
        epoch = 10;
        Assert.True(MentorProgressionObservation.Apply(
            ref epoch, ref dirty, ref cachedMastery, ref cachedDiscovered,
            observedMastery: 6, observedDiscovered: true, epochAlreadyAdvanced: true));
        Assert.Equal(10, epoch);
    }

    [Fact]
    public void LifecycleResetSignalsCoalesceUntilTheBoundedWorkerRuns()
    {
        var signal = new MentorLifecycleSignal();

        signal.Request();
        signal.Request();

        Assert.True(signal.TryConsume());
        Assert.False(signal.TryConsume());
    }

    [Fact]
    public void PendingGrantSurvivesIdentityDeferralUntilExplicitCompletion()
    {
        var engine = new MentorEngine();
        engine.Consolidate(new MentorGrant("recipient", new MentorAmount(2, 4)));

        Assert.True(engine.TryPeek(out var uuid, out var amount));
        Assert.Equal("recipient", uuid);
        Assert.Equal(new MentorAmount(2, 4), amount);
        Assert.True(engine.TryPeek(out var first));
        Assert.True(engine.TryPeek(out var second));
        Assert.Equal(first.Amount, second.Amount);
        Assert.Equal(1, engine.PendingCount);
        Assert.True(engine.Complete("recipient"));
        Assert.Equal(0, engine.PendingCount);
    }

    [Fact]
    public void SameUuidReplacementCancelsCapturedPlannedAndExpandedXpBeforeGrant()
    {
        var pending = new MentorPendingWork();
        var source = new object();
        Assert.Equal(MentorCaptureResult.Added, pending.Captures.Capture(
            new MentorCaptureKey(source, "source", 5, true, progressionEpoch: 3),
            new MentorAmount(1, 3)));
        Assert.True(pending.Captures.TryTake(out var captured));
        pending.Sources.Capture(captured.Key.Uuid, captured.Amount, qualifiesAtEvent: true, captured.EventCount);
        var batch = pending.Sources.Drain();
        pending.ActivePlan = pending.Engine.CreatePlan(
            batch.Amount,
            10,
            MentorEconomyMode.SharedPool,
            new[] { new MentorRecipe("recipient", 1, true) },
            batch.EventCount);
        Assert.NotNull(pending.ActivePlan);
        Assert.True(pending.ActivePlan!.TryTake(out var expanded));
        pending.Engine.Consolidate(expanded);
        pending.ActivePlan = null;
        Assert.True(pending.Engine.TryPeek(out _));

        // Reconciliation found a different native object for the same UUID.
        Assert.True(MentorIdentityTransition.CancelPendingOnChange(identityChanged: true, pending));

        Assert.False(pending.Engine.TryPeek(out _));
        Assert.Equal(0, pending.Engine.PendingCount);
        Assert.False(pending.Sources.HasPending);
        Assert.Equal(0, pending.Captures.Count);
        Assert.Null(pending.ActivePlan);
    }

    [Fact]
    public void ReconcileAndRelationshipRequestsAfterEnumerationSurviveCommit()
    {
        var reconcile = new MentorWorkGeneration();
        var relationship = new MentorWorkGeneration();
        var captures = new MentorCaptureQueue(2);
        var reconcilePass = reconcile.Current;
        var refreshPass = relationship.Current;

        // Enumeration/read completed, then a new native source and a mastery
        // transition arrived while ordered output was still being built.
        Assert.Equal(MentorCaptureResult.Added, captures.Capture(
            new MentorCaptureKey(new object(), "late-source", 3, true, progressionEpoch: 4),
            new MentorAmount(1, 2)));
        reconcile.Request();
        relationship.Request();

        Assert.Equal(1, captures.Count);
        Assert.False(reconcile.IsCurrent(reconcilePass));
        Assert.False(relationship.IsCurrent(refreshPass));
    }

    [Fact]
    public void IncrementalUuidOrderingIsDeterministicAndLinearInBuildSteps()
    {
        const int count = 100;
        using var order = new MentorIncrementalOrder<int>();
        for (var index = count - 1; index >= 0; index--)
            Assert.True(order.TryAdd($"id-{index:D3}", index));
        Assert.False(order.TryAdd("id-050", 999));

        var output = new List<int>();
        var steps = 0;
        while (true)
        {
            steps++;
            if (!order.TryTakeNext(out var value)) break;
            output.Add(value);
        }

        Assert.Equal(Enumerable.Range(0, count), output);
        Assert.Equal(count + 1, steps);
    }

    [Fact]
    public void MoreThanOnePlanningSliceIsFullyCollatedBeforeALevelingGrant()
    {
        var pending = new MentorPendingWork();
        const long relationshipEpoch = 7;
        var recipients = new[] { new MentorRecipe("a", 1, true), new MentorRecipe("b", 2, true) };
        var relationship = new MentorRelationshipSnapshot(
            relationshipEpoch, 5, recipients, recipients);
        for (var index = 0; index < 20; index++)
        {
            Assert.Equal(MentorCaptureResult.Added, pending.Captures.Capture(
                new MentorCaptureKey(new object(), $"mentor-{index}", 5, true, relationshipEpoch, relationship),
                new MentorAmount(1, 1)));
        }

        for (var index = 0; index < 16; index++)
        {
            Assert.True(pending.Captures.TryTake(out var captured));
            Assert.Equal(MentorQualificationStatus.Qualified, MentorRelationshipQualification.Evaluate(
                captured.Key, relationshipEpoch, highestMastery: 5, recipientCount: 2));
            pending.Sources.Capture(captured.Key.Relationship!, captured.Key.Uuid, captured.Amount, captured.EventCount);
        }

        Assert.True(pending.HasGrantBarrier);
        Assert.Equal(4, pending.Captures.Count);
        Assert.Equal(0, pending.Engine.PendingCount);

        // An unrelated later transition advances the live epoch before the
        // second bounded slice. Capture-time qualification must remain valid.
        long epoch = relationshipEpoch;
        var dirty = false;
        var mastery = 1;
        var discovered = true;
        Assert.True(MentorProgressionObservation.Apply(
            ref epoch, ref dirty, ref mastery, ref discovered,
            observedMastery: 2, observedDiscovered: true));
        Assert.Equal(relationshipEpoch + 1, epoch);
        while (pending.Captures.TryTake(out var captured))
        {
            Assert.Equal(MentorQualificationStatus.Qualified, MentorRelationshipQualification.Evaluate(
                captured.Key, epoch, highestMastery: 6, recipientCount: 0));
            pending.Sources.Capture(captured.Key.Relationship!, captured.Key.Uuid, captured.Amount, captured.EventCount);
        }
        var batch = pending.Sources.Drain();
        Assert.Equal(2, batch.Amount.Mantissa, 12);
        Assert.Equal(2, batch.Amount.Exponent);
        pending.ActivePlan = pending.Engine.CreatePlan(
            batch.Amount,
            10,
            MentorEconomyMode.SharedPool,
            batch.Relationship!.Recipients,
            batch.EventCount);
        while (pending.ActivePlan!.TryTake(out var grant)) pending.Engine.Consolidate(grant);
        pending.ActivePlan = null;

        Assert.False(pending.HasGrantBarrier);
        Assert.Equal(2, pending.Engine.PendingCount);
        Assert.True(pending.Engine.TryPeek(out var first));
        Assert.Equal(1, first.Amount.Mantissa, 12);
        Assert.Equal(1, first.Amount.Exponent);
        Assert.True(pending.Engine.Complete(first.Uuid));

        Assert.True(pending.Engine.TryPeek(out var remaining));
        Assert.Equal(1, remaining.Amount.Mantissa, 12);
        Assert.Equal(1, remaining.Amount.Exponent);
        Assert.Equal(1, pending.Engine.PendingCount);
        Assert.True(pending.Engine.Complete(remaining.Uuid));
        Assert.False(pending.Engine.TryPeek(out _));
    }

    [Fact]
    public void IdentityValidationRejectsDestroyedWrongAndReplacedObjects()
    {
        var candidate = new IdentityBase();
        var replacement = new IdentityBase();

        Assert.Equal(MentorIdentityStatus.Valid,
            MentorIdentityValidation.Validate(typeof(IdentityBase), "id", candidate, candidate, "id", destroyed: false));
        Assert.Equal(MentorIdentityStatus.RegistryMismatch,
            MentorIdentityValidation.Validate(typeof(IdentityBase), "id", candidate, replacement, "id", destroyed: false));
        Assert.Equal(MentorIdentityStatus.Valid,
            MentorIdentityValidation.Validate(typeof(IdentityBase), "id", replacement, replacement, "id", destroyed: false));
        Assert.Equal(MentorIdentityStatus.UuidMismatch,
            MentorIdentityValidation.Validate(typeof(IdentityBase), "id", candidate, candidate, "other", destroyed: false));
        Assert.Equal(MentorIdentityStatus.Destroyed,
            MentorIdentityValidation.Validate(typeof(IdentityBase), "id", candidate, candidate, "id", destroyed: true));
    }

    [Fact]
    public void PermanentContractBlockSurvivesLifecycleResetAndDropsAreCounted()
    {
        var failure = new MentorFailureState();
        failure.BlockPermanent("contract");
        failure.BlockTransient("temporary");
        failure.ResetLifecycle();

        Assert.True(failure.IsBlocked);
        Assert.Equal("contract", failure.Reason);
        Assert.Null(failure.TransientReason);

        var diagnostics = new MentorDiagnostics();
        diagnostics.RecordDrop(MentorDropReason.SourceIneligible, 3, grant: false);
        diagnostics.RecordDrop(MentorDropReason.RecipientIdentityChanged, 2, grant: true);
        Assert.Equal(3, diagnostics.DroppedEvents);
        Assert.Equal(2, diagnostics.DroppedGrants);
        Assert.Equal(3, diagnostics.DropCount(MentorDropReason.SourceIneligible));
    }

    [Fact]
    public void OptionalDomainQuarantineDoesNotBlockRequiredSpellState()
    {
        var failures = new MentorFailureRegistry();

        failures.For(MentorDomain.Artifacts).BlockPermanent("optional artifact contract");

        Assert.False(failures.Global.IsBlocked);
        Assert.False(failures.IsDomainBlocked(MentorDomain.Spells));
        Assert.True(failures.IsDomainBlocked(MentorDomain.Artifacts));
        Assert.False(failures.IsDomainBlocked(MentorDomain.Alchemy));
        failures.ResetLifecycle();
        Assert.True(failures.IsDomainBlocked(MentorDomain.Artifacts));
    }

    private sealed class IdentityBase { }

    [Fact]
    public void RecipientPlanResumesWithoutDroppingUnexpandedWork()
    {
        var recipients = new[]
        {
            new MentorRecipe("a", 0, true),
            new MentorRecipe("b", 1, true),
            new MentorRecipe("c", 2, true),
        };
        var engine = new MentorEngine();
        var plan = Assert.IsType<MentorPlan>(engine.CreatePlan(
            new MentorAmount(3, 3),
            30,
            MentorEconomyMode.SharedPool,
            recipients));

        Assert.True(plan.TryTake(out var first));
        Assert.Equal("a", first.Uuid);
        Assert.Equal(2, plan.RemainingCount);

        Assert.True(plan.TryTake(out var second));
        Assert.True(plan.TryTake(out var third));
        Assert.Equal(new[] { "b", "c" }, new[] { second.Uuid, third.Uuid });
        Assert.Equal(0, plan.RemainingCount);
        Assert.False(plan.TryTake(out _));

        Assert.Equal(first.Amount, second.Amount);
        Assert.Equal(second.Amount, third.Amount);
        Assert.Equal(3, first.Amount.Mantissa, 12);
        Assert.Equal(2, first.Amount.Exponent);
    }

    [Fact]
    public void CancellationClearsCapturedEventsAndExpandedRecipientWork()
    {
        var events = new MentorSourceAccumulator();
        var engine = new MentorEngine();
        events.Capture("mentor", new MentorAmount(1, 3), qualifiesAtEvent: true);
        engine.Consolidate(new MentorGrant("recipient", new MentorAmount(1, 2)));

        events.Cancel();
        engine.Cancel();

        Assert.False(events.HasPending);
        Assert.Equal(0, engine.PendingCount);
    }
}
