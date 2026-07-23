using System;
using System.IO;
using System.Threading;
using BepInEx;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Format;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Status;
using OrbModding.Common.Runtime.Tracing;

namespace OrbAutomata;

internal sealed class AutomataDecisionJournalPathPolicy : IAutomataDecisionJournalSource
{
    internal const int LiveCandidateBlockCount = 10;
    internal const int LiveCandidateMaximumCommittedSegments = 10_080;
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
        var root = Path.Combine(
            Paths.ConfigPath,
            "OrbOfCreation-ModSuite",
            "trace",
            ArtifactName);
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
        return "BepInEx/config/OrbOfCreation-ModSuite/trace/" + artifactName;
    }

    private static ulong NextIdentity() => checked((ulong)Interlocked.Increment(ref _nextIdentity));
}
