using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Format;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Status;
using OrbModding.Common.Runtime.Tracing;

namespace OrbAutomata;

internal readonly struct AutomataDecisionJournalSpec
{
    internal AutomataDecisionJournalSpec(
        IRestartAwareTraceSegmentStorage storage,
        DecisionJournalRunId run,
        int maximumCommittedSegments,
        int blockCount,
        MonotonicDuration checkpointInterval)
    {
        Storage = storage ?? throw new ArgumentNullException(nameof(storage));
        if (!run.IsValid) throw new ArgumentException("A valid journal run is required.", nameof(run));
        if (maximumCommittedSegments <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumCommittedSegments));
        if (blockCount < 3) throw new ArgumentOutOfRangeException(nameof(blockCount));
        if (checkpointInterval.Ticks <= 0)
            throw new ArgumentOutOfRangeException(nameof(checkpointInterval));
        Run = run;
        MaximumCommittedSegments = maximumCommittedSegments;
        BlockCount = blockCount;
        CheckpointInterval = checkpointInterval;
    }

    internal IRestartAwareTraceSegmentStorage Storage { get; }
    internal DecisionJournalRunId Run { get; }
    internal int MaximumCommittedSegments { get; }
    internal int BlockCount { get; }
    internal MonotonicDuration CheckpointInterval { get; }
}

internal interface IAutomataDecisionJournalSource
{
    AutomataDecisionJournalSpec Create();
}

internal readonly struct AutomataDecisionJournalOptions
{
    internal AutomataDecisionJournalOptions(
        DecisionJournalStatusRegistry status,
        IAutomataDecisionJournalSource source,
        string artifactName)
    {
        Status = status ?? throw new ArgumentNullException(nameof(status));
        Source = source ?? throw new ArgumentNullException(nameof(source));
        if (!DecisionJournalStatus.IsSafeArtifactName(artifactName))
            throw new ArgumentException("A bounded journal artifact basename is required.", nameof(artifactName));
        ArtifactName = artifactName;
    }

    internal bool Enabled => Status is not null && Source is not null;
    internal DecisionJournalStatusRegistry? Status { get; }
    internal IAutomataDecisionJournalSource? Source { get; }
    internal string? ArtifactName { get; }
}
