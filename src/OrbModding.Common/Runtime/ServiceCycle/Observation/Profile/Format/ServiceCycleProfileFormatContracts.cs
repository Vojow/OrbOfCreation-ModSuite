#if SERVICE_CYCLE_PROFILE
using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Profile.Format;

internal sealed class ServiceCycleProfileSegmentDocument
{
    internal ServiceCycleProfileSegmentDocument(
        ServiceCycleProfileSessionId session,
        ulong ordinal,
        ulong firstRecordSequence,
        in ServiceCycleProfileCalibration calibration,
        ServiceCycleProfileRecord[] records)
    {
        Session = session;
        Ordinal = ordinal;
        FirstRecordSequence = firstRecordSequence;
        Calibration = calibration;
        Records = records ?? throw new ArgumentNullException(nameof(records));
    }

    internal ServiceCycleProfileSessionId Session { get; }
    internal ulong Ordinal { get; }
    internal ulong FirstRecordSequence { get; }
    internal ServiceCycleProfileCalibration Calibration { get; }
    internal ServiceCycleProfileRecord[] Records { get; }
}

internal readonly struct ServiceCycleProfileManifestDocument
{
    internal ServiceCycleProfileManifestDocument(
        ServiceCycleProfileCompleteness completeness,
        ServiceCycleProfileTerminalReason reason,
        ServiceCycleProfileSessionId session,
        in ServiceCycleProfileCalibration calibration,
        ulong segmentCount,
        ulong acceptedRecords,
        ulong writtenRecords,
        ulong firstIncompleteSequence,
        ulong segmentBytes,
        long minimumStartedAtRawTicks,
        long maximumStartedAtRawTicks)
    {
        Completeness = completeness;
        Reason = reason;
        Session = session;
        Calibration = calibration;
        SegmentCount = segmentCount;
        AcceptedRecords = acceptedRecords;
        WrittenRecords = writtenRecords;
        FirstIncompleteSequence = firstIncompleteSequence;
        SegmentBytes = segmentBytes;
        MinimumStartedAtRawTicks = minimumStartedAtRawTicks;
        MaximumStartedAtRawTicks = maximumStartedAtRawTicks;
    }

    internal ServiceCycleProfileCompleteness Completeness { get; }
    internal ServiceCycleProfileTerminalReason Reason { get; }
    internal ServiceCycleProfileSessionId Session { get; }
    internal ServiceCycleProfileCalibration Calibration { get; }
    internal ulong SegmentCount { get; }
    internal ulong AcceptedRecords { get; }
    internal ulong WrittenRecords { get; }
    internal ulong FirstIncompleteSequence { get; }
    internal ulong SegmentBytes { get; }
    internal long MinimumStartedAtRawTicks { get; }
    internal long MaximumStartedAtRawTicks { get; }
}
#endif
