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

    /// <summary>The journal's share of the suite's ~100 MB on-disk budget, in whole segments.</summary>
    /// <remarks>
    /// A segment is 80 header + 128 x 512 records + 40 footer = 65,656 bytes, so 1,520 full segments
    /// occupy 99,797,120 bytes. The floor on coverage is one checkpoint segment per minute — over 25
    /// hours of unattended play before the oldest evidence rolls off, and far longer than that
    /// whenever segments fill on decisions rather than on the checkpoint.
    /// </remarks>
    internal const int LiveCandidateMaximumCommittedSegments = 1_520;
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
