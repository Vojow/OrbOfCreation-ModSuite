using System;

namespace OrbAutomata;

internal readonly struct AutoAgromancyFactFingerprint : IEquatable<AutoAgromancyFactFingerprint>
{
    internal AutoAgromancyFactFingerprint(ulong value) => Value = value;
    internal ulong Value { get; }
    internal bool IsValid => Value != 0;
    public bool Equals(AutoAgromancyFactFingerprint other) => Value == other.Value;
    public override bool Equals(object? obj) =>
        obj is AutoAgromancyFactFingerprint other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
}

internal readonly struct AutoAgromancyCycleAction
{
    internal AutoAgromancyCycleAction(
        Guid actionId,
        Guid elementId,
        int observedLevel,
        int targetLevel,
        int maximumLevel,
        long collectedAtEpoch,
        AutoAgromancyFactFingerprint fingerprint)
    {
        if (actionId == Guid.Empty) throw new ArgumentException("An action identity is required.", nameof(actionId));
        if (elementId == Guid.Empty) throw new ArgumentException("An element identity is required.", nameof(elementId));
        if (observedLevel < 0) throw new ArgumentOutOfRangeException(nameof(observedLevel));
        if (targetLevel < 0 || targetLevel > maximumLevel)
            throw new ArgumentOutOfRangeException(nameof(targetLevel));
        if (collectedAtEpoch <= 0) throw new ArgumentOutOfRangeException(nameof(collectedAtEpoch));
        if (!fingerprint.IsValid) throw new ArgumentException("A valid fact fingerprint is required.", nameof(fingerprint));

        ActionId = actionId;
        ElementId = elementId;
        ObservedLevel = observedLevel;
        TargetLevel = targetLevel;
        MaximumLevel = maximumLevel;
        CollectedAtEpoch = collectedAtEpoch;
        Fingerprint = fingerprint;
    }

    internal Guid ActionId { get; }
    internal Guid ElementId { get; }
    internal string ExpectedActionType => "HarvestActionSO";
    internal string ExpectedElementType => "HarvestElementSO";
    internal int ObservedLevel { get; }
    internal int TargetLevel { get; }
    internal int MaximumLevel { get; }
    internal long CollectedAtEpoch { get; }
    internal AutoAgromancyFactFingerprint Fingerprint { get; }
}
