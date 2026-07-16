using OrbMentor;
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
    public void LaterProgressionCannotRetroactivelyQualifyAnEarlierSource()
    {
        var source = new object();
        var atEvent = new MentorCaptureKey(source, "source", 5, true, progressionEpoch: 7);

        // Another recipe was level 6 at capture time, then fell below this
        // source before processing. Final-state-only qualification would grant.
        Assert.Equal(
            MentorQualificationStatus.StaleRelationship,
            MentorRelationshipQualification.Evaluate(atEvent, relationshipEpoch: 8, highestMastery: 5, recipientCount: 2));
        Assert.Equal(
            MentorQualificationStatus.Qualified,
            MentorRelationshipQualification.Evaluate(atEvent, relationshipEpoch: 7, highestMastery: 5, recipientCount: 2));
        Assert.Equal(
            MentorQualificationStatus.SourceIneligible,
            MentorRelationshipQualification.Evaluate(atEvent, relationshipEpoch: 7, highestMastery: 6, recipientCount: 2));
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
        var captured = new MentorCaptureKey(new object(), "source", cachedMastery, cachedDiscovered, epoch);

        Assert.Equal(4, epoch);
        Assert.True(dirty);
        Assert.Equal(
            MentorQualificationStatus.StaleRelationship,
            MentorRelationshipQualification.Evaluate(captured, relationshipEpoch: 3, highestMastery: 5, recipientCount: 1));
        Assert.Equal(
            MentorQualificationStatus.Qualified,
            MentorRelationshipQualification.Evaluate(captured, relationshipEpoch: 4, highestMastery: 6, recipientCount: 1));
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

        Assert.True(engine.TryPeek(out var first));
        Assert.True(engine.TryPeek(out var second));
        Assert.Equal(first.Amount, second.Amount);
        Assert.Equal(1, engine.PendingCount);
        Assert.True(engine.Complete("recipient"));
        Assert.Equal(0, engine.PendingCount);
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
        diagnostics.RecordDrop(MentorDropReason.StaleRelationship, 3, grant: false);
        diagnostics.RecordDrop(MentorDropReason.RecipientIdentityChanged, 2, grant: true);
        Assert.Equal(3, diagnostics.DroppedEvents);
        Assert.Equal(2, diagnostics.DroppedGrants);
        Assert.Equal(3, diagnostics.DropCount(MentorDropReason.StaleRelationship));
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
