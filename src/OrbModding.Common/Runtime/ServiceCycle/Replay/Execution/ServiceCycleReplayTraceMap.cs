using System;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

/// <summary>Identity map for production-dense replay keys.</summary>
internal sealed class ServiceCycleReplayTraceMap
{
    private readonly int _count;

    internal ServiceCycleReplayTraceMap(IServiceCycleReplayProductionParticipant[] participants)
    {
        if (participants is null) throw new ArgumentNullException(nameof(participants));
        if (participants.Length == 0) throw new ArgumentException("Replay requires a participant.", nameof(participants));
        _count = participants.Length;
        for (var index = 0; index < participants.Length; index++)
        {
            var key = participants[index].TraceServiceKey;
            if (key != index + 1)
                throw new InvalidOperationException("Production replay participant keys must be dense and ordered.");
        }
    }

    internal int Count => _count;
    internal int ArtifactKeyForOrdinal(int ordinal) => checked(ordinal + 1);

    internal bool TryRuntimeKey(int artifactKey, out int runtimeKey)
    {
        runtimeKey = artifactKey;
        return artifactKey > 0 && artifactKey <= _count;
    }

    internal bool TryArtifactKey(int runtimeKey, out int artifactKey)
    {
        artifactKey = runtimeKey;
        return runtimeKey > 0 && runtimeKey <= _count;
    }

    internal ServiceCycleReplayCycleKey ToArtifact(in ServiceCycleReplayCycleKey runtime)
    {
        return runtime;
    }
}
