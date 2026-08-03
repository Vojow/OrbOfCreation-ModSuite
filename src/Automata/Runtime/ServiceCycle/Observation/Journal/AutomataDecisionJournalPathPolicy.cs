using System;
using System.Threading;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Format;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Status;
using OrbModding.Common.Runtime.Tracing;

namespace OrbAutomata;

internal sealed class AutomataDecisionJournalPathPolicy : IAutomataDecisionJournalSource
{
    internal const int LiveCandidateBlockCount = 10;

    /// <summary>The journal's fixed 64 MiB on-disk envelope.</summary>
    /// <remarks>
    /// Every committed segment is at most 80 header + 128 x 80 records + 40 footer = 10,360 bytes.
    /// The retained count leaves room for one maximum-sized temporary segment while its atomic
    /// commit and oldest-first eviction complete, so even the write transition stays inside the
    /// envelope.
    /// </remarks>
    internal const long LiveCandidateMaximumBytes = 64L * 1024 * 1024;
    internal static readonly int LiveCandidateMaximumCommittedSegments = checked(
        (int)(LiveCandidateMaximumBytes / DecisionJournalSegmentCodec.GetEncodedLength(
            DecisionJournalSegmentCodec.MaximumRecords)) - 1);
    internal const string ArtifactName = "journal";
    private static readonly MonotonicDuration CheckpointInterval =
        MonotonicDuration.FromTimeSpan(TimeSpan.FromMinutes(1));
    private static long _nextIdentity = DateTime.UtcNow.Ticks;
    private static int _storageClaimed;
    private readonly string _rootDirectory;

    private AutomataDecisionJournalPathPolicy(string rootDirectory) => _rootDirectory = rootDirectory;

    internal static AutomataDecisionJournalOptions Create(DecisionJournalStatusRegistry status)
    {
        if (status is null) throw new ArgumentNullException(nameof(status));
        var root = AutomataTraceRunRoot.Stable(ArtifactName);
        return new AutomataDecisionJournalOptions(
            status,
            new AutomataDecisionJournalPathPolicy(root),
            ArtifactName);
    }

    public AutomataDecisionJournalSpec Create()
    {
        if (Interlocked.CompareExchange(ref _storageClaimed, 1, 0) != 0)
            throw new InvalidOperationException("The process decision-journal storage has already been claimed.");
        return new AutomataDecisionJournalSpec(
            new FileTraceSegmentStorage(_rootDirectory, ArtifactName, ".osjd"),
            new DecisionJournalRunId(NextIdentity()),
            LiveCandidateMaximumCommittedSegments,
            LiveCandidateBlockCount,
            CheckpointInterval);
    }

    internal static string FormatRelativeArtifactPath(string artifactName)
    {
        if (!DecisionJournalStatus.IsSafeArtifactName(artifactName))
            throw new ArgumentException("A bounded journal artifact basename is required.", nameof(artifactName));
        return AutomataTraceRunRoot.FormatStableRelativePath(artifactName);
    }

    private static ulong NextIdentity() => checked((ulong)Interlocked.Increment(ref _nextIdentity));
}
