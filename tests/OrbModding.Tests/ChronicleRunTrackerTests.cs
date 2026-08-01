using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OrbChronicle;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests;

public sealed class ChronicleRunTrackerTests
{
    [Fact]
    public void StartMarksExistingProgressWithoutInventingTimes()
    {
        var tracker = new ChronicleRunTracker();
        tracker.Observe(Observation(reached: Bit(1) | Bit(2), restored: false));

        var outcome = tracker.Start();
        var snapshot = tracker.Snapshot;

        Assert.True(outcome.Accepted);
        Assert.Equal(ChronicleRunState.Running, snapshot.State);
        Assert.Equal(ChronicleMilestoneState.Reached, snapshot.Milestones[0].State);
        Assert.Equal(0, snapshot.Milestones[0].ElapsedSeconds);
        Assert.Equal(ChronicleMilestoneState.Preexisting, snapshot.Milestones[1].State);
        Assert.Null(snapshot.Milestones[1].ElapsedSeconds);
        Assert.Equal(ChronicleMilestoneState.Pending, snapshot.Milestones[3].State);
    }

    [Fact]
    public void DuplicateAndSimultaneousObservationsRecordEachSplitOnce()
    {
        var tracker = Started();
        tracker.Observe(Observation(
            reached: Bit(1) | Bit(2),
            restored: false,
            world: 3,
            observedAtTicks: Seconds(12.5)));
        tracker.Observe(Observation(
            reached: Bit(1) | Bit(2),
            restored: false,
            world: 4,
            observedAtTicks: Seconds(14)));

        var snapshot = tracker.Snapshot;
        Assert.Equal(12.5, snapshot.Milestones[1].ElapsedSeconds);
        Assert.Equal(12.5, snapshot.Milestones[2].ElapsedSeconds);
        Assert.Equal(14, snapshot.ElapsedSeconds);
    }

    [Fact]
    public void FeatureResourceKpisCaptureWhenEachResourceFirstBecomesVisible()
    {
        var mana = ChronicleResources.At(0).Resources[0];
        var tracker = Started(PublicationTable<WorldResource>.Create(new[]
        {
            Resource(mana.TargetId, 0, 100, 0, 0, visible: false),
        }));
        tracker.Observe(Observation(
            reached: 0,
            restored: false,
            world: 3,
            observedAtTicks: Seconds(8),
            resources: PublicationTable<WorldResource>.Create(new[]
            {
                Resource(mana.TargetId, quantity: 40, capacity: 100, trueQuantity: 60, trueRate: 5),
            })));

        var section = tracker.Snapshot.ResourceSections[0];
        var reading = section.Resources[0];
        Assert.Equal("first-visible", section.CaptureMode);
        Assert.Equal(1, section.CapturedCount);
        Assert.Equal(ChronicleResourceKpiState.Captured, reading.State);
        Assert.Equal(8, reading.ElapsedSeconds);
        Assert.True(reading.Visible);
        Assert.Equal(40, reading.Quantity!.Value.ToDouble());
        Assert.Equal(60, reading.TrueQuantity!.Value.ToDouble());
        Assert.Equal(5, reading.TrueRate!.Value.ToDouble());
        Assert.Equal(100, reading.Capacity!.Value.ToDouble());
        Assert.Equal(0.4, reading.FillFraction);

        tracker.Observe(Observation(
            reached: 0,
            restored: false,
            world: 4,
            observedAtTicks: Seconds(12),
            resources: PublicationTable<WorldResource>.Create(new[]
            {
                Resource(mana.TargetId, quantity: 90, capacity: 100, trueQuantity: 90, trueRate: 9),
            })));
        Assert.Equal(40, tracker.Snapshot.ResourceSections[0].Resources[0]
            .Quantity!.Value.ToDouble());
    }

