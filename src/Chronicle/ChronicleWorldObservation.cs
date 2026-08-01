using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbChronicle;

internal readonly struct ChronicleWorldObservation
{
    private ChronicleWorldObservation(
        bool available,
        string unavailableReason,
        ulong worldGeneration,
        long lifecycleGeneration,
        long observedAtTicks,
        ulong reachedMask,
        ulong blockedMask,
        bool worldRestored,
        PublicationTable<WorldResource> resources)
    {
        Available = available;
        UnavailableReason = unavailableReason;
        WorldGeneration = worldGeneration;
        LifecycleGeneration = lifecycleGeneration;
        ObservedAtTicks = observedAtTicks;
        ReachedMask = reachedMask;
        BlockedMask = blockedMask;
        WorldRestored = worldRestored;
        Resources = resources;
    }

    internal bool Available { get; }
    internal string UnavailableReason { get; }
    internal ulong WorldGeneration { get; }
    internal long LifecycleGeneration { get; }
    internal long ObservedAtTicks { get; }
    internal ulong ReachedMask { get; }
    internal ulong BlockedMask { get; }
    internal bool WorldRestored { get; }
    internal PublicationTable<WorldResource> Resources { get; }

    internal static ChronicleWorldObservation Create(
        ulong worldGeneration,
        long lifecycleGeneration,
        long observedAtTicks,
        ulong reachedMask,
        ulong blockedMask,
        bool worldRestored)
        => Create(
            worldGeneration,
            lifecycleGeneration,
            observedAtTicks,
            reachedMask,
            blockedMask,
            worldRestored,
            PublicationTable<WorldResource>.Empty);

    internal static ChronicleWorldObservation Create(
        ulong worldGeneration,
        long lifecycleGeneration,
        long observedAtTicks,
        ulong reachedMask,
        ulong blockedMask,
        bool worldRestored,
        PublicationTable<WorldResource> resources)
    {
        if (resources is null) throw new ArgumentNullException(nameof(resources));
        if (worldGeneration == 0) throw new ArgumentOutOfRangeException(nameof(worldGeneration));
        if (lifecycleGeneration <= 0)
            throw new ArgumentOutOfRangeException(nameof(lifecycleGeneration));
        if (observedAtTicks < 0) throw new ArgumentOutOfRangeException(nameof(observedAtTicks));
        if (((reachedMask | blockedMask) & ~ChronicleMilestones.NativeMask) != 0)
            throw new ArgumentOutOfRangeException(nameof(reachedMask));
        if ((reachedMask & blockedMask) != 0)
            throw new ArgumentException("Reached and blocked milestone masks must not overlap.");

        var finalReached =
            (reachedMask & ChronicleMilestones.At(ChronicleMilestones.WorldRestoredIndex).Mask) != 0;
        if (worldRestored != finalReached)
        {
            throw new ArgumentException(
                "The world-restored value must match the final milestone bit.",
                nameof(worldRestored));
        }

        return new ChronicleWorldObservation(
            true,
            string.Empty,
            worldGeneration,
            lifecycleGeneration,
            observedAtTicks,
            reachedMask,
            blockedMask,
            worldRestored,
            resources);
    }

    internal static ChronicleWorldObservation Unavailable(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("An unavailable reason is required.", nameof(reason));
        return new ChronicleWorldObservation(
            false,
            reason,
            0,
            0,
            0,
            0,
            0,
            false,
            PublicationTable<WorldResource>.Empty);
    }
}

internal static class ChronicleWorldObservationProjector
{
    private const string ViewsCategory = "views";
    private const string UpgradesCategory = "upgrades";
    private const string BoolVariablesCategory = "bool variables";
    private const string ResourcesCategory = "resources";

    internal static ChronicleWorldObservation Project(
        GameWorldState world,
        ulong worldGeneration,
        long lifecycleGeneration,
        long observedAtTicks)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (observedAtTicks < 0) throw new ArgumentOutOfRangeException(nameof(observedAtTicks));
        if (worldGeneration == 0 || lifecycleGeneration <= 0 || world.CollectedAtEpoch <= 0)
        {
            return ChronicleWorldObservation.Unavailable(
                "the shared world has not completed a lifecycle-valid collection");
        }
        if (world.CollectedAtEpoch != lifecycleGeneration)
        {
            return ChronicleWorldObservation.Unavailable(
                "the latest shared world belongs to lifecycle " + world.CollectedAtEpoch +
                ", not current lifecycle " + lifecycleGeneration);
        }
        if (!TryRequireCleanCategory(world, ViewsCategory, out var categoryFailure) ||
            !TryRequireCleanCategory(world, UpgradesCategory, out categoryFailure) ||
            !TryRequireCleanCategory(world, BoolVariablesCategory, out categoryFailure) ||
            !TryRequireCleanCategory(world, ResourcesCategory, out categoryFailure))
        {
            return ChronicleWorldObservation.Unavailable(categoryFailure);
        }

        ulong reached = 0;
        ulong blocked = 0;
        for (var index = 1; index <= 5; index++)
        {
            var definition = ChronicleMilestones.At(index);
            if (!WorldLookup.TryFind(world.Views, definition.TargetId, out var view))
            {
                blocked |= definition.Mask;
                continue;
            }
            if (view.Available) reached |= definition.Mask;
        }

        var restoration = ChronicleMilestones.At(6);
        if (!WorldLookup.TryFind(world.Upgrades, restoration.TargetId, out var upgrade))
            blocked |= restoration.Mask;
        else if (!upgrade.IsBounded || upgrade.Reading.MaxLevel != 1)
            blocked |= restoration.Mask;
        else if (upgrade.IsExhausted)
            reached |= restoration.Mask;

        var final = ChronicleMilestones.At(ChronicleMilestones.WorldRestoredIndex);
        var worldRestored = false;
        if (!WorldLookup.TryFind(world.BoolVariables, final.TargetId, out var completedWorld))
            blocked |= final.Mask;
        else if (!completedWorld.IsSaved || completedWorld.InitialValue)
            blocked |= final.Mask;
        else
        {
            worldRestored = completedWorld.Value;
            if (worldRestored) reached |= final.Mask;
        }

        return ChronicleWorldObservation.Create(
            worldGeneration,
            lifecycleGeneration,
            observedAtTicks,
            reached,
            blocked,
            worldRestored,
            world.Resources);
    }

    private static bool TryRequireCleanCategory(
        GameWorldState world,
        string category,
        out string failure)
    {
        for (var index = 0; index < world.CollectionCategories.Count; index++)
        {
            var status = world.CollectionCategories[index];
            if (!string.Equals(status.Category, category, StringComparison.Ordinal)) continue;
            if (status.IsClean)
            {
                failure = string.Empty;
                return true;
            }
            failure = "Chronicle requires a complete " + category +
                " collection; outcome was " + status.Outcome + " with " +
                status.Skipped + " skipped rows";
            return false;
        }

        failure = "Chronicle requires collection status for " + category;
        return false;
    }
}
