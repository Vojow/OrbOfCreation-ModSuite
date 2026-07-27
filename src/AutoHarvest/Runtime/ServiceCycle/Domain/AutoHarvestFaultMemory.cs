using System;

namespace OrbAutomata;

/// <summary>How a past action of this service failed, in the terms the health report uses.</summary>
internal enum AutoHarvestFaultKind
{
    None = 0,

    /// <summary>The build's authored content could not be bound or audited.</summary>
    ContractUnavailable = 1,

    /// <summary>A mutation was attempted and the game did not do what it was asked.</summary>
    Faulted = 2,
}

/// <summary>
/// What this service has learned about its own failures, and how far each one reaches.
/// </summary>
/// <remarks>
/// <para>
/// It lives in the cycle state because it is the worker's memory rather than a reading of anything.
/// It used to live in two main-thread objects the capture pass consulted — a quarantine flag set
/// after an unverified mutation and a circuit tripped by a failed binding — which meant deciding
/// depended on state only the action boundary could write. See W45.
/// </para>
/// <para>
/// The state is created per lifecycle, so the memory clears when the game reloads without anything
/// having to notice: the quarantine's <c>ObserveLifecycle</c> reset exists for the copy the action
/// boundary keeps, which is a different object with a different job.
/// </para>
/// </remarks>
internal readonly struct AutoHarvestFaultMemory
{
    internal AutoHarvestFaultMemory(
        AutoHarvestFaultKind feature,
        AutoHarvestFaultKind fruit,
        AutoHarvestFaultKind treasure)
    {
        Feature = Checked(feature, nameof(feature));
        Fruit = Checked(fruit, nameof(fruit));
        Treasure = Checked(treasure, nameof(treasure));
    }

    /// <summary>A failure that reaches every pair, whichever one provoked it.</summary>
    internal AutoHarvestFaultKind Feature { get; }

    internal AutoHarvestFaultKind Fruit { get; }
    internal AutoHarvestFaultKind Treasure { get; }

    internal bool HasFeatureFault => Feature != AutoHarvestFaultKind.None;

    /// <summary>
    /// What stands in the way of one pair. A feature-wide failure answers for both pairs, because a
    /// pair with no failure of its own is still not one this service can act on.
    /// </summary>
    internal AutoHarvestFaultKind For(AutoHarvestPair pair) =>
        HasFeatureFault
            ? Feature
            : pair switch
            {
                AutoHarvestPair.FruitTree => Fruit,
                AutoHarvestPair.TreasureTree => Treasure,
                _ => throw new ArgumentOutOfRangeException(nameof(pair)),
            };

    internal AutoHarvestFaultMemory With(AutoHarvestPair pair, AutoHarvestFaultKind kind) => pair switch
    {
        AutoHarvestPair.FruitTree => new AutoHarvestFaultMemory(Feature, kind, Treasure),
        AutoHarvestPair.TreasureTree => new AutoHarvestFaultMemory(Feature, Fruit, kind),
        _ => throw new ArgumentOutOfRangeException(nameof(pair)),
    };

    internal AutoHarvestFaultMemory WithFeature(AutoHarvestFaultKind kind) =>
        new(kind, Fruit, Treasure);

    private static AutoHarvestFaultKind Checked(AutoHarvestFaultKind kind, string name) =>
        kind is AutoHarvestFaultKind.None or AutoHarvestFaultKind.ContractUnavailable or
            AutoHarvestFaultKind.Faulted
            ? kind
            : throw new ArgumentOutOfRangeException(name);
}
