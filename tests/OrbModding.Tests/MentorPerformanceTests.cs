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
    public void ParkedGrantLedgerIsBoundedCoalescesExactlyAndCancelsFailClosed()
    {
        var parked = new MentorParkedGrantLedger(capacity: 2);
        Assert.Equal(
            MentorParkResult.Parked,
            parked.Park(new MentorGrant("a", new MentorAmount(1, 3)), settledGeneration: 4));
        Assert.Equal(
            MentorParkResult.Parked,
            parked.Park(new MentorGrant("b", new MentorAmount(2, 3)), settledGeneration: 4));
        Assert.Equal(
            MentorParkResult.Coalesced,
            parked.Park(new MentorGrant("a", new MentorAmount(3, 3)), settledGeneration: 4));
        Assert.Equal(
            MentorParkResult.Overflow,
            parked.Park(new MentorGrant("c", new MentorAmount(9, 3)), settledGeneration: 4));
        Assert.Equal(1, parked.OverflowCount);
        Assert.Equal(2, parked.Count);
        Assert.False(parked.HasReady(settledGeneration: 4));

        Assert.True(parked.TryTakeReady(settledGeneration: 5, out var first));
        Assert.True(parked.TryTakeReady(settledGeneration: 5, out var second));
        var grants = new[] { first, second }.ToDictionary(grant => grant.Uuid);
        Assert.Equal(new MentorAmount(4, 3), grants["a"].Amount);
        Assert.Equal(new MentorAmount(2, 3), grants["b"].Amount);
        Assert.False(parked.TryTakeReady(settledGeneration: 5, out _));

        parked.Park(new MentorGrant("retained", new MentorAmount(7, 2)), settledGeneration: 5);
        parked.Cancel();
        Assert.Equal(0, parked.Count);
        Assert.Equal(1, parked.OverflowCount);
        Assert.False(parked.HasReady(settledGeneration: 6));
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
        var relationship = new MentorRelationshipSnapshot(
            3, 5,
            new[] { new MentorRecipe("source", 5, true), new MentorRecipe("recipient", 1, true) },
            new[] { new MentorRecipe("recipient", 1, true) });
        pending.ResolvingCapture = captured;
        pending.RelationshipResolution = new MentorRelationshipResolutionWork(
            MentorRelationshipEvidence.FromSnapshot(relationship));

        // Reconciliation found a different native object for the same UUID.
        Assert.True(MentorIdentityTransition.CancelPendingOnChange(identityChanged: true, pending));

        Assert.False(pending.Engine.TryPeek(out _));
        Assert.Equal(0, pending.Engine.PendingCount);
        Assert.False(pending.Sources.HasPending);
        Assert.Equal(0, pending.Captures.Count);
        Assert.Null(pending.ActivePlan);
        Assert.Null(pending.ResolvingCapture);
        Assert.Null(pending.RelationshipResolution);
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
            pending.Sources.Capture(
                captured.Key.Relationship!.ForCapture(captured.Key),
                captured.Key.Uuid, captured.Amount, captured.EventCount);
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
            pending.Sources.Capture(
                captured.Key.Relationship!.ForCapture(captured.Key),
                captured.Key.Uuid, captured.Amount, captured.EventCount);
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
    public void DirtyWindowCapturesBindToTheirEvidenceAcrossLaterInvalidation()
    {
        var recipient = new MentorRecipe("recipient", 1, true);
        var discovered = Enumerable.Range(0, 20)
            .Select(index => new MentorRecipe($"source-{index}", 5, true))
            .Append(new MentorRecipe("existing-mentor", 5, true))
            .Append(recipient)
            .ToArray();
        var basis = new MentorRelationshipSnapshot(
            7,
            5,
            discovered,
            new[] { recipient });
        var dirtyGeneration = MentorRelationshipEvidence.FromSnapshot(basis)
            .WithChange("discovered-mentor", 5, discovered: true, epoch: 8);
        var pending = new MentorPendingWork();

        // Discover/PurchaseLevel has made the live cache dirty and its refresh
        // takes multiple frames. More than one planning slice arrives meanwhile.
        for (var index = 0; index < 20; index++)
        {
            Assert.Equal(MentorCaptureResult.Added, pending.Captures.Capture(
                new MentorCaptureKey(
                    new object(), $"source-{index}", 5, true, 8,
                    relationship: null, evidence: dirtyGeneration),
                new MentorAmount(1, 1)));
        }

        for (var index = 0; index < 16; index++)
            ResolveNextCapturedEvidence(pending);
        Assert.Equal(4, pending.Captures.Count);

        // A later mastery transition would make the captured sources
        // ineligible if they were redirected to the newest live relationship.
        var laterGeneration = dirtyGeneration.WithChange(
            "later-mentor", 6, discovered: true, epoch: 9);
        var laterSnapshot = ResolveEvidence(laterGeneration);
        Assert.Equal(6, laterSnapshot.HighestMastery);

        while (pending.Captures.Count > 0) ResolveNextCapturedEvidence(pending);
        Assert.Equal(20, pending.Sources.EventCount);
        var batch = pending.Sources.Drain();
        Assert.Equal(5, batch.Relationship!.HighestMastery);
        Assert.Equal(new[] { "recipient" }, batch.Relationship.Recipients.Select(item => item.Uuid));
        Assert.Equal(2, batch.Amount.Mantissa, 12);
        Assert.Equal(2, batch.Amount.Exponent);

        pending.ActivePlan = pending.Engine.CreatePlan(
            batch.Amount, 10, MentorEconomyMode.SharedPool,
            batch.Relationship.Recipients, batch.EventCount);
        while (pending.ActivePlan!.TryTake(out var grant)) pending.Engine.Consolidate(grant);
        pending.ActivePlan = null;

        Assert.True(pending.Engine.TryPeek(out var uuid, out var amount));
        Assert.Equal("recipient", uuid);
        Assert.Equal(2, amount.Mantissa, 12);
        Assert.Equal(1, amount.Exponent);
        Assert.True(pending.Engine.Complete(uuid));
        Assert.False(pending.Engine.TryPeek(out _, out _));
    }

    [Fact]
    public void CrossingSourceDerivationIsConstantTimeAndRecipientPlanningRemainsBounded()
    {
        const int lowerRecipientCount = 4096;
        var source = new MentorRecipe("crossing-source", 5, true);
        var oldMentor = new MentorRecipe("old-mentor", 10, true);
        var discovered = new List<MentorRecipe>(lowerRecipientCount + 2) { oldMentor, source };
        for (var index = 0; index < lowerRecipientCount; index++)
            discovered.Add(new MentorRecipe($"lower-{index:D4}", 1, true));
        var oldRecipients = discovered.Where(recipe => recipe.MasteryLevel < 10).ToArray();
        var relationship = new MentorRelationshipSnapshot(12, 10, discovered, oldRecipients);
        var captured = new MentorCaptureKey(
            new object(), source.Uuid, 11, true, 13, relationship);

        Assert.Equal(MentorQualificationStatus.Qualified,
            MentorRelationshipQualification.Evaluate(captured, 99, 99, 0));
        var derived = relationship.ForCapture(captured);
        Assert.InRange(derived.DerivationSteps, 0, 1);
        Assert.Equal(discovered.Count - 1, derived.Recipients.Count);
        Assert.DoesNotContain(derived.Recipients, recipe => recipe.Uuid == source.Uuid);
        Assert.Contains(derived.Recipients, recipe => recipe.Uuid == oldMentor.Uuid);

        var engine = new MentorEngine();
        var plan = Assert.IsType<MentorPlan>(engine.CreatePlan(
            new MentorAmount(1, 2), 10, MentorEconomyMode.SharedPool, derived.Recipients));
        var total = default(MentorAmount);
        var processed = 0;
        for (; processed < 16; processed++)
        {
            Assert.True(plan.TryTake(out var grant));
            total = total.Add(grant.Amount);
        }
        Assert.Equal(derived.Recipients.Count - 16, plan.RemainingCount);
        while (plan.TryTake(out var grant))
        {
            total = total.Add(grant.Amount);
            processed++;
        }
        Assert.Equal(derived.Recipients.Count, processed);
        Assert.Equal(10, total.Mantissa, 10);
        Assert.Equal(0, total.Exponent);
    }

    [Fact]
    public void RefreshObservationEvidencePrecedesInterleavedCaptureAndCommit()
    {
        var crossing = new MentorRecipe("crossing", 4, true);
        var oldMentor = new MentorRecipe("old-mentor", 5, true);
        var lower = new MentorRecipe("lower", 1, true);
        var basis = new MentorRelationshipSnapshot(
            20, 5, new[] { crossing, oldMentor, lower }, new[] { crossing, lower });

        // ProcessRefreshStep observes the native 4 -> 6 delta and appends this
        // immutable node before mutating the cached NativeEntry.
        var beforeReadRequirement = new MentorRelationshipRequirement(requestGeneration: 11);
        MentorRelationshipRequirement? currentRequirement = beforeReadRequirement;
        MentorRefreshCaptureOrdering.ObserveDelta(ref currentRequirement, requestGeneration: 11);
        Assert.Null(currentRequirement);
        var observedEvidence = MentorRelationshipEvidence.FromSnapshot(basis)
            .WithChange(crossing.Uuid, 6, discovered: true, epoch: 20);
        var requirement = new MentorRelationshipRequirement(requestGeneration: 11);
        var captured = new MentorCaptureKey(
            new object(), crossing.Uuid, 6, true, 20,
            relationship: null, evidence: null, requirement: requirement);
        var committed = ResolveEvidence(observedEvidence);
        MentorRefreshCaptureOrdering.Commit(beforeReadRequirement, 11, committed);
        MentorRefreshCaptureOrdering.Commit(requirement, 11, committed);
        Assert.Null(beforeReadRequirement.Resolved);
        Assert.Same(committed, requirement.Resolved);

        var resolvedCapture = new MentorCaptureKey(
            captured.Source, captured.Uuid, captured.MasteryLevel, captured.Discovered,
            captured.ProgressionEpoch, committed);
        Assert.Equal(MentorQualificationStatus.Qualified,
            MentorRelationshipQualification.Evaluate(resolvedCapture, 21, 6, 2));
        var recipients = committed.ForCapture(resolvedCapture).Recipients;
        Assert.Equal(new[] { "lower", "old-mentor" }, recipients.Select(recipe => recipe.Uuid));
        Assert.DoesNotContain(recipients, recipe => recipe.Uuid == crossing.Uuid);
    }

    [Fact]
    public void UncertainRequirementRetainsXpUntilLifecycleCancellationWithoutRouting()
    {
        var requirement = new MentorRelationshipRequirement(requestGeneration: 7);
        var captured = new MentorCapturedEvent(
            new MentorCaptureKey(
                new object(), "source", 5, true, 3,
                relationship: null, evidence: null, requirement: requirement),
            new MentorAmount(4, 2));
        var pending = new MentorPendingWork();

        requirement.MarkUncertain();
        requirement.Resolve(new MentorRelationshipSnapshot(
            4, 5,
            new[] { new MentorRecipe("source", 5, true), new MentorRecipe("recipient", 1, true) },
            new[] { new MentorRecipe("recipient", 1, true) }));
        Assert.Null(requirement.Resolved);
        Assert.True(pending.Unroutable.Retain(captured));
        Assert.Equal(1, pending.Unroutable.EventCount);
        Assert.Equal(4, pending.Unroutable.TotalAmount.Mantissa, 12);
        Assert.Equal(2, pending.Unroutable.TotalAmount.Exponent);
        Assert.Equal(0, pending.Engine.PendingCount);

        pending.CancelPending();
        Assert.Equal(0, pending.Unroutable.EventCount);
        Assert.False(pending.Unroutable.TotalAmount.IsValidPositive);
        Assert.Equal(0, pending.Engine.PendingCount);
    }

    [Fact]
    public void EvidenceBufferCoalescesOnlyUnpinnedLatestUuidAndPreservesCapturedHead()
    {
        var basis = new MentorRelationshipSnapshot(
            1, 5,
            new[] { new MentorRecipe("mentor", 5, true), new MentorRecipe("recipient", 1, true) },
            new[] { new MentorRecipe("recipient", 1, true) });
        var buffer = new MentorRelationshipEvidenceBuffer(capacity: 4);
        var pending = new MentorPendingWork();
        buffer.Rebase(basis);

        Assert.Equal(MentorEvidenceAppendResult.Added, buffer.Append("changed", 6, true, 2));
        Assert.Equal(MentorEvidenceAppendResult.Coalesced, buffer.Append("changed", 7, true, 3));
        Assert.Equal(2, buffer.VersionCount);
        var capturedHead = buffer.Head!;
        Assert.Equal(MentorCaptureResult.Added, pending.Captures.Capture(
            new MentorCaptureKey(new object(), "mentor", 5, true, 3, evidence: capturedHead),
            new MentorAmount(1, 0)));

        Assert.Equal(MentorEvidenceAppendResult.Added, buffer.Append("changed", 8, true, 4));
        Assert.Equal(3, buffer.VersionCount);
        Assert.NotSame(capturedHead, buffer.Head);
        Assert.Equal(7, ResolveEvidence(capturedHead).HighestMastery);
        Assert.Equal(8, ResolveEvidence(buffer.Head!).HighestMastery);

        pending.Captures.Cancel();
        Assert.Equal(0, buffer.CaptureReferences);
    }

    [Fact]
    [Trait("Category", "PerformanceSimulation")]
    public void SustainedInvalidationKeepsEvidenceBoundedAndTransfersExactXpPerDomain()
    {
        const int evidenceCapacity = 8;
        const int invalidations = 4096;
        var basis = new MentorRelationshipSnapshot(
            1, 5,
            new[] { new MentorRecipe("source", 5, true), new MentorRecipe("recipient", 1, true) },
            new[] { new MentorRecipe("recipient", 1, true) });
        var spellBuffer = new MentorRelationshipEvidenceBuffer(evidenceCapacity);
        var artifactBuffer = new MentorRelationshipEvidenceBuffer(evidenceCapacity);
        var spellPending = new MentorPendingWork();
        var artifactPending = new MentorPendingWork();
        var spellSource = new object();
        spellBuffer.Rebase(basis);
        artifactBuffer.Rebase(basis);
        Assert.Equal(MentorEvidenceAppendResult.Added, artifactBuffer.Append("artifact-change", 6, true, 2));
        Assert.Equal(MentorCaptureResult.Added, artifactPending.Captures.Capture(
            new MentorCaptureKey(new object(), "source", 5, true, 2, evidence: artifactBuffer.Head),
            new MentorAmount(2, 0)));

        var controlledRebases = 0;
        var overflowEvents = 0;
        for (var index = 0; index < invalidations; index++)
        {
            var result = spellBuffer.Append($"change-{index % 17:D2}", index + 6, true, index + 2);
            if (result == MentorEvidenceAppendResult.Overflow)
            {
                overflowEvents += spellPending.Captures.TransferEvidence(spellBuffer, spellPending.Unroutable);
                Assert.Equal(0, spellBuffer.CaptureReferences);
                spellBuffer.Invalidate();
                // Models the controlled authoritative commit that publishes a
                // fresh basis after the active bounded pass finishes.
                spellBuffer.Rebase(basis);
                controlledRebases++;
                result = spellBuffer.Append($"change-{index % 17:D2}", index + 6, true, index + 2);
            }
            Assert.Equal(MentorEvidenceAppendResult.Added, result);
            Assert.InRange(spellBuffer.VersionCount, 2, evidenceCapacity);
            Assert.Equal(MentorCaptureResult.Added, spellPending.Captures.Capture(
                new MentorCaptureKey(spellSource, "source", 5, true, 1, evidence: spellBuffer.Head),
                new MentorAmount(1, 0)));

            Assert.Equal(2, artifactBuffer.VersionCount);
            Assert.Equal(1, artifactBuffer.CaptureReferences);
            Assert.Equal(0, artifactPending.Unroutable.EventCount);
        }

        overflowEvents += spellPending.Captures.TransferEvidence(spellBuffer, spellPending.Unroutable);
        Assert.True(controlledRebases > 100);
        Assert.Equal(0, overflowEvents);
        Assert.Equal(0, spellBuffer.CaptureReferences);
        Assert.Equal(invalidations, spellPending.Unroutable.EventCount);
        Assert.Equal(4.096, spellPending.Unroutable.TotalAmount.Mantissa, 12);
        Assert.Equal(3, spellPending.Unroutable.TotalAmount.Exponent);
        Assert.Equal(1, artifactPending.Captures.EventCount);
        Assert.Equal(0, artifactPending.Unroutable.EventCount);

        spellBuffer.Invalidate();
        artifactPending.CancelPending();
        Assert.Equal(0, artifactBuffer.CaptureReferences);
    }

    [Fact]
    [Trait("Category", "PerformanceSimulation")]
    public void SustainedRequestsDoNotRestartAnActiveRefreshPass()
    {
        const int passSteps = 31;
        const int invalidations = 4096;
        var requests = new MentorWorkGeneration();
        var passGeneration = requests.Current;
        var progress = 0;
        var commits = 0;
        var hasActivePass = true;

        for (var index = 0; index < invalidations; index++)
        {
            requests.Request();
            Assert.False(MentorRefreshPassContinuity.ShouldStartNewPass(hasActivePass));
            progress++;
            if (progress != passSteps) continue;

            commits++;
            Assert.True(MentorRefreshPassContinuity.RequiresFollowUp(requests, passGeneration));
            hasActivePass = false;
            Assert.True(MentorRefreshPassContinuity.ShouldStartNewPass(hasActivePass));
            passGeneration = requests.Current;
            progress = 0;
            hasActivePass = true;
        }

        Assert.True(commits > 100);
        // Finish the superseded pass, then an immediate quiet follow-up. The
        // latter is authoritative and needs no third pass.
        while (progress++ < passSteps - 1) { }
        Assert.True(MentorRefreshPassContinuity.RequiresFollowUp(requests, passGeneration));
        hasActivePass = false;
        Assert.True(MentorRefreshPassContinuity.ShouldStartNewPass(hasActivePass));
        passGeneration = requests.Current;
        hasActivePass = true;
        for (var step = 0; step < passSteps; step++)
            Assert.False(MentorRefreshPassContinuity.ShouldStartNewPass(hasActivePass));
        Assert.False(MentorRefreshPassContinuity.RequiresFollowUp(requests, passGeneration));
    }

    [Fact]
    public void EvidenceTransferReportsOnlyXpBeyondTheBoundedUnroutableLedger()
    {
        var basis = new MentorRelationshipSnapshot(
            1, 5,
            new[] { new MentorRecipe("source", 5, true), new MentorRecipe("recipient", 1, true) },
            new[] { new MentorRecipe("recipient", 1, true) });
        var buffer = new MentorRelationshipEvidenceBuffer(capacity: 3);
        var captures = new MentorCaptureQueue(capacity: 2);
        var ledger = new MentorUnroutableLedger(capacity: 1);
        var diagnostics = new MentorDiagnostics();
        buffer.Rebase(basis);

        Assert.Equal(MentorEvidenceAppendResult.Added, buffer.Append("first", 6, true, 2));
        Assert.Equal(MentorCaptureResult.Added, captures.Capture(
            new MentorCaptureKey(new object(), "source-a", 5, true, 2, evidence: buffer.Head),
            new MentorAmount(2, 1)));
        Assert.Equal(MentorEvidenceAppendResult.Added, buffer.Append("second", 7, true, 3));
        Assert.Equal(MentorCaptureResult.Added, captures.Capture(
            new MentorCaptureKey(new object(), "source-b", 5, true, 3, evidence: buffer.Head),
            new MentorAmount(3, 1)));

        var overflowEvents = captures.TransferEvidence(buffer, ledger);
        diagnostics.RecordDrop(MentorDropReason.CaptureOverflow, overflowEvents, grant: false);

        Assert.Equal(1, ledger.EventCount);
        Assert.Equal(1, overflowEvents);
        Assert.Equal(1, diagnostics.DropCount(MentorDropReason.CaptureOverflow));
        Assert.Equal(2, ledger.TotalAmount.Mantissa, 12);
        Assert.Equal(1, ledger.TotalAmount.Exponent);
        Assert.Equal(0, buffer.CaptureReferences);
        Assert.Equal(0, captures.EventCount);
    }

    [Fact]
    public void EvidenceTransferRequeuesResolvedHeadsAndRetainsOnlyUnresolvedXp()
    {
        var basis = new MentorRelationshipSnapshot(
            1, 5,
            new[] { new MentorRecipe("source", 5, true), new MentorRecipe("recipient", 1, true) },
            new[] { new MentorRecipe("recipient", 1, true) });
        var buffer = new MentorRelationshipEvidenceBuffer(capacity: 4);
        var captures = new MentorCaptureQueue(capacity: 4);
        var ledger = new MentorUnroutableLedger(capacity: 4);
        buffer.Rebase(basis);

        Assert.Equal(MentorEvidenceAppendResult.Added, buffer.Append("resolved-change", 6, true, 2));
        var resolvedHead = buffer.Head!;
        Assert.Equal(MentorCaptureResult.Added, captures.Capture(
            new MentorCaptureKey(new object(), "resolved-source", 6, true, 2, evidence: resolvedHead),
            new MentorAmount(2, 0)));
        ResolveEvidence(resolvedHead);
        Assert.NotNull(resolvedHead.Resolved);

        Assert.Equal(MentorEvidenceAppendResult.Added, buffer.Append("unresolved-change", 7, true, 3));
        Assert.Equal(MentorCaptureResult.Added, captures.Capture(
            new MentorCaptureKey(new object(), "unresolved-source", 7, true, 3, evidence: buffer.Head),
            new MentorAmount(3, 0)));

        Assert.Equal(0, captures.TransferEvidence(buffer, ledger));
        Assert.Equal(0, buffer.CaptureReferences);
        Assert.Equal(1, ledger.EventCount);
        Assert.Equal(3, ledger.TotalAmount.Mantissa, 12);
        Assert.True(captures.TryTake(out var rebound));
        Assert.NotNull(rebound.Key.Relationship);
        Assert.Null(rebound.Key.Evidence);
        Assert.Equal(2, rebound.Amount.Mantissa, 12);
        Assert.False(captures.TryTake(out _));
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

    private static void ResolveNextCapturedEvidence(MentorPendingWork pending)
    {
        Assert.True(pending.Captures.TryTake(out var captured));
        var snapshot = ResolveEvidence(captured.Key.Evidence!);
        var resolved = new MentorCaptureKey(
            captured.Key.Source, captured.Key.Uuid, captured.Key.MasteryLevel,
            captured.Key.Discovered, captured.Key.ProgressionEpoch, snapshot);
        Assert.Equal(MentorQualificationStatus.Qualified,
            MentorRelationshipQualification.Evaluate(resolved, 99, 99, 0));
        pending.Sources.Capture(snapshot.ForCapture(resolved), resolved.Uuid, captured.Amount, captured.EventCount);
    }

    private static MentorRelationshipSnapshot ResolveEvidence(MentorRelationshipEvidence evidence)
    {
        using var work = new MentorRelationshipResolutionWork(evidence);
        while (!work.IsComplete) work.Step();
        return work.Result!;
    }

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
