using System.Globalization;
using OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Format;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.ServiceCycleTrace.IO;

namespace OrbModding.ServiceCycleTrace.ManualTrace;

internal sealed class ManualFullTraceSessionDirectory : IFullTracePriorEventReader
{
    private const string ManifestName = "manifest.oscm";
    private readonly string _path;

    internal ManualFullTraceSessionDirectory(string path)
    {
        _path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!Directory.Exists(_path)) throw new DirectoryNotFoundException("The full-trace session directory does not exist.");
        Name = Path.GetFileName(Path.TrimEndingDirectorySeparator(_path));
        Session = ParseSessionName(Name);
    }

    internal string Name { get; }
    internal FullTraceSessionId Session { get; }

    internal FullTraceManifestDocument? ReadManifest()
    {
        var path = Path.Combine(_path, ManifestName);
        return File.Exists(path)
            ? FullTraceManifestCodec.Decode(BoundedBinaryFile.Read(
                path,
                FullTraceManifestCodec.ManifestBytes,
                FullTraceManifestCodec.ManifestBytes))
            : null;
    }

    internal ulong CountDenseSegments()
    {
        var count = 0UL;
        var minimum = ulong.MaxValue;
        var maximum = 0UL;
        foreach (var path in Directory.EnumerateFiles(_path, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(path);
            if (IsOwnedTemporarySegment(name) || IsOwnedTemporaryManifest(name)) continue;
            if (string.Equals(name, ManifestName, StringComparison.Ordinal)) continue;
            if (LooksLikeManifest(name))
                throw new InvalidDataException("The full-trace directory contains a noncanonical manifest name.");
            if (!LooksLikeSegment(name)) continue;
            var ordinal = ParseSegmentName(name);
            count = checked(count + 1);
            minimum = Math.Min(minimum, ordinal);
            maximum = Math.Max(maximum, ordinal);
        }

        if (count != 0 && (minimum != 0 || maximum != count - 1))
            throw new InvalidDataException("The full-trace segment set is not a dense prefix.");
        return count;
    }

    internal void EnsureSafeReportPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(fullPath);
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!string.Equals(parent, _path, comparison)) return;
        var name = Path.GetFileName(fullPath);
        if (LooksLikeSegment(name) || LooksLikeManifest(name))
            throw new InvalidOperationException("The report cannot overwrite full-trace session evidence.");
    }

    internal FullTraceSegmentDocument ReadSegment(ulong ordinal)
    {
        var bytes = BoundedBinaryFile.Read(
            SegmentPath(ordinal),
            FullTraceSegmentCodec.GetEncodedLength(1),
            FullTraceSegmentCodec.GetEncodedLength(FullTraceSegmentCodec.MaximumRecords));
        return FullTraceSegmentCodec.Decode(bytes);
    }

    public ServiceCycleSemanticEvent ReadEvent(ulong segmentOrdinal, int eventIndex)
    {
        if (eventIndex is < 0 or >= FullTraceSegmentCodec.MaximumRecords)
            throw new InvalidDataException("The full-trace parent index is invalid.");
        using var stream = new FileStream(SegmentPath(segmentOrdinal), FileMode.Open, FileAccess.Read, FileShare.Read);
        var offset = checked(FullTraceSegmentCodec.HeaderBytes +
            eventIndex * ServiceCycleSemanticEventV5Codec.RecordBytes);
        if (offset + ServiceCycleSemanticEventV5Codec.RecordBytes > stream.Length - FullTraceSegmentCodec.FooterBytes)
            throw new InvalidDataException("The full-trace parent record is absent.");
        stream.Position = offset;
        Span<byte> record = stackalloc byte[ServiceCycleSemanticEventV5Codec.RecordBytes];
        stream.ReadExactly(record);
        return ServiceCycleSemanticEventV5Codec.Read(record);
    }

    private string SegmentPath(ulong ordinal) => Path.Combine(
        _path,
        "segment-" + ordinal.ToString("D8", CultureInfo.InvariantCulture) + ".oscs");

    private static bool LooksLikeSegment(string name) =>
        name.StartsWith("segment-", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".oscs", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeManifest(string name) =>
        name.StartsWith("manifest", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".oscm", StringComparison.OrdinalIgnoreCase);

    private static bool IsOwnedTemporarySegment(string name)
    {
        const string marker = ".oscs.tmp-";
        var markerIndex = name.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0 || !HasOwnedTemporarySuffix(name, markerIndex + ".oscs".Length))
            return false;
        var committedName = name.Substring(0, markerIndex + ".oscs".Length);
        if (!TryParseSegmentName(committedName, out _)) return false;
        return true;
    }

    private static bool IsOwnedTemporaryManifest(string name) =>
        name.StartsWith(ManifestName, StringComparison.Ordinal) &&
        HasOwnedTemporarySuffix(name, ManifestName.Length);

    private static bool HasOwnedTemporarySuffix(string name, int committedLength)
    {
        const string marker = ".tmp-";
        if (name.Length != committedLength + marker.Length + 32 ||
            !name.AsSpan(committedLength, marker.Length).SequenceEqual(marker))
            return false;
        for (var index = committedLength + marker.Length; index < name.Length; index++)
            if (!IsLowerHex(name[index])) return false;
        return true;
    }

    private static bool IsLowerHex(char value) =>
        value is >= '0' and <= '9' or >= 'a' and <= 'f';

    private static ulong ParseSegmentName(string name)
    {
        if (!TryParseSegmentName(name, out var ordinal))
            throw new InvalidDataException("The full-trace directory contains a noncanonical segment name.");
        return ordinal;
    }

    private static bool TryParseSegmentName(string name, out ulong ordinal)
    {
        const string prefix = "segment-";
        const string suffix = ".oscs";
        ordinal = 0;
        if (!name.StartsWith(prefix, StringComparison.Ordinal) ||
            !name.EndsWith(suffix, StringComparison.Ordinal))
            return false;
        var value = name.Substring(prefix.Length, name.Length - prefix.Length - suffix.Length);
        return ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ordinal) &&
            string.Equals(
                name,
                prefix + ordinal.ToString("D8", CultureInfo.InvariantCulture) + suffix,
                StringComparison.Ordinal);
    }

    private static FullTraceSessionId ParseSessionName(string name)
    {
        const string prefix = "session-";
        if (name.Length != prefix.Length + 16 || !name.StartsWith(prefix, StringComparison.Ordinal) ||
            !ulong.TryParse(
                name.AsSpan(prefix.Length),
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out var value) ||
            value == 0 ||
            !string.Equals(
                name,
                prefix + value.ToString("x16", CultureInfo.InvariantCulture),
                StringComparison.Ordinal))
            throw new InvalidDataException("The full-trace session directory name is invalid.");
        return new FullTraceSessionId(value);
    }
}
