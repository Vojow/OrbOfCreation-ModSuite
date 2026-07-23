using System.Collections.Generic;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

/// <summary>Immutable service-to-codec projection for one already canonical manifest.</summary>
internal sealed class ServiceCycleReplayCodecIndex
{
    private readonly ServiceCycleReplayCodecManifestEntry[] _codecs;
    private readonly Dictionary<int, int> _serviceOffsets;

    private ServiceCycleReplayCodecIndex(
        ServiceCycleReplayCodecManifestEntry[] codecs,
        Dictionary<int, int> serviceOffsets)
    {
        _codecs = codecs;
        _serviceOffsets = serviceOffsets;
    }

    internal static ServiceCycleReplayCodecIndex Build(
        ServiceCycleReplayCodecManifestEntry[] codecs,
        ServiceCycleReplayFormatWorkCounter? work = null)
    {
        var offsets = new Dictionary<int, int>(codecs.Length / 3);
        for (var index = 0; index + 2 < codecs.Length; index += 3)
        {
            work?.Add();
            offsets.TryAdd(codecs[index].TraceServiceKey, index);
        }
        return new ServiceCycleReplayCodecIndex(codecs, offsets);
    }

    internal bool HasService(int service, ServiceCycleReplayFormatWorkCounter? work = null)
    {
        work?.Add();
        return _serviceOffsets.ContainsKey(service);
    }

    internal bool TryGetDescriptor(
        int service,
        ServiceCycleReplayRecordKind kind,
        out ServiceCycleReplayCodecDescriptor descriptor,
        ServiceCycleReplayFormatWorkCounter? work = null)
    {
        work?.Add();
        var offset = kind switch
        {
            ServiceCycleReplayRecordKind.CycleInput => 0,
            ServiceCycleReplayRecordKind.PreviousState or ServiceCycleReplayRecordKind.NextState => 1,
            ServiceCycleReplayRecordKind.Action => 2,
            _ => -1,
        };
        if (offset >= 0 && _serviceOffsets.TryGetValue(service, out var serviceOffset))
        {
            descriptor = _codecs[serviceOffset + offset].Descriptor;
            return true;
        }
        descriptor = default;
        return false;
    }

    internal bool TryGetDescriptor(
        int service,
        ServiceCycleReplayCodecRole role,
        out ServiceCycleReplayCodecDescriptor descriptor,
        ServiceCycleReplayFormatWorkCounter? work = null)
    {
        work?.Add();
        var offset = role switch
        {
            ServiceCycleReplayCodecRole.CycleInput => 0,
            ServiceCycleReplayCodecRole.State => 1,
            ServiceCycleReplayCodecRole.Action => 2,
            _ => -1,
        };
        if (offset >= 0 && _serviceOffsets.TryGetValue(service, out var serviceOffset))
        {
            descriptor = _codecs[serviceOffset + offset].Descriptor;
            return true;
        }
        descriptor = default;
        return false;
    }
}