    [Fact]
    public void PreexistingResourceDoesNotInventHistoricalDiscoveryOrKpis()
    {
        var tracker = new ChronicleRunTracker();
        var mana = ChronicleResources.At(0).Resources[0];
        tracker.Observe(Observation(
            reached: Bit(1),
            restored: false,
            resources: PublicationTable<WorldResource>.Create(new[]
            {
                Resource(mana.TargetId, 40, 100, 60, 5),
            })));

        Assert.True(tracker.Start().Accepted);
        var section = tracker.Snapshot.ResourceSections[0];
        Assert.Equal(1, section.PreexistingCount);
        Assert.Equal(ChronicleResourceKpiState.Preexisting, section.Resources[0].State);
        Assert.Null(section.Resources[0].ElapsedSeconds);
        Assert.Null(section.Resources[0].Quantity);
    }

    [Fact]
    public void LaterProgressCanDiscoverAResourceInAnEarlierFeatureSection()
    {
        var arcanum = Assert.Single(
            ChronicleResources.At(0).Resources,
            candidate => candidate.Id == "arcanum");
        var tracker = Started(PublicationTable<WorldResource>.Create(new[]
        {
            Resource(arcanum.TargetId, 0, 100, 0, 0, visible: false),
        }));

        tracker.Observe(Observation(
            reached: Bit(1) | Bit(2) | Bit(3) | Bit(4),
            restored: false,
            world: 3,
            observedAtTicks: Seconds(30),
            resources: PublicationTable<WorldResource>.Create(new[]
            {
                Resource(arcanum.TargetId, 25, 100, 25, 2),
            })));

        var captured = Assert.Single(
            tracker.Snapshot.ResourceSections[0].Resources,
            candidate => candidate.Id == "arcanum");
        Assert.Equal(ChronicleResourceKpiState.Captured, captured.State);
        Assert.Equal(30, captured.ElapsedSeconds);
        Assert.Equal(25, captured.Quantity!.Value.ToDouble());
    }

    [Fact]
    public void CrossFeatureResourceCapturesInEveryCuratedSubsection()
    {
        var worldOre = Assert.Single(
            ChronicleResources.At(2).Resources,
            candidate => candidate.Id == "ore");
        var tracker = Started(PublicationTable<WorldResource>.Create(new[]
        {
            Resource(worldOre.TargetId, 0, 100, 0, 0, visible: false),
        }));

        tracker.Observe(Observation(
            reached: Bit(1) | Bit(2) | Bit(3) | Bit(4) | Bit(5) | Bit(6),
            restored: false,
            world: 3,
            observedAtTicks: Seconds(90),
            resources: PublicationTable<WorldResource>.Create(new[]
            {
                Resource(worldOre.TargetId, 10, 100, 10, 1),
            })));

        var worldReading = Assert.Single(
            tracker.Snapshot.ResourceSections[2].Resources,
            candidate => candidate.Id == "ore");
        var restorationReading = Assert.Single(
            tracker.Snapshot.ResourceSections[6].Resources,
            candidate => candidate.Id == "ore");
        Assert.Equal(ChronicleResourceKpiState.Captured, worldReading.State);
        Assert.Equal(worldReading.ElapsedTicks, restorationReading.ElapsedTicks);
        Assert.Equal(
            worldReading.Quantity!.Value.ToDouble(),
            restorationReading.Quantity!.Value.ToDouble());
    }

    [Fact]
    public void TimeRuneLevelTransitionsCaptureObservedTimeLevelAndLevelWeightedMix()
    {
        var runeId = Guid.NewGuid();
        var tracker = Started(timeRunes: TimeRunes(
            Rune(runeId, "Magic Tempo", WorldTimeRuneArchetype.Tempo, level: 0)));

        tracker.Observe(Observation(
            reached: 0,
            restored: false,
            world: 3,
            observedAtTicks: Seconds(10),
            timeRunes: TimeRunes(
                Rune(runeId, "Magic Tempo", WorldTimeRuneArchetype.Tempo, level: 3, mastery: 4))));

        var snapshot = tracker.Snapshot;
        var item = Assert.Single(snapshot.RuneTimeline);
        Assert.Equal(10, item.ElapsedSeconds);
        Assert.Equal("Magic Tempo", item.Label);
        Assert.Equal(ChronicleRuneArchetype.Tempo, item.Archetype);
        Assert.Equal(0, item.LevelBefore);
        Assert.Equal(3, item.LevelAfter);
        Assert.Equal(3, item.LevelsGained);
        Assert.Equal(4, item.MasteryLevel);
        Assert.Equal(3, snapshot.RuneMix.TempoLevels);
        Assert.Equal(1d, snapshot.RuneMix.TempoRatio);
        Assert.Equal(0, snapshot.RuneMix.OtherLevels);
    }

