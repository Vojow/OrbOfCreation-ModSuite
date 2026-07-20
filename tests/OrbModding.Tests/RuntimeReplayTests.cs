using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.ReplayConverter;
using OrbModding.RuntimeReplay;
using OrbModding.Tests.Scenarios;
using Xunit;
using ReplayDocument = OrbModding.RuntimeReplay.RuntimeReplay;

namespace OrbModding.Tests;

[Trait("Category", "Reliability")]
public sealed class RuntimeReplayTests
{
    private const string CandidateA = "33333333-3333-4333-8333-333333333333";
    private const string CandidateB = "44444444-4444-4444-8444-444444444444";

    [Theory]
    [InlineData("queue-refill-v1.json")]
    [InlineData("chained-progression-v1.json")]
    [Trait("Category", "HeadlessE2E")]
    public void CanonicalFixtures_AreCopiedAndRoundTripDeterministically(string fixtureName)
    {
        var path = FixturePath(fixtureName);
        Assert.True(File.Exists(path), $"Copied replay fixture was not found at {path}.");
        var source = File.ReadAllText(path).TrimEnd();
        var parsed = ReplayJsonCodec.Parse(source);
        var canonical = ReplayJsonCodec.Write(parsed);

        Assert.Equal(source, canonical);
        Assert.Equal(canonical, ReplayJsonCodec.Write(ReplayJsonCodec.Parse(canonical)));
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void QueueRefillReplay_ReplaysThroughProductionSchedulerAndRefillsAfterCompletion()
    {
        var replay = LoadFixture("queue-refill-v1.json");
        using var dispatcher = new RuntimeReplayDispatcher(replay);

        var result = dispatcher.Run();

        Assert.True(result.TotalSubmitted >= 6);
        Assert.Equal(1, result.TotalCompleted);
        Assert.Equal(5, result.QueueCount);
        Assert.Equal(5, result.QueueHighWater);
        Assert.InRange(result.QueueCount, 0, replay.Setup.QueueCapacity);
        Assert.Contains(result.Invalidations, kinds => (kinds & GameplayInvalidationKind.Queue) != 0);
        Assert.All(
            dispatcher.AutoBuy.World.Candidates,
            candidate => Assert.Equal(
                replay.Setup.PrimaryResource.Identity.Uuid,
                Assert.Single(candidate.ResourceDependencies)));
        Assert.All(result.DispatchTrace, observation =>
        {
            Assert.Equal(observation.DeclaredFrame, observation.ActualFrame);
            Assert.Equal(observation.DeclaredMicroseconds, observation.ActualMicroseconds);
        });
        ScenarioOracles.OneMutationOwnerPerFrame(dispatcher.Kernel);
        ScenarioOracles.MutationRequestsAreUnique(dispatcher.Kernel);
        ScenarioOracles.NoLifecycleDispatchFailures(dispatcher.Kernel);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void ChainedProgressionReplay_UnlocksCompletesAndInvalidatesInRecordedOrder()
    {
        var replay = LoadFixture("chained-progression-v1.json");
        using var dispatcher = new RuntimeReplayDispatcher(replay);

        var result = dispatcher.Run();

        Assert.Equal(2, result.TotalSubmitted);
        Assert.Equal(2, result.TotalCompleted);
        Assert.Equal(new[] { CandidateA, CandidateB }, result.SubmissionOrder);
        Assert.InRange(result.QueueCount, 0, replay.Setup.QueueCapacity);
        Assert.Contains(result.Invalidations, kinds => (kinds & GameplayInvalidationKind.Progression) != 0);
        Assert.Contains(result.Invalidations, kinds => (kinds & GameplayInvalidationKind.Inventory) != 0);
        Assert.Contains(result.Invalidations, kinds => (kinds & GameplayInvalidationKind.ResourceQuantity) != 0);
        Assert.Contains(result.Invalidations, kinds => (kinds & GameplayInvalidationKind.Configuration) != 0);
        Assert.Equal(
            0,
            dispatcher.AutoBuy.World.GetResourceQuantity(replay.Setup.PrimaryResource.Identity.Uuid)
                .CompareTo(new BigAmount(100, 0)));
        Assert.Equal(
            new[] { "lifecycle", "progression", "completion", "inventory", "resource", "configuration", "queue" },
            result.DispatchTrace.Select(value => value.Kind).Distinct(StringComparer.Ordinal).ToArray());
        ScenarioOracles.MutationRequestsAreUnique(dispatcher.Kernel);
        ScenarioOracles.NoLifecycleDispatchFailures(dispatcher.Kernel);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void RepeatedReplay_ProducesIdenticalTraceMutationsAndQueueOutcome()
    {
        var replay = LoadFixture("chained-progression-v1.json");

        var first = Execute(replay);
        var second = Execute(replay);

        Assert.Equal(first.Snapshot, second.Snapshot);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void LifecycleReplay_OldGenerationInvalidationIsRejectedByProductionBus()
    {
        var replay = ReplayWithReset();
        using var dispatcher = new RuntimeReplayDispatcher(replay);
        var result = dispatcher.Run(settleFrames: 0);
        var playingGeneration = result.DispatchTrace.Single(value => value.Sequence == 4).LifecycleGeneration;

        var accepted = dispatcher.Kernel.TryPublishForGeneration(
            playingGeneration,
            GameplayInvalidationKind.Queue,
            GameplayInvalidationDomains.AutomataStructures,
            CandidateA,
            "StructureSO",
            out var reason);

        Assert.False(accepted);
        Assert.Contains("stale", reason, StringComparison.OrdinalIgnoreCase);
        Assert.True(dispatcher.Kernel.Lifecycle.Current.Generation > playingGeneration);
    }

    [Fact]
    public void Codec_ParsesExactlySevenTypedEventVariants()
    {
        var replay = LoadFixture("chained-progression-v1.json");

        Assert.Contains(replay.Events, value => value is LifecycleReplayEvent);
        Assert.Contains(replay.Events, value => value is ResourceReplayEvent);
        Assert.Contains(replay.Events, value => value is QueueReplayEvent);
        Assert.Contains(replay.Events, value => value is ProgressionReplayEvent);
        Assert.Contains(replay.Events, value => value is InventoryReplayEvent);
        Assert.Contains(replay.Events, value => value is ConfigurationReplayEvent);
        Assert.Contains(replay.Events, value => value is CompletionReplayEvent);
        Assert.Equal(7, replay.Events.Select(value => value.GetType()).Distinct().Count());
    }

    [Fact]
    public void SetupCodec_RequiresExactTypedPrimaryResource()
    {
        const string wrongType = "{\"queueCapacity\":3,\"primaryResource\":{\"uuid\":\"77777777-7777-4777-8777-777777777777\",\"expectedNativeType\":\"PlayerSO\",\"initialQuantity\":100},\"candidates\":[{\"uuid\":\"33333333-3333-4333-8333-333333333333\",\"expectedNativeType\":\"StructureSO\",\"baseCost\":1,\"costScaling\":1,\"available\":true,\"maximumLevel\":1}]}";
        const string detachedScalar = "{\"queueCapacity\":3,\"initialResourceQuantity\":100,\"candidates\":[]}";
        const string crossTypeUuidCollision = "{\"queueCapacity\":3,\"primaryResource\":{\"uuid\":\"77777777-7777-4777-8777-777777777777\",\"expectedNativeType\":\"ResourceSO\",\"initialQuantity\":100},\"candidates\":[{\"uuid\":\"33333333-3333-4333-8333-333333333333\",\"expectedNativeType\":\"StructureSO\",\"baseCost\":1,\"costScaling\":1,\"available\":true,\"maximumLevel\":1},{\"uuid\":\"33333333-3333-4333-8333-333333333333\",\"expectedNativeType\":\"UpgradeSO\",\"baseCost\":1,\"costScaling\":1,\"available\":true,\"maximumLevel\":1}]}";

        var wrongTypeException = Assert.Throws<ReplayFormatException>(() => ReplayJsonCodec.ParseSetup(wrongType));
        var detachedException = Assert.Throws<ReplayFormatException>(() => ReplayJsonCodec.ParseSetup(detachedScalar));
        var collisionException = Assert.Throws<ReplayFormatException>(() => ReplayJsonCodec.ParseSetup(crossTypeUuidCollision));

        Assert.Contains("ResourceSO", wrongTypeException.Message, StringComparison.Ordinal);
        Assert.Contains("initialResourceQuantity", detachedException.Message, StringComparison.Ordinal);
        Assert.Contains("UUIDs must be unique", collisionException.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void CompletionReplay_MismatchedFrontIdentityRejectsBeforeQueueOrLevelMutation()
    {
        var replay = ReplayForCompletionPreflight(claimCandidateB: true, manualFront: false);
        using var dispatcher = new RuntimeReplayDispatcher(replay);

        var exception = Assert.Throws<InvalidOperationException>(() => dispatcher.Run(settleFrames: 0));

        Assert.Contains("not Upgrade", exception.Message, StringComparison.Ordinal);
        Assert.Equal(2, dispatcher.AutoBuy.World.QueueCount);
        Assert.Equal(0, dispatcher.AutoBuy.World.TotalCompleted);
        Assert.Equal(0, dispatcher.AutoBuy.Candidate(CandidateA).CurrentLevel);
        Assert.Equal(1, dispatcher.AutoBuy.Candidate(CandidateA).QueuedLevels);
        Assert.Equal(0, dispatcher.AutoBuy.Candidate(CandidateB).CurrentLevel);
        Assert.Equal(1, dispatcher.AutoBuy.Candidate(CandidateB).QueuedLevels);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void CompletionReplay_ManualFrontRejectsBeforeQueueOrLevelMutation()
    {
        var replay = ReplayForCompletionPreflight(claimCandidateB: false, manualFront: true);
        using var dispatcher = new RuntimeReplayDispatcher(replay);

        var exception = Assert.Throws<InvalidOperationException>(() => dispatcher.Run(settleFrames: 0));

        Assert.Contains("manual action", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, dispatcher.AutoBuy.World.QueueCount);
        Assert.Equal(0, dispatcher.AutoBuy.World.TotalCompleted);
        Assert.Equal(0, dispatcher.AutoBuy.Candidate(CandidateA).CurrentLevel);
        Assert.Equal(1, dispatcher.AutoBuy.Candidate(CandidateA).QueuedLevels);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void CompletionReplay_LaterRequestedMismatchRejectsEntireBatchBeforeMutation()
    {
        var replay = ReplayForCompletionPreflight(
            claimCandidateB: false,
            manualFront: false,
            completionCount: 2);
        using var dispatcher = new RuntimeReplayDispatcher(replay);

        var exception = Assert.Throws<InvalidOperationException>(() => dispatcher.Run(settleFrames: 0));

        Assert.Contains("Queue entry 1", exception.Message, StringComparison.Ordinal);
        Assert.Equal(2, dispatcher.AutoBuy.World.QueueCount);
        Assert.Equal(0, dispatcher.AutoBuy.World.TotalCompleted);
        Assert.Equal(0, dispatcher.AutoBuy.Candidate(CandidateA).CurrentLevel);
        Assert.Equal(1, dispatcher.AutoBuy.Candidate(CandidateA).QueuedLevels);
        Assert.Equal(0, dispatcher.AutoBuy.Candidate(CandidateB).CurrentLevel);
        Assert.Equal(1, dispatcher.AutoBuy.Candidate(CandidateB).QueuedLevels);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void ResourceReplay_DifferentResourceIdentityRejectsBeforeBalanceMutation()
    {
        var resource = new ReplayResource(
            new ReplayIdentity("77777777-7777-4777-8777-777777777777", "ResourceSO"),
            100);
        var setup = new ReplaySetup(3, resource, new[]
        {
            new ReplayCandidate(new ReplayIdentity(CandidateA, "StructureSO"), 1, 1, true, 1),
        });
        var replay = new ReplayDocument(
            ReplayDocument.SchemaIdentifier,
            1,
            "resource-identity-mismatch-v1",
            setup,
            new ReplayEvent[]
            {
                new ResourceReplayEvent(
                    0,
                    0,
                    0,
                    new ReplayIdentity("88888888-8888-4888-8888-888888888888", "ResourceSO"),
                    999),
            });
        using var dispatcher = new RuntimeReplayDispatcher(ReplayJsonCodec.Parse(ReplayJsonCodec.Write(replay)));

        var exception = Assert.Throws<InvalidOperationException>(() => dispatcher.Run(settleFrames: 0));

        Assert.Contains("does not match setup primary resource", exception.Message, StringComparison.Ordinal);
        Assert.Equal(
            0,
            dispatcher.AutoBuy.World.GetResourceQuantity(resource.Identity.Uuid).CompareTo(new BigAmount(100, 0)));
        Assert.Equal(0, dispatcher.AutoBuy.World.TotalSubmitted);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void ResourceReplay_RefreshesCachedAffordabilityAndMakesCandidatePurchasable()
    {
        var setup = SingleCandidateSetup(initialResourceQuantity: 0, candidateAvailable: true);
        var events = LoadedGameplayEvents("resource-refresh");
        events.Add(new QueueReplayEvent(events.Count, 8, 3_000_000, 0));
        events.Add(new ResourceReplayEvent(
            events.Count,
            8,
            3_000_000,
            setup.PrimaryResource.Identity,
            20));
        var replay = CanonicalReplay("resource-refresh-v1", setup, events);
        using var dispatcher = new RuntimeReplayDispatcher(replay);

        var result = dispatcher.Run(settleFrames: 120);

        Assert.Equal(1, result.TotalSubmitted);
        Assert.Equal(1, result.QueueCount);
        Assert.Equal(
            0,
            dispatcher.AutoBuy.World.GetResourceQuantity(setup.PrimaryResource.Identity.Uuid)
                .CompareTo(new BigAmount(10, 0)));
        Assert.Contains(result.Invalidations, kinds => (kinds & GameplayInvalidationKind.ResourceQuantity) != 0);
        Assert.True(dispatcher.AutoBuy.Candidate(CandidateA).DirtyMarks > 0);
        ScenarioOracles.NoLifecycleDispatchFailures(dispatcher.Kernel);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void SameFrameLifecycleAndNativeEvents_PreserveSequenceAndExactTimestamp()
    {
        var setup = SingleCandidateSetup(initialResourceQuantity: 0, candidateAvailable: false);
        var events = new List<ReplayEvent>
        {
            new LifecycleReplayEvent(0, 1, 1_000, "SceneEntered", "Main", "same-frame-main"),
            new LifecycleReplayEvent(1, 1, 1_000, "SaveLoadStarted", "Main", "same-frame-main"),
            new QueueReplayEvent(2, 1, 1_000, 0),
            new LifecycleReplayEvent(3, 2, 2_000, "SaveLoaded", "Main", "same-frame-main"),
            new LifecycleReplayEvent(4, 2, 2_000, "RegistryRebuilt", "Main", "same-frame-registry"),
            new LifecycleReplayEvent(5, 2, 2_000, "RuntimeReady", "Main", "same-frame-main"),
            new ProgressionReplayEvent(6, 2, 2_000, setup.Candidates[0].Identity, true),
            new ResourceReplayEvent(7, 2, 2_000, setup.PrimaryResource.Identity, 10),
            new CompletionReplayEvent(8, 4, 4_000, setup.Candidates[0].Identity, 1),
        };
        var replay = CanonicalReplay("same-frame-order-v1", setup, events);
        using var dispatcher = new RuntimeReplayDispatcher(replay);

        var result = dispatcher.Run(settleFrames: 0);

        Assert.Equal(1, result.TotalSubmitted);
        Assert.Equal(1, result.TotalCompleted);
        Assert.Equal(0, result.QueueCount);
        Assert.Equal(Enumerable.Range(0, events.Count), result.DispatchTrace.Select(value => value.Sequence));
        Assert.All(result.DispatchTrace, observation =>
        {
            Assert.Equal(observation.DeclaredFrame, observation.ActualFrame);
            Assert.Equal(observation.DeclaredMicroseconds, observation.ActualMicroseconds);
        });
        Assert.Equal(2, result.DispatchTrace.Count(value => value.DeclaredFrame == 1 && value.DeclaredMicroseconds == 1_000 && value.Kind == "lifecycle"));
        Assert.Equal(3, dispatcher.Kernel.LifecycleTrace.Count(value => value.Current.LastFrame == 2));
        ScenarioOracles.NoLifecycleDispatchFailures(dispatcher.Kernel);
        ScenarioOracles.MutationRequestsAreUnique(dispatcher.Kernel);
    }

    [Fact]
    public void QueueReplay_OverflowRejectsAtomicallyBeforeAddingManualActions()
    {
        var setup = SingleCandidateSetup(initialResourceQuantity: 100, candidateAvailable: false) with
        {
            QueueCapacity = 2,
        };
        var replay = CanonicalReplay(
            "manual-overflow-v1",
            setup,
            new ReplayEvent[] { new QueueReplayEvent(0, 0, 0, 3) });
        using var dispatcher = new RuntimeReplayDispatcher(replay);

        var exception = Assert.Throws<InvalidOperationException>(() => dispatcher.Run(settleFrames: 0));

        Assert.Contains("rejected before mutation", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, dispatcher.AutoBuy.World.QueueCount);
        Assert.Equal(0, dispatcher.AutoBuy.World.QueueHighWater);
        Assert.Equal(0, dispatcher.AutoBuy.World.TotalSubmitted);
    }

    [Fact]
    public void Codec_RejectsUnreplayableTimingBeforeDispatch()
    {
        var setup = SingleCandidateSetup(initialResourceQuantity: 100, candidateAvailable: false);
        var differentTimes = new ReplayEvent[]
        {
            new QueueReplayEvent(0, 1, 1_000, 0),
            new QueueReplayEvent(1, 1, 1_001, 0),
        };
        var oversized = new ReplayEvent[]
        {
            new QueueReplayEvent(0, ReplayDocument.MaximumFrame + 1, 1_000, 0),
        };
        var nonDivisible = new ReplayEvent[]
        {
            new QueueReplayEvent(0, 2, 1_001, 0),
        };
        var oversizedTime = new ReplayEvent[]
        {
            new QueueReplayEvent(0, 1, ReplayDocument.MaximumMicroseconds + 1, 0),
        };

        var timeException = Assert.Throws<ReplayFormatException>(() =>
            ReplayJsonCodec.Write(new ReplayDocument(ReplayDocument.SchemaIdentifier, 1, "different-times-v1", setup, differentTimes)));
        var frameException = Assert.Throws<ReplayFormatException>(() =>
            ReplayJsonCodec.Write(new ReplayDocument(ReplayDocument.SchemaIdentifier, 1, "oversized-frame-v1", setup, oversized)));
        var divisibilityException = Assert.Throws<ReplayFormatException>(() =>
            ReplayJsonCodec.Write(new ReplayDocument(ReplayDocument.SchemaIdentifier, 1, "non-divisible-v1", setup, nonDivisible)));
        var timeLimitException = Assert.Throws<ReplayFormatException>(() =>
            ReplayJsonCodec.Write(new ReplayDocument(ReplayDocument.SchemaIdentifier, 1, "oversized-time-v1", setup, oversizedTime)));

        Assert.Contains("same frame", timeException.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(ReplayDocument.MaximumFrame.ToString(), frameException.Message, StringComparison.Ordinal);
        Assert.Contains("divisible", divisibilityException.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(ReplayDocument.MaximumMicroseconds.ToString(), timeLimitException.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Converter_UnreplayableTimingLeavesNoOutput()
    {
        var setup = SingleCandidateSetup(initialResourceQuantity: 100, candidateAvailable: false);
        using var temporary = new TemporaryDirectory();
        var setupPath = Path.Combine(temporary.Path, "setup.json");
        var observationsPath = Path.Combine(temporary.Path, "observations.jsonl");
        var outputPath = Path.Combine(temporary.Path, "unreplayable.json");
        File.WriteAllText(setupPath, ReplayJsonCodec.WriteSetup(setup));
        File.WriteAllText(
            observationsPath,
            ReplayJsonCodec.WriteEvent(new QueueReplayEvent(0, 2, 3, 0)));

        var exception = Assert.Throws<ReplayFormatException>(() =>
            ReplayConversion.Convert(setupPath, observationsPath, outputPath, "unreplayable-v1"));

        Assert.Contains("divisible", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(outputPath));
    }

    [Theory]
    [InlineData("{\"schema\":\"orb-of-creation/runtime-replay\",\"schemaVersion\":2,\"replayId\":\"x\",\"setup\":{},\"events\":[]}", "schemaVersion")]
    [InlineData("{\"schema\":\"orb-of-creation/runtime-replay\",\"schemaVersion\":1,\"replayId\":\"x\",\"setup\":{},\"events\":[],\"savePath\":\"private.sav\"}", "savePath")]
    [InlineData("{\"schema\":\"wrong\",\"schemaVersion\":1,\"replayId\":\"x\",\"setup\":{},\"events\":[]}", "Unsupported schema")]
    [InlineData("{\"schemaVersion\":1,\"replayId\":\"x\",\"setup\":{},\"events\":[]}", "schema")]
    public void Codec_RejectsUnknownVersionsAndPrivateShapedMembers(string json, string expected)
    {
        var exception = Assert.Throws<ReplayFormatException>(() => ReplayJsonCodec.Parse(json));
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"sequence\":0,\"atFrame\":1,\"atMicroseconds\":1.5,\"kind\":\"queue\",\"manualActions\":0}", "integer")]
    [InlineData("{\"sequence\":0,\"atFrame\":1,\"atMicroseconds\":1,\"kind\":\"progression\",\"uuid\":\"33333333-3333-4333-8333-333333333333\",\"expectedNativeType\":\"UpgradeSO\",\"available\":true,\"playerName\":\"secret\"}", "playerName")]
    [InlineData("{\"sequence\":0,\"atFrame\":1,\"atMicroseconds\":1,\"kind\":\"progression\",\"uuid\":\"33333333-3333-4333-8333-33333333333A\",\"expectedNativeType\":\"UpgradeSO\",\"available\":true}", "canonical")]
    [InlineData("{\"sequence\":0,\"atFrame\":1,\"atMicroseconds\":1,\"kind\":\"configuration\",\"setting\":\"Arbitrary.Private.Key\",\"enabled\":true}", "AutoBuyEnabled")]
    [InlineData("{\"sequence\":0,\"atFrame\":1,\"atMicroseconds\":1,\"kind\":\"resource\",\"uuid\":\"33333333-3333-4333-8333-333333333333\",\"expectedNativeType\":\"PlayerSO\",\"quantity\":1}", "ResourceSO")]
    [InlineData("{\"sequence\":0,\"atFrame\":1,\"atMicroseconds\":1,\"kind\":\"inventory\",\"uuid\":\"33333333-3333-4333-8333-333333333333\",\"expectedNativeType\":\"PlayerSO\",\"quantity\":1}", "Inventory")]
    [InlineData("{\"sequence\":0,\"atFrame\":1,\"atMicroseconds\":1,\"kind\":\"completion\",\"uuid\":\"33333333-3333-4333-8333-333333333333\",\"expectedNativeType\":\"ArtifactSO\",\"count\":1}", "Completion")]
    [InlineData("{\"sequence\":0,\"atFrame\":1,\"atMicroseconds\":1,\"kind\":\"lifecycle\",\"transition\":\"NewGamePlusCompleted\",\"sceneName\":\"Main\",\"nativeIdentityToken\":\"main\"}", "Unknown lifecycle")]
    [InlineData("{\"sequence\":0,\"atFrame\":1,\"atMicroseconds\":1,\"kind\":\"lifecycle\",\"transition\":\"SceneEntered\",\"sceneName\":\"C:/private/save\",\"nativeIdentityToken\":\"main\"}", "sceneName")]
    [InlineData("{\"sequence\":0,\"atFrame\":1,\"atMicroseconds\":1,\"kind\":\"resource\",\"uuid\":\"33333333-3333-4333-8333-333333333333\",\"expectedNativeType\":\"ResourceSO\",\"quantity\":1e3}", "non-exponent")]
    public void EventCodec_FailsClosedOnNonCanonicalOrOpenEndedInput(string json, string expected)
    {
        var exception = Assert.Throws<ReplayFormatException>(() => ReplayJsonCodec.ParseEvent(json));
        Assert.Contains(expected, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Codec_RejectsNonContiguousOrReverseOrderedEvents()
    {
        var replay = LoadFixture("queue-refill-v1.json");
        var changed = replay with
        {
            Events = replay.Events.Select((value, index) => index == 1 ? value with { Sequence = 4 } : value).ToArray()
        };
        Assert.Throws<ReplayFormatException>(() => ReplayJsonCodec.Write(changed));

        changed = replay with
        {
            Events = replay.Events.Select((value, index) => index == 1 ? value with { AtFrame = 0 } : value).ToArray()
        };
        Assert.Throws<ReplayFormatException>(() => ReplayJsonCodec.Write(changed));
    }

    [Fact]
    public void Converter_CombinesReviewedSetupAndSanitizedJsonlIntoCanonicalFixture()
    {
        var source = LoadFixture("queue-refill-v1.json");
        using var temporary = new TemporaryDirectory();
        var setupPath = Path.Combine(temporary.Path, "setup.json");
        var observationsPath = Path.Combine(temporary.Path, "observations.jsonl");
        var outputPath = Path.Combine(temporary.Path, "converted.json");
        File.WriteAllText(setupPath, ReplayJsonCodec.WriteSetup(source.Setup));
        File.WriteAllLines(observationsPath, source.Events.Select(ReplayJsonCodec.WriteEvent));

        ReplayConversion.Convert(setupPath, observationsPath, outputPath, "converted-queue-refill");

        var convertedText = File.ReadAllText(outputPath);
        var converted = ReplayJsonCodec.Parse(convertedText);
        Assert.Equal("converted-queue-refill", converted.ReplayId);
        Assert.Equal(source.Events.Count, converted.Events.Count);
        Assert.Equal(convertedText, ReplayJsonCodec.Write(converted));
    }

    [Fact]
    public void Converter_InvalidObservationLeavesNoOutput()
    {
        var source = LoadFixture("queue-refill-v1.json");
        using var temporary = new TemporaryDirectory();
        var setupPath = Path.Combine(temporary.Path, "setup.json");
        var observationsPath = Path.Combine(temporary.Path, "observations.jsonl");
        var outputPath = Path.Combine(temporary.Path, "must-not-exist.json");
        File.WriteAllText(setupPath, ReplayJsonCodec.WriteSetup(source.Setup));
        File.WriteAllText(observationsPath, "{\"kind\":\"native-log-line\",\"message\":\"unsanitized\"}");

        var exception = Assert.Throws<ReplayFormatException>(() =>
            ReplayConversion.Convert(setupPath, observationsPath, outputPath, "rejected"));

        Assert.Contains("line 1", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public void Converter_InvalidObservationDoesNotClobberExistingReviewedOutput()
    {
        var source = LoadFixture("queue-refill-v1.json");
        using var temporary = new TemporaryDirectory();
        var setupPath = Path.Combine(temporary.Path, "setup.json");
        var observationsPath = Path.Combine(temporary.Path, "observations.jsonl");
        var outputPath = Path.Combine(temporary.Path, "reviewed.json");
        File.WriteAllText(setupPath, ReplayJsonCodec.WriteSetup(source.Setup));
        File.WriteAllText(observationsPath, "{\"kind\":\"unsupported\"}");
        File.WriteAllText(outputPath, "reviewed-content");

        Assert.Throws<ReplayFormatException>(() =>
            ReplayConversion.Convert(setupPath, observationsPath, outputPath, "rejected"));

        Assert.Equal("reviewed-content", File.ReadAllText(outputPath));
        Assert.Empty(Directory.GetFiles(temporary.Path, ".*.tmp"));
    }

    private static ReplayDocument LoadFixture(string name) => ReplayJsonCodec.Parse(File.ReadAllText(FixturePath(name)));

    private static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", "replays", name);

    private static (string Snapshot, RuntimeReplayResult Result) Execute(ReplayDocument replay)
    {
        using var dispatcher = new RuntimeReplayDispatcher(replay);
        var result = dispatcher.Run();
        var snapshot = string.Join("|",
            result.TotalSubmitted,
            result.TotalCompleted,
            result.QueueCount,
            result.QueueHighWater,
            string.Join(",", result.SubmissionOrder),
            string.Join(",", result.Mutations.Select(value => $"{value.Frame}:{value.LifecycleGeneration}:{value.Feature}:{value.ActionFamily}:{value.Target}:{value.RequestId}")),
            string.Join(",", result.DispatchTrace.Select(value => $"{value.Sequence}:{value.Kind}:{value.ActualFrame}:{value.ActualMicroseconds}:{value.LifecycleGeneration}")));
        return (snapshot, result);
    }

    private static ReplayDocument ReplayWithReset()
    {
        var identity = new ReplayIdentity(CandidateA, "StructureSO");
        var resource = new ReplayResource(
            new ReplayIdentity("77777777-7777-4777-8777-777777777777", "ResourceSO"),
            100);
        var setup = new ReplaySetup(3, resource, new[] { new ReplayCandidate(identity, 1, 1, true, 1) });
        var events = new ReplayEvent[]
        {
            new LifecycleReplayEvent(0, 1, 0, "SceneEntered", "Main", "reset-main"),
            new LifecycleReplayEvent(1, 2, 0, "SaveLoadStarted", "Main", "reset-main"),
            new LifecycleReplayEvent(2, 3, 0, "SaveLoaded", "Main", "reset-main"),
            new LifecycleReplayEvent(3, 4, 0, "RegistryRebuilt", "Main", "reset-registry"),
            new LifecycleReplayEvent(4, 5, 0, "RuntimeReady", "Main", "reset-main"),
            new LifecycleReplayEvent(5, 6, 0, "ResetStarted", "Main", "reset-main"),
            new LifecycleReplayEvent(6, 7, 0, "ResetCompleted", "Main", "reset-main"),
        };
        return ReplayJsonCodec.Parse(ReplayJsonCodec.Write(new ReplayDocument(ReplayDocument.SchemaIdentifier, 1, "stale-generation-v1", setup, events)));
    }

    private static ReplayDocument ReplayForCompletionPreflight(
        bool claimCandidateB,
        bool manualFront,
        int completionCount = 1)
    {
        var resource = new ReplayResource(
            new ReplayIdentity("77777777-7777-4777-8777-777777777777", "ResourceSO"),
            100);
        var candidates = manualFront
            ? new[] { new ReplayCandidate(new ReplayIdentity(CandidateA, "StructureSO"), 1, 1, true, 1) }
            : new[]
            {
                new ReplayCandidate(new ReplayIdentity(CandidateA, "StructureSO"), 1, 1, true, 1),
                new ReplayCandidate(new ReplayIdentity(CandidateB, "UpgradeSO"), 1, 1, true, 1),
            };
        var claimed = claimCandidateB
            ? new ReplayIdentity(CandidateB, "UpgradeSO")
            : new ReplayIdentity(CandidateA, "StructureSO");
        var events = new List<ReplayEvent>();
        events.Add(new LifecycleReplayEvent(events.Count, 1, 0, "SceneEntered", "Main", "preflight-main"));
        events.Add(new LifecycleReplayEvent(events.Count, 2, 0, "SaveLoadStarted", "Main", "preflight-main"));
        events.Add(new LifecycleReplayEvent(events.Count, 3, 0, "SaveLoaded", "Main", "preflight-main"));
        events.Add(new LifecycleReplayEvent(events.Count, 4, 0, "RegistryRebuilt", "Main", "preflight-registry"));
        events.Add(new LifecycleReplayEvent(events.Count, 5, 0, "RuntimeReady", "Main", "preflight-main"));
        if (manualFront) events.Add(new QueueReplayEvent(events.Count, 5, 0, 1));
        events.Add(new CompletionReplayEvent(events.Count, 9, 4_000_000, claimed, completionCount));
        var replay = new ReplayDocument(
            ReplayDocument.SchemaIdentifier,
            1,
            manualFront ? "manual-front-v1" : "mismatched-front-v1",
            new ReplaySetup(4, resource, candidates),
            events);
        return ReplayJsonCodec.Parse(ReplayJsonCodec.Write(replay));
    }

    private static ReplaySetup SingleCandidateSetup(decimal initialResourceQuantity, bool candidateAvailable)
    {
        var resource = new ReplayResource(
            new ReplayIdentity("77777777-7777-4777-8777-777777777777", "ResourceSO"),
            initialResourceQuantity);
        return new ReplaySetup(
            3,
            resource,
            new[]
            {
                new ReplayCandidate(
                    new ReplayIdentity(CandidateA, "StructureSO"),
                    10,
                    1,
                    candidateAvailable,
                    1),
            });
    }

    private static List<ReplayEvent> LoadedGameplayEvents(string token)
    {
        return new List<ReplayEvent>
        {
            new LifecycleReplayEvent(0, 1, 0, "SceneEntered", "Main", token + "-main"),
            new LifecycleReplayEvent(1, 2, 0, "SaveLoadStarted", "Main", token + "-main"),
            new LifecycleReplayEvent(2, 3, 0, "SaveLoaded", "Main", token + "-main"),
            new LifecycleReplayEvent(3, 4, 0, "RegistryRebuilt", "Main", token + "-registry"),
            new LifecycleReplayEvent(4, 5, 0, "RuntimeReady", "Main", token + "-main"),
        };
    }

    private static ReplayDocument CanonicalReplay(
        string replayId,
        ReplaySetup setup,
        IReadOnlyList<ReplayEvent> events) =>
        ReplayJsonCodec.Parse(ReplayJsonCodec.Write(new ReplayDocument(
            ReplayDocument.SchemaIdentifier,
            1,
            replayId,
            setup,
            events)));

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "orb-replay-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
