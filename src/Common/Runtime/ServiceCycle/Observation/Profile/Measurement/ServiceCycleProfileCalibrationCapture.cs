#if SERVICE_CYCLE_PROFILE
using System;
using OrbModding.Common.Runtime;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;

internal readonly struct ServiceCycleProfileCalibrationPoint
{
    private readonly IServiceCycleProfileRawClock? _rawClock;
    private readonly ServiceCycleProfileAllocationCapability _allocationCapability;

    private ServiceCycleProfileCalibrationPoint(
        IServiceCycleProfileRawClock rawClock,
        in ServiceCycleProfileAllocationCapability allocationCapability,
        in ServiceCycleProfileCalibration calibration,
        int ownerThreadId)
    {
        _rawClock = rawClock ?? throw new ArgumentNullException(nameof(rawClock));
        _allocationCapability = allocationCapability;
        if (!calibration.IsValid) throw new ArgumentException("A valid calibration is required.", nameof(calibration));
        if (ownerThreadId <= 0) throw new ArgumentOutOfRangeException(nameof(ownerThreadId));
        Calibration = calibration;
        OwnerThreadId = ownerThreadId;
    }

    internal ServiceCycleProfileCalibration Calibration { get; }
    internal int OwnerThreadId { get; }
    internal IServiceCycleProfileRawClock RawClock =>
        _rawClock ?? throw new InvalidOperationException("The calibration point is invalid.");
    internal ServiceCycleProfileAllocationCapability AllocationCapability => _allocationCapability;
    internal bool IsValid => _rawClock is not null && _allocationCapability.IsValid &&
        _allocationCapability.OwnerThreadId == OwnerThreadId && Calibration.IsValid &&
        Calibration.AllocationAvailable == _allocationCapability.IsAvailable && OwnerThreadId > 0;

    internal static ServiceCycleProfileCalibrationPoint Capture(
        IServiceCycleProfileRawClock rawClock,
        IMonotonicClock monotonicClock,
        Guid buildId,
        bool traceActive,
        in ServiceCycleProfileAllocationCapability allocationCapability)
    {
        if (rawClock is null) throw new ArgumentNullException(nameof(rawClock));
        if (monotonicClock is null) throw new ArgumentNullException(nameof(monotonicClock));
        if (!allocationCapability.IsValid)
            throw new ArgumentException("A probed allocation capability is required.", nameof(allocationCapability));
        if (allocationCapability.OwnerThreadId != Environment.CurrentManagedThreadId)
            throw new ArgumentException("Calibration must use the allocation capability's owner thread.", nameof(allocationCapability));
        if (rawClock.Frequency <= 0)
            throw new InvalidOperationException("The profile raw clock frequency must be positive.");

        var rawBefore = rawClock.ReadTimestamp();
        var monotonicTicks = monotonicClock.Now.Ticks;
        var rawAfter = rawClock.ReadTimestamp();
        if (rawAfter < rawBefore)
            throw new InvalidOperationException("The profile raw clock moved backwards during calibration.");
        var midpoint = checked(rawBefore + checked(rawAfter - rawBefore) / 2);
        var calibration = new ServiceCycleProfileCalibration(
            rawClock.Frequency,
            midpoint,
            monotonicTicks,
            buildId,
            traceActive,
            allocationCapability.IsAvailable);
        return new ServiceCycleProfileCalibrationPoint(
            rawClock,
            in allocationCapability,
            in calibration,
            Environment.CurrentManagedThreadId);
    }
}
#endif