    [Fact]
    public void AmbiguousCoreRuneTypeIsIsolatedAsOther()
    {
        var runeId = Guid.NewGuid();
        var types = WorldTimeRuneArchetype.Tempo | WorldTimeRuneArchetype.Scaling;
        var tracker = Started(timeRunes: TimeRunes(Rune(runeId, "Odd rune", types, 1)));

        tracker.Observe(Observation(
            reached: 0,
            restored: false,
            world: 3,
            observedAtTicks: Seconds(2),
            timeRunes: TimeRunes(Rune(runeId, "Odd rune", types, 2))));

        Assert.Equal(ChronicleRuneArchetype.Other, Assert.Single(tracker.Snapshot.RuneTimeline).Archetype);
        Assert.Equal(1, tracker.Snapshot.RuneMix.OtherLevels);
        Assert.Equal(0, tracker.Snapshot.RuneMix.CoreLevels);
    }

    [Fact]
    public void TimeRuneRegressionPausesWithoutAddingTheRegressedInterval()
    {
        var runeId = Guid.NewGuid();
        var tracker = Started(timeRunes: TimeRunes(
            Rune(runeId, "Magic Scaling", WorldTimeRuneArchetype.Scaling, 2)));
        tracker.Observe(Observation(
            reached: 0,
            restored: false,
            world: 3,
            observedAtTicks: Seconds(2),
            timeRunes: TimeRunes(
                Rune(runeId, "Magic Scaling", WorldTimeRuneArchetype.Scaling, 3))));
        tracker.Observe(Observation(
            reached: 0,
            restored: false,
            world: 4,
            observedAtTicks: Seconds(8),
            timeRunes: TimeRunes(
                Rune(runeId, "Magic Scaling", WorldTimeRuneArchetype.Scaling, 1))));

        Assert.Equal(ChronicleRunState.Paused, tracker.Snapshot.State);
        Assert.Equal(2, tracker.Snapshot.ElapsedSeconds);
        Assert.Contains("time-rune progression regressed", tracker.Snapshot.Reason);
    }

    [Fact]
    public void MissingCuratedResourceMarksOnlyThatKpiMissing()
    {
        var tracker = Started();

        Assert.Equal(
            ChronicleResourceKpiState.Missing,
            tracker.Snapshot.ResourceSections[0].Resources[0].State);
    }

    [Fact]
    public void RestorationFalseToTrueFinishesAndFreezesTheClock()
    {
        var tracker = Started();
        tracker.Observe(Observation(
            reached: 0,
            restored: false,
            world: 3,
            observedAtTicks: Seconds(4)));
        tracker.Observe(Observation(
            reached: Bit(7),
            restored: true,
            world: 4,
            observedAtTicks: Seconds(6)));
        tracker.Observe(Observation(
            reached: Bit(7),
            restored: true,
            world: 5,
            observedAtTicks: Seconds(16)));

        var snapshot = tracker.Snapshot;
        Assert.Equal(ChronicleRunState.Finished, snapshot.State);
        Assert.Equal(6, snapshot.ElapsedSeconds);
        Assert.Equal(6, snapshot.Milestones[7].ElapsedSeconds);
    }

    [Fact]
    public void AlreadyRestoredSaveDoesNotFinishAtStart()
    {
        var tracker = new ChronicleRunTracker();
        tracker.Observe(Observation(reached: Bit(7), restored: true));

        Assert.True(tracker.Start().Accepted);
        tracker.Observe(Observation(
            reached: Bit(7),
            restored: true,
            world: 3,
            observedAtTicks: Seconds(3)));

        Assert.Equal(ChronicleRunState.Running, tracker.Snapshot.State);
        Assert.Equal(ChronicleMilestoneState.Preexisting, tracker.Snapshot.Milestones[7].State);
    }

