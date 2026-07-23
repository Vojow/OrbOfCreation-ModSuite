#if SERVICE_CYCLE_PROFILE
using System.Globalization;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile.Format;
using OrbModding.ServiceCycleTrace.IO;

namespace OrbModding.ServiceCycleTrace.Performance;

internal sealed class ServiceCycleProfileSession
{
    internal ServiceCycleProfileSession(
        string directory,
        ServiceCycleProfileManifestDocument manifest,
        ServiceCycleProfileRecord[] records)
    {
        Directory = directory;
        Manifest = manifest;
        Records = records;
    }

    internal string Directory { get; }
    internal ServiceCycleProfileManifestDocument Manifest { get; }
    internal ServiceCycleProfileRecord[] Records { get; }
}

internal static class ServiceCycleProfileSessionReader
{
    internal static ServiceCycleProfileSession Read(string directory)
    {
        if (!System.IO.Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Profile session directory does not exist: {directory}");
        var manifest = ServiceCycleProfileManifestCodec.Decode(BoundedBinaryFile.Read(
            Path.Combine(directory, "manifest.ospm"),
            ServiceCycleProfileManifestCodec.ManifestBytes,
            ServiceCycleProfileManifestCodec.ManifestBytes));
        var paths = System.IO.Directory.GetFiles(directory, "segment-*.osps");
        Array.Sort(paths, StringComparer.Ordinal);
        if (checked((ulong)paths.Length) != manifest.SegmentCount)
            throw new FormatException("Profile segment count does not match the manifest.");

        var records = new List<ServiceCycleProfileRecord>(checked((int)manifest.WrittenRecords));
        ulong nextSequence = 1;
        for (var index = 0; index < paths.Length; index++)
        {
            var expectedName = "segment-" + index.ToString("D8", CultureInfo.InvariantCulture) + ".osps";
            if (!string.Equals(Path.GetFileName(paths[index]), expectedName, StringComparison.Ordinal))
                throw new FormatException("Profile segment ordinals are not dense.");
            var segment = ServiceCycleProfileSegmentCodec.Decode(BoundedBinaryFile.Read(
                paths[index],
                ServiceCycleProfileSegmentCodec.HeaderBytes +
                    ServiceCycleProfileRecordCodec.RecordBytes +
                    ServiceCycleProfileSegmentCodec.FooterBytes,
                ServiceCycleProfileSegmentCodec.GetEncodedLength(ServiceCycleProfileSegmentCodec.MaximumRecords)));
            var segmentCalibration = segment.Calibration;
            var manifestCalibration = manifest.Calibration;
            if (segment.Session != manifest.Session || segment.Ordinal != checked((ulong)index) ||
                segment.FirstRecordSequence != nextSequence ||
                !SameCalibration(in segmentCalibration, in manifestCalibration))
                throw new FormatException("Profile segment lineage does not match the manifest.");
            records.AddRange(segment.Records);
            nextSequence = checked(nextSequence + (ulong)segment.Records.Length);
        }
        if (checked((ulong)records.Count) != manifest.WrittenRecords)
            throw new FormatException("Profile record count does not match the manifest.");
        return new ServiceCycleProfileSession(directory, manifest, records.ToArray());
    }

    private static bool SameCalibration(
        in ServiceCycleProfileCalibration left,
        in ServiceCycleProfileCalibration right) =>
        left.TimestampFrequency == right.TimestampFrequency &&
        left.RawTimestamp == right.RawTimestamp &&
        left.MonotonicTimestampTicks == right.MonotonicTimestampTicks &&
        left.BuildId == right.BuildId &&
        left.TraceActive == right.TraceActive &&
        left.AllocationAvailable == right.AllocationAvailable;
}
#endif
