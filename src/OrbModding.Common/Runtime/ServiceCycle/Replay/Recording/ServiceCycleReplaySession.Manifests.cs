using System;
using System.Threading;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;

public sealed partial class ServiceCycleReplaySession
{
    public bool TryReadCodecManifest(
        int traceServiceKey,
        out ServiceCycleReplayCodecManifest manifest)
    {
        var index = traceServiceKey - 1;
        if ((uint)index >= (uint)_codecManifests.Length ||
            Volatile.Read(ref _codecManifestBound[index]) == 0)
        {
            manifest = default;
            return false;
        }
        manifest = _codecManifests[index];
        return true;
    }

    public bool TryReadCodecManifestAt(
        int index,
        in ServiceCycleReplayCodecManifestFence fence,
        out ServiceCycleReplayCodecManifest manifest)
    {
        if (!fence.IsValid || fence.Count > _publishedCodecManifests.Length ||
            fence.Publication > Interlocked.Read(ref _codecManifestPublication) ||
            (uint)index >= (uint)fence.Count)
        {
            manifest = default;
            return false;
        }

        manifest = _publishedCodecManifests[index];
        return manifest.IsValid;
    }

    internal void BindCodecManifest(
        int traceServiceKey,
        object registrationOwner,
        ServiceCycleReplayCodecDescriptor cycleInput,
        ServiceCycleReplayCodecDescriptor state,
        ServiceCycleReplayCodecDescriptor action)
    {
        if (registrationOwner is null) throw new ArgumentNullException(nameof(registrationOwner));
        var index = traceServiceKey - 1;
        if ((uint)index >= (uint)_codecManifests.Length)
            throw new ArgumentOutOfRangeException(nameof(traceServiceKey));
        var candidate = new ServiceCycleReplayCodecManifest(
            traceServiceKey, cycleInput, state, action);
        if (!candidate.IsValid)
            throw new ArgumentException("A replay codec manifest requires three valid descriptors.");
        lock (_manifestGate)
        {
            if (_codecManifestBound[index] == 0)
            {
                if (_codecManifestCount == _publishedCodecManifests.Length)
                    throw new InvalidOperationException("The replay codec manifest capacity is exhausted.");
                BeginFenceWrite();
                _codecManifests[index] = candidate;
                _codecManifestOwners[index] = registrationOwner;
                _publishedCodecManifests[_codecManifestCount] = candidate;
                Volatile.Write(ref _codecManifestBound[index], 1);
                Volatile.Write(ref _codecManifestCount, checked(_codecManifestCount + 1));
                Interlocked.Increment(ref _codecManifestPublication);
                EndFenceWrite();
                return;
            }
            if (!ReferenceEquals(_codecManifestOwners[index], registrationOwner))
                throw new InvalidOperationException(
                    "A trace service ordinal is already bound to another replay registration.");
            if (_codecManifests[index] != candidate)
                throw new InvalidOperationException(
                    "Every physical replay worker must use the frozen codec manifest.");
        }
    }
}
