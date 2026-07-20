using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

public sealed class QueueCapacitySnapshotTests
{
    [Fact]
    public void UnequalCapacityAndAutomationLimit_UsesSmallerPolicyAllocation()
    {
        Assert.True(QueueCapacitySnapshot.TryCreate(20, 17, 5, 0, out var snapshot, out var reason));

        Assert.Equal(QueueCapacityInvalidReason.None, reason);
        Assert.Equal(20, snapshot.NativeCapacity);
        Assert.Equal(3, snapshot.LiveOccupancy);
        Assert.Equal(17, snapshot.NativeRemainingRoom);
        Assert.Equal(5, snapshot.AutomationUsageLimit);
        Assert.Equal(5, snapshot.UsableAutomationRoom);
    }

    [Fact]
    public void EqualNativeAndAutomationCapacity_PreservesAllUnreservedRoom()
    {
        Assert.True(QueueCapacitySnapshot.TryCreate(8, 6, 8, 0, out var snapshot, out var reason));

        Assert.Equal(QueueCapacityInvalidReason.None, reason);
        Assert.Equal(2, snapshot.LiveOccupancy);
        Assert.Equal(6, snapshot.UsableAutomationRoom);
    }

    [Fact]
    public void PartialOccupancyAndReservation_AppliesReservationExactlyOnce()
    {
        Assert.True(QueueCapacitySnapshot.TryCreate(12, 7, 20, 2, out var snapshot, out _));

        Assert.Equal(5, snapshot.LiveOccupancy);
        Assert.Equal(2, snapshot.ManualReservation);
        Assert.Equal(5, snapshot.UsableAutomationRoom);
    }

    [Fact]
    public void OneSlotQueue_WithManualReservation_LeavesNoAutomationRoom()
    {
        Assert.True(QueueCapacitySnapshot.TryCreate(1, 1, 10, 1, out var snapshot, out _));

        Assert.Equal(0, snapshot.LiveOccupancy);
        Assert.Equal(0, snapshot.UsableAutomationRoom);
    }

    [Fact]
    public void ValuesExposeNativePolicyAndDerivedProvenance()
    {
        Assert.True(QueueCapacitySnapshot.TryCreate(4, 3, 2, 1, out var snapshot, out _));

        Assert.Equal(QueueCapacityValueProvenance.AuthoritativeNativeContract, snapshot.NativeCapacityProvenance);
        Assert.Equal(QueueCapacityValueProvenance.DerivedFromNativeValues, snapshot.LiveOccupancyProvenance);
        Assert.Equal(QueueCapacityValueProvenance.AuthoritativeNativeContract, snapshot.NativeRemainingRoomProvenance);
        Assert.Equal(QueueCapacityValueProvenance.AutomationUsagePolicy, snapshot.AutomationUsageLimitProvenance);
        Assert.Equal(QueueCapacityValueProvenance.ManualReservationPolicy, snapshot.ManualReservationProvenance);
        Assert.Equal(QueueCapacityValueProvenance.DerivedFromNativeAndPolicyValues, snapshot.UsableAutomationRoomProvenance);
    }

    [Theory]
    [InlineData(-1, 0, 1, 0, QueueCapacityInvalidReason.NegativeNativeCapacity)]
    [InlineData(1, -1, 1, 0, QueueCapacityInvalidReason.NegativeNativeRemainingRoom)]
    [InlineData(1, 2, 1, 0, QueueCapacityInvalidReason.NativeRemainingRoomExceedsCapacity)]
    [InlineData(1, 1, -1, 0, QueueCapacityInvalidReason.NegativeAutomationUsageLimit)]
    [InlineData(1, 1, 1, -1, QueueCapacityInvalidReason.NegativeManualReservation)]
    public void ContradictoryOrInvalidInputs_FailClosed(
        int capacity,
        int remainingRoom,
        int automationLimit,
        int reservation,
        QueueCapacityInvalidReason expectedReason)
    {
        Assert.False(QueueCapacitySnapshot.TryCreate(
            capacity,
            remainingRoom,
            automationLimit,
            reservation,
            out var snapshot,
            out var reason));

        Assert.Equal(default, snapshot);
        Assert.Equal(expectedReason, reason);
    }
}
