using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

internal static class ServiceCycleReplayCodecCoverageValidator
{
    internal static void Validate(
        ServiceCycleReplayCodecManifestEntry[] codecs,
        ServiceCycleReplayArtifactRecord[] records,
        ServiceCycleReplayArtifactFooter[] footers)
    {
        if (codecs.Length % 3 != 0)
            throw ServiceCycleReplayBinary.Error(ServiceCycleReplayFormatErrorCode.CodecManifestCoverageInvalid);
        for (var index = 0; index < codecs.Length; index += 3)
        {
            if (codecs[index].Role != ServiceCycleReplayCodecRole.CycleInput ||
                codecs[index + 1].Role != ServiceCycleReplayCodecRole.State ||
                codecs[index + 2].Role != ServiceCycleReplayCodecRole.Action ||
                codecs[index].TraceServiceKey != codecs[index + 1].TraceServiceKey ||
                codecs[index].TraceServiceKey != codecs[index + 2].TraceServiceKey)
                throw ServiceCycleReplayBinary.Error(ServiceCycleReplayFormatErrorCode.CodecManifestCoverageInvalid, index);
        }
        Validate(ServiceCycleReplayCodecIndex.Build(codecs), records, footers);
    }

    internal static void Validate(
        ServiceCycleReplayCodecIndex codecIndex,
        ServiceCycleReplayArtifactRecord[] records,
        ServiceCycleReplayArtifactFooter[] footers,
        ServiceCycleReplayFormatWorkCounter? work = null)
    {
        for (var index = 0; index < records.Length; index++)
        {
            if (!codecIndex.TryGetDescriptor(records[index].Cycle.TraceServiceKey, records[index].Identity.Kind,
                    out var descriptor, work) ||
                records[index].SchemaVersion != descriptor.SchemaVersion ||
                records[index].PayloadView.Length > descriptor.MaximumEncodedBytes)
                throw ServiceCycleReplayBinary.Error(ServiceCycleReplayFormatErrorCode.RecordSchemaMismatch, index);
        }
        for (var index = 0; index < footers.Length; index++)
            if (!codecIndex.HasService(footers[index].Context.Cycle.TraceServiceKey, work))
                throw ServiceCycleReplayBinary.Error(ServiceCycleReplayFormatErrorCode.CodecManifestCoverageInvalid, index);
    }

}