    [Fact]
    public void LifecycleReplacementPausesAndCannotResumeIntoAnotherWorld()
    {
        var tracker = Started();
        tracker.Observe(Observation(
            reached: 0,
            restored: false,
            world: 3,
            lifecycle: 2,
            observedAtTicks: Seconds(1)));

        var snapshot = tracker.Snapshot;
        Assert.Equal(ChronicleRunState.Paused, snapshot.State);
        Assert.Contains("lifecycle changed", snapshot.Reason);
        var resume = tracker.Resume();
        Assert.False(resume.Accepted);
        Assert.Equal("chronicle_lifecycle_changed", resume.Code);
    }

    [Fact]
    public void MissingPredicateBecomesBlockedWithoutStoppingOtherSplits()
    {
        var tracker = Started();
        tracker.Observe(
            ChronicleWorldObservation.Create(
                3,
                1,
                Seconds(5),
                Bit(2),
                Bit(1),
                false));

        var snapshot = tracker.Snapshot;
        Assert.Equal(ChronicleMilestoneState.Blocked, snapshot.Milestones[1].State);
        Assert.Equal(ChronicleMilestoneState.Reached, snapshot.Milestones[2].State);
        Assert.Equal(5, snapshot.Milestones[2].ElapsedSeconds);
    }

    [Fact]
    public void CommandsEnforceClosedStateTransitions()
    {
        var tracker = Started();
        Assert.False(tracker.Start().Accepted);
        Assert.True(tracker.Pause().Accepted);
        Assert.False(tracker.Pause().Accepted);
        Assert.True(tracker.Resume().Accepted);
        Assert.True(tracker.Abandon().Accepted);
        Assert.False(tracker.Resume().Accepted);
        Assert.False(tracker.Abandon().Accepted);
    }

    [Fact]
    public void ManualPauseAndResumeExcludeThePausedMonotonicInterval()
    {
        var tracker = Started();
        tracker.Observe(Observation(
            reached: 0,
            restored: false,
            world: 3,
            observedAtTicks: Seconds(2)));
        Assert.True(tracker.Pause().Accepted);
        tracker.Observe(Observation(
            reached: Bit(1),
            restored: false,
            world: 4,
            observedAtTicks: Seconds(20)));
        Assert.True(tracker.Resume().Accepted);
        tracker.Observe(Observation(
            reached: Bit(1),
            restored: false,
            world: 5,
            observedAtTicks: Seconds(23)));

        Assert.Equal(5, tracker.Snapshot.ElapsedSeconds);
        Assert.Equal(2, tracker.Snapshot.Milestones[1].ElapsedSeconds);
    }

    [Fact]
    public void NativeProgressRegressionPausesWithoutAddingTheRegressedInterval()
    {
        var tracker = Started();
        tracker.Observe(Observation(
            reached: Bit(1),
            restored: false,
            world: 3,
            observedAtTicks: Seconds(2)));
        tracker.Observe(Observation(
            reached: 0,
            restored: false,
            world: 4,
            observedAtTicks: Seconds(7)));

        Assert.Equal(ChronicleRunState.Paused, tracker.Snapshot.State);
        Assert.Equal(2, tracker.Snapshot.ElapsedSeconds);
        Assert.Contains("progression regressed", tracker.Snapshot.Reason);
        Assert.Equal("chronicle_progress_regressed", tracker.Resume().Code);
    }

    [Fact]
    public void BackwardMonotonicClockPausesFailClosed()
    {
        var tracker = Started();
        tracker.Observe(Observation(
            reached: 0,
            restored: false,
            world: 3,
            observedAtTicks: Seconds(2)));
        tracker.Observe(Observation(
            reached: 0,
            restored: false,
            world: 4,
            observedAtTicks: Seconds(1)));

        Assert.Equal(ChronicleRunState.Paused, tracker.Snapshot.State);
        Assert.Equal(2, tracker.Snapshot.ElapsedSeconds);
        Assert.Contains("clock moved backwards", tracker.Snapshot.Reason);
        Assert.Equal("chronicle_clock_regressed", tracker.Resume().Code);
    }

