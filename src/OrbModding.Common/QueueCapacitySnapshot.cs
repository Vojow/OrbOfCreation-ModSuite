using System;

namespace OrbModding.Common;

/// <summary>
/// Identifies where each queue-capacity value comes from. Native values are
/// observed from the game, policy values are supplied by the automation
/// module, and derived values are calculated only by <see cref="QueueCapacitySnapshot"/>.
/// </summary>
public enum QueueCapacityValueProvenance
{
    AuthoritativeNativeContract,
    AutomationUsagePolicy,
    ManualReservationPolicy,
    DerivedFromNativeValues,
    DerivedFromNativeAndPolicyValues,
}

/// <summary>
/// Describes why contradictory queue-capacity inputs were rejected.
/// </summary>
public enum QueueCapacityInvalidReason
{
    None,
    NegativeNativeCapacity,
    NegativeNativeRemainingRoom,
    NativeRemainingRoomExceedsCapacity,
    NegativeAutomationUsageLimit,
    NegativeManualReservation,
}

/// <summary>
/// An immutable, fail-closed view of one native action queue and one
/// automation module's allocation policy.
/// </summary>
public readonly struct QueueCapacitySnapshot
{
    private QueueCapacitySnapshot(
        int nativeCapacity,
        int liveOccupancy,
        int nativeRemainingRoom,
        int automationUsageLimit,
        int manualReservation,
        int usableAutomationRoom)
    {
        NativeCapacity = nativeCapacity;
        LiveOccupancy = liveOccupancy;
        NativeRemainingRoom = nativeRemainingRoom;
        AutomationUsageLimit = automationUsageLimit;
        ManualReservation = manualReservation;
        UsableAutomationRoom = usableAutomationRoom;
    }

    public int NativeCapacity { get; }

    public int LiveOccupancy { get; }

    public int NativeRemainingRoom { get; }

    public int AutomationUsageLimit { get; }

    public int ManualReservation { get; }

    public int UsableAutomationRoom { get; }

    public QueueCapacityValueProvenance NativeCapacityProvenance =>
        QueueCapacityValueProvenance.AuthoritativeNativeContract;

    public QueueCapacityValueProvenance LiveOccupancyProvenance =>
        QueueCapacityValueProvenance.DerivedFromNativeValues;

    public QueueCapacityValueProvenance NativeRemainingRoomProvenance =>
        QueueCapacityValueProvenance.AuthoritativeNativeContract;

    public QueueCapacityValueProvenance AutomationUsageLimitProvenance =>
        QueueCapacityValueProvenance.AutomationUsagePolicy;

    public QueueCapacityValueProvenance ManualReservationProvenance =>
        QueueCapacityValueProvenance.ManualReservationPolicy;

    public QueueCapacityValueProvenance UsableAutomationRoomProvenance =>
        QueueCapacityValueProvenance.DerivedFromNativeAndPolicyValues;

    /// <summary>
    /// Creates a snapshot only when the authoritative native facts and policy
    /// inputs are internally consistent. Manual reservation is subtracted once
    /// from native remaining room; the automation usage limit is then applied
    /// once as an upper bound.
    /// </summary>
    public static bool TryCreate(
        int nativeCapacity,
        int nativeRemainingRoom,
        int automationUsageLimit,
        int manualReservation,
        out QueueCapacitySnapshot snapshot,
        out QueueCapacityInvalidReason invalidReason)
    {
        snapshot = default;
        if (nativeCapacity < 0)
        {
            invalidReason = QueueCapacityInvalidReason.NegativeNativeCapacity;
            return false;
        }

        if (nativeRemainingRoom < 0)
        {
            invalidReason = QueueCapacityInvalidReason.NegativeNativeRemainingRoom;
            return false;
        }

        if (nativeRemainingRoom > nativeCapacity)
        {
            invalidReason = QueueCapacityInvalidReason.NativeRemainingRoomExceedsCapacity;
            return false;
        }

        if (automationUsageLimit < 0)
        {
            invalidReason = QueueCapacityInvalidReason.NegativeAutomationUsageLimit;
            return false;
        }

        if (manualReservation < 0)
        {
            invalidReason = QueueCapacityInvalidReason.NegativeManualReservation;
            return false;
        }

        var liveOccupancy = nativeCapacity - nativeRemainingRoom;
        var roomAfterManualReservation = Math.Max(0, nativeRemainingRoom - manualReservation);
        var usableAutomationRoom = Math.Min(automationUsageLimit, roomAfterManualReservation);
        snapshot = new QueueCapacitySnapshot(
            nativeCapacity,
            liveOccupancy,
            nativeRemainingRoom,
            automationUsageLimit,
            manualReservation,
            usableAutomationRoom);
        invalidReason = QueueCapacityInvalidReason.None;
        return true;
    }
}
