using OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Format;

namespace OrbModding.ServiceCycleTrace.ManualTrace;

internal static class ManualFullTraceSessionReader
{
    internal static ManualFullTraceSession Read(string path)
    {
        var directory = new ManualFullTraceSessionDirectory(path);
        var segmentCount = directory.CountDenseSegments();
        var manifest = directory.ReadManifest();
        var assembler = new FullTraceSessionAssembler(directory.Session, directory);
        for (var ordinal = 0UL; ordinal < segmentCount; ordinal++)
        {
            var segment = directory.ReadSegment(ordinal);
            assembler.Add(in segment, FullTraceSegmentCodec.GetEncodedLength(segment.Events.Length));
        }
        var finalSegmentCount = directory.CountDenseSegments();
        var finalManifest = directory.ReadManifest();
        if (finalSegmentCount != segmentCount || !Nullable.Equals(manifest, finalManifest))
            throw new InvalidDataException("The full-trace session changed while it was being read.");
        var document = assembler.Complete(manifest);
        return new ManualFullTraceSession(directory, in document);
    }
}