    [Fact]
    public void SnapshotMilestoneCollectionIsReadOnly()
    {
        var milestones = new ChronicleRunTracker().Snapshot.Milestones;

        Assert.IsAssignableFrom<System.Collections.Generic.IReadOnlyList<ChronicleMilestoneSnapshot>>(
            milestones);
        Assert.False(milestones is ChronicleMilestoneSnapshot[]);
        Assert.False(new ChronicleRunTracker().Snapshot.ResourceSections is
            ChronicleResourceSectionSnapshot[]);
    }

    [Fact]
    public void ResourceCatalogIsFeatureGroupedAndAllowsExplicitCrossFeatureLinks()
    {
        var sectionIds = new HashSet<string>(StringComparer.Ordinal);
        var count = 0;
        Assert.Equal(7, ChronicleResources.Count);
        for (var sectionIndex = 0; sectionIndex < ChronicleResources.Count; sectionIndex++)
        {
            var section = ChronicleResources.At(sectionIndex);
            Assert.True(sectionIds.Add(section.Id));
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var targets = new HashSet<Guid>();
            for (var index = 0; index < section.Resources.Count; index++)
            {
                Assert.True(ids.Add(section.Resources[index].Id));
                Assert.True(targets.Add(section.Resources[index].TargetId));
                count++;
            }
        }
        Assert.Equal(44, count);
        Assert.Equal("spell-output", ChronicleResources.At(0).Relationship);
        Assert.Equal("agromancy-output", ChronicleResources.At(2).Relationship);
        Assert.Equal("restoration-input", ChronicleResources.At(6).Relationship);
        Assert.Contains(ChronicleResources.At(0).Resources, candidate =>
            candidate.Id == "arcanum");
        Assert.Contains(ChronicleResources.At(2).Resources, candidate =>
            candidate.Id == "ore");
        Assert.Contains(ChronicleResources.At(6).Resources, candidate =>
            candidate.Id == "ore");
    }

    [Fact]
    public void FinishedRunIsAtomicallyArchivedAndReloadedForPersonalBestComparison()
    {
        var directory = Path.Combine(Path.GetTempPath(), "orb-chronicle-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "history.json");
        try
        {
            var runeId = Guid.NewGuid();
            var tracker = Started(timeRunes: TimeRunes(
                Rune(runeId, "Capacity Investment", WorldTimeRuneArchetype.Investment, 0)));
            tracker.Observe(Observation(
                reached: ChronicleMilestones.NativeMask,
                restored: true,
                world: 3,
                observedAtTicks: Seconds(42),
                timeRunes: TimeRunes(
                    Rune(runeId, "Capacity Investment", WorldTimeRuneArchetype.Investment, 5))));
            Assert.Equal(ChronicleRunState.Finished, tracker.Snapshot.State);

            var history = new ChronicleHistory(path, _ => { });
            history.Observe(tracker.Snapshot);
            Assert.True(File.Exists(path));
            Assert.False(File.Exists(path + ".tmp"));

            var reloaded = new ChronicleHistory(path, _ => { }).Project(tracker.Snapshot);
            var run = Assert.Single(reloaded.Runs);
            Assert.Equal(tracker.Snapshot.RunId, run.RunId);
            Assert.Equal(42, run.ElapsedSeconds);
            Assert.Equal(5, run.RuneMix.InvestmentLevels);
            Assert.Single(run.RuneTimeline);
            Assert.Same(run, reloaded.Comparison);
            Assert.Same(run, reloaded.PersonalBest);
            Assert.Equal(ChronicleComparisonMode.PersonalBest, reloaded.ComparisonMode);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void InvalidSidecarIsPreservedAndBlocksWrites()
    {
        var directory = Path.Combine(Path.GetTempPath(), "orb-chronicle-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "history.json");
        Directory.CreateDirectory(directory);
        File.WriteAllText(path, "{ definitely-not-json }");
        try
        {
            var warnings = new List<string>();
            var history = new ChronicleHistory(path, warnings.Add);
            var tracker = Started();
            history.Observe(tracker.Snapshot);

            Assert.Equal("{ definitely-not-json }", File.ReadAllText(path));
            Assert.Single(warnings);
            Assert.Contains("read-only", history.Project(tracker.Snapshot).Status);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void LegacySchemaOneRunRemainsReadableWithoutInventedRuneHistory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "orb-chronicle-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "history.json");
        Directory.CreateDirectory(directory);
        var current = Started().Snapshot;
        var root = new JObject
        {
            ["schemaVersion"] = 1,
            ["comparisonMode"] = "PersonalBest",
            ["selectedRunId"] = string.Empty,
            ["active"] = new JObject { ["state"] = "Dormant" },
            ["runs"] = new JArray(new JObject
            {
                ["runId"] = Guid.NewGuid().ToString("D"),
                ["completedAtUtcTicks"] = DateTime.UtcNow.Ticks,
                ["elapsedTicks"] = Seconds(30),
                ["milestoneSchemaId"] = current.MilestoneSchemaId,
                ["resourceSchemaId"] = current.ResourceSchemaId,
                ["clockId"] = current.ClockId,
                ["milestones"] = new JArray(),
                ["resources"] = new JArray(),
            }),
        };
        File.WriteAllText(path, root.ToString(Formatting.Indented));
        try
        {
            var projected = new ChronicleHistory(path, _ => { }).Project(current);
            var run = Assert.Single(projected.Runs);
            Assert.Same(run, projected.PersonalBest);
            Assert.Empty(run.RuneTimeline);
            Assert.Equal(string.Empty, run.RuneSchemaId);
            Assert.False(run.IsRuneCompatible(current));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ChronicleRunTracker Started(
        PublicationTable<WorldResource>? resources = null,
        PublicationTable<WorldTimeRune>? timeRunes = null)
    {
        var tracker = new ChronicleRunTracker();
        tracker.Observe(Observation(
            reached: 0,
            restored: false,
            resources: resources,
            timeRunes: timeRunes));
        Assert.True(tracker.Start().Accepted);
        return tracker;
    }

    private static ChronicleWorldObservation Observation(
        ulong reached,
        bool restored,
        ulong world = 2,
        long lifecycle = 1,
        long observedAtTicks = 0,
        PublicationTable<WorldResource>? resources = null,
        PublicationTable<WorldTimeRune>? timeRunes = null) =>
        ChronicleWorldObservation.Create(
            world,
            lifecycle,
            observedAtTicks,
            reached,
            0,
            restored,
            resources ?? PublicationTable<WorldResource>.Empty,
            timeRunes ?? PublicationTable<WorldTimeRune>.Empty);

    private static PublicationTable<WorldTimeRune> TimeRunes(params WorldTimeRune[] runes)
    {
        Array.Sort(runes, static (left, right) => left.EntityId.CompareTo(right.EntityId));
        return PublicationTable<WorldTimeRune>.Create(runes);
    }

    private static WorldTimeRune Rune(
        Guid id,
        string label,
        WorldTimeRuneArchetype archetypes,
        int level,
        int mastery = 0) =>
        new(
            id,
            label,
            archetypes,
            discovered: level > 0,
            level,
            discRarityLevel: 0,
            masteryXp: default,
            masteryLevel: mastery,
            isDiscoverRequired: false,
            seen: level > 0,
            freeUsages: default,
            power: default,
            powerScalingMod: default,
            masteryXpMod: default);

    private static WorldResource Resource(
        Guid id,
        double quantity,
        double capacity,
        double trueQuantity,
        double trueRate,
        bool visible = true)
    {
        var rateInputs = default(RawResourceRateInputs);
        var traits = default(RawResourceTraits);
        var modifiers = default(RawResourceModifiers);
        var reading = new RawResourceSample(
            id,
            new BigDouble(quantity),
            new BigDouble(capacity),
            new BigDouble(trueRate),
            visible,
            lifetimeQuantity: default,
            discoveryTime: default,
            quality: new BigDouble(100),
            gainRate: new BigDouble(100),
            drain: default,
            reservation: default,
            usage: default,
            inLossMode: false,
            inRestMode: false,
            inRallyMode: false,
            appliedLevels: 0,
            levelVariableId: Guid.Empty,
            in rateInputs,
            in traits,
            in modifiers);
        return new WorldResource(
            in reading,
            isCapped: true,
            headroom: new BigDouble(capacity - quantity),
            fillFraction: quantity / capacity,
            isAtCapacity: quantity >= capacity,
            trueQuantity: new BigDouble(trueQuantity),
            trueRate: new BigDouble(trueRate));
    }

    private static ulong Bit(int index) => 1UL << index;
    private static long Seconds(double value) => checked((long)(value * TimeSpan.TicksPerSecond));
}

public sealed class ChronicleWorldObservationProjectorTests
{
    [Fact]
    public void ProjectsExactViewsRestorationUpgradeAndSavedCompletionFlag()
    {
        var views = new WorldView[5];
        for (var index = 0; index < views.Length; index++)
        {
            var definition = ChronicleMilestones.All[index + 1];
            views[index] = new WorldView(
                definition.TargetId,
                active: false,
                alwaysActive: false,
                available: index < 3);
        }
        Array.Sort(views, static (left, right) => left.EntityId.CompareTo(right.EntityId));

        var restorationDefinition = ChronicleMilestones.All[6];
        var restorationReading = new RawUpgradeSample(
            restorationDefinition.TargetId,
            level: 1,
            maxLevel: 1,
            available: false,
            queuedLevels: 0,
            buildTime: default,
            developmentTime: 5,
            cachedCostLevel: 1);
        var restoration = new WorldUpgrade(
            in restorationReading,
            isBounded: true,
            isExhausted: true,
            remainingLevels: 0,
            committedLevel: 1,
            isDeveloping: false,
            developmentProgress: 0);
        var completionDefinition = ChronicleMilestones.All[7];
        var completion = new WorldBoolVariable(
            completionDefinition.TargetId,
            value: true,
            initialValue: false,
            isSaved: true,
            observerId: 1);
        var world = new GameWorldState
        {
            CollectedAtEpoch = 9,
            CollectionCategories = CleanCategories(),
            Views = PublicationTable<WorldView>.Create(views),
            Upgrades = PublicationTable<WorldUpgrade>.Create(new[] { restoration }),
            BoolVariables = PublicationTable<WorldBoolVariable>.Create(new[] { completion }),
        };

        var observation = ChronicleWorldObservationProjector.Project(
            world,
            42,
            9,
            observedAtTicks: 123);

        Assert.True(observation.Available);
        Assert.Equal((ulong)42, observation.WorldGeneration);
        Assert.Equal(123, observation.ObservedAtTicks);
        Assert.Equal(Bit(1) | Bit(2) | Bit(3) | Bit(6) | Bit(7), observation.ReachedMask);
        Assert.Equal((ulong)0, observation.BlockedMask);
        Assert.True(observation.WorldRestored);
    }

    [Fact]
    public void LifecycleMismatchIsUnavailableBeforeAnyPredicateIsTrusted()
    {
        var observation = ChronicleWorldObservationProjector.Project(
            new GameWorldState { CollectedAtEpoch = 8 },
            42,
            9,
            observedAtTicks: 123);

        Assert.False(observation.Available);
        Assert.Contains("not current lifecycle", observation.UnavailableReason);
    }

    [Fact]
    public void IncompleteRequiredCategoryMakesTheWholeObservationUnavailable()
    {
        var world = new GameWorldState
        {
            CollectedAtEpoch = 9,
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                Status("views", WorldCategoryOutcome.Collected, sampled: 5, skipped: 1),
                Status("upgrades", WorldCategoryOutcome.Collected, sampled: 1, skipped: 0),
                Status("bool variables", WorldCategoryOutcome.Collected, sampled: 1, skipped: 0),
            }),
        };

        var observation = ChronicleWorldObservationProjector.Project(world, 42, 9, 123);

        Assert.False(observation.Available);
        Assert.Contains("complete views collection", observation.UnavailableReason);
    }

    [Fact]
    public void IncompleteResourceCollectionCannotProducePartialKpiCheckpoints()
    {
        var world = new GameWorldState
        {
            CollectedAtEpoch = 9,
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                Status("views", WorldCategoryOutcome.Collected, sampled: 5, skipped: 0),
                Status("upgrades", WorldCategoryOutcome.Collected, sampled: 1, skipped: 0),
                Status("bool variables", WorldCategoryOutcome.Collected, sampled: 1, skipped: 0),
                Status("resources", WorldCategoryOutcome.Collected, sampled: 79, skipped: 1),
            }),
        };

        var observation = ChronicleWorldObservationProjector.Project(world, 42, 9, 123);

        Assert.False(observation.Available);
        Assert.Contains("complete resources collection", observation.UnavailableReason);
    }

    [Fact]
    public void MissingTargetInCleanCategoryIsExplicitlyBlocked()
    {
        var world = new GameWorldState
        {
            CollectedAtEpoch = 9,
            CollectionCategories = CleanCategories(),
        };

        var observation = ChronicleWorldObservationProjector.Project(world, 42, 9, 123);

        Assert.True(observation.Available);
        Assert.Equal(ChronicleMilestones.NativeMask, observation.BlockedMask);
    }

    [Fact]
    public void ObservationRejectsContradictoryMasks()
    {
        Assert.Throws<ArgumentException>(() => ChronicleWorldObservation.Create(
            1,
            1,
            0,
            Bit(1),
            Bit(1),
            false));
        Assert.Throws<ArgumentException>(() => ChronicleWorldObservation.Create(
            1,
            1,
            0,
            reachedMask: 0,
            blockedMask: 0,
            worldRestored: true));
    }

    [Fact]
    public void CompletionFlagMustRetainItsSavedFalseInitialContract()
    {
        var final = ChronicleMilestones.At(ChronicleMilestones.WorldRestoredIndex);
        var world = new GameWorldState
        {
            CollectedAtEpoch = 9,
            CollectionCategories = CleanCategories(),
            BoolVariables = PublicationTable<WorldBoolVariable>.Create(new[]
            {
                new WorldBoolVariable(
                    final.TargetId,
                    value: true,
                    initialValue: true,
                    isSaved: false,
                    observerId: 1),
            }),
        };

        var observation = ChronicleWorldObservationProjector.Project(world, 42, 9, 123);

        Assert.True(observation.Available);
        Assert.False(observation.WorldRestored);
        Assert.NotEqual((ulong)0, observation.BlockedMask & final.Mask);
    }

    [Fact]
    public void RestorationUpgradeMustRemainOneShotAndBounded()
    {
        var definition = ChronicleMilestones.At(6);
        var reading = new RawUpgradeSample(
            definition.TargetId,
            level: 2,
            maxLevel: 2,
            available: false,
            queuedLevels: 0,
            buildTime: default,
            developmentTime: 5,
            cachedCostLevel: 2);
        var malformed = new WorldUpgrade(
            in reading,
            isBounded: true,
            isExhausted: true,
            remainingLevels: 0,
            committedLevel: 2,
            isDeveloping: false,
            developmentProgress: 0);
        var world = new GameWorldState
        {
            CollectedAtEpoch = 9,
            CollectionCategories = CleanCategories(),
            Upgrades = PublicationTable<WorldUpgrade>.Create(new[] { malformed }),
        };

        var observation = ChronicleWorldObservationProjector.Project(world, 42, 9, 123);

        Assert.Equal((ulong)0, observation.ReachedMask & definition.Mask);
        Assert.NotEqual((ulong)0, observation.BlockedMask & definition.Mask);
    }

    private static PublicationTable<WorldCollectionCategoryStatus> CleanCategories() =>
        PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
        {
            Status("views", WorldCategoryOutcome.Collected, sampled: 5, skipped: 0),
            Status("upgrades", WorldCategoryOutcome.Collected, sampled: 1, skipped: 0),
            Status("bool variables", WorldCategoryOutcome.Collected, sampled: 1, skipped: 0),
            Status("resources", WorldCategoryOutcome.Collected, sampled: 80, skipped: 0),
            Status("time runes", WorldCategoryOutcome.Collected, sampled: 62, skipped: 0),
        });

    private static WorldCollectionCategoryStatus Status(
        string category,
        WorldCategoryOutcome outcome,
        int sampled,
        int skipped) =>
        new(category, outcome, sampled, skipped, skipped == 0 ? string.Empty : "test skip");

    private static ulong Bit(int index) => 1UL << index;
}
