using OrbModConfig;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Status;
using Xunit;

namespace OrbModding.Tests.OrbModConfig;

public sealed class DecisionJournalPresenterTests
{
    [Fact]
    public void PresenterDistinguishesUnavailableRecordingAndFaultedStates()
    {
        var unavailable = DecisionJournalPresenter.Build(DecisionJournalStatus.Unavailable);
        var recording = DecisionJournalPresenter.Build(Status(
            DecisionJournalStatusState.Recording,
            DecisionJournalStatusResult.None,
            firstIncompleteSequence: 0,
            pendingBlocks: 1));
        var faulted = DecisionJournalPresenter.Build(Status(
            DecisionJournalStatusState.Faulted,
            DecisionJournalStatusResult.WriteFailed,
            firstIncompleteSequence: 3,
            pendingBlocks: 0));

        Assert.Contains("Unavailable", unavailable);
        Assert.Contains("3 accepted", recording);
        Assert.Contains("1 retained | 0 evicted", recording);
        Assert.Contains("buffers 1 pending / 2 peak", recording);
        Assert.Contains("4 pruned at startup", recording);
        Assert.Contains("2 stale temporary files removed", recording);
        Assert.Contains("background write failed", faulted);
        Assert.Contains("First missing sequence: 3", faulted);
    }

    private static DecisionJournalStatus Status(
        DecisionJournalStatusState state,
        DecisionJournalStatusResult result,
        long firstIncompleteSequence,
        int pendingBlocks) => new(
        state,
        acceptedRecords: 3,
        writtenRecords: 2,
        discardedRecords: state == DecisionJournalStatusState.Faulted ? 1 : 0,
        bytesWritten: 1_024,
        writtenSegments: 1,
        retainedSegments: 1,
        evictedSegments: 0,
        startupPrunedSegments: 4,
        staleTemporaryFilesRemoved: 2,
        pendingBlocks,
        peakPendingBlocks: 2,
        firstIncompleteSequence,
        result,
        "journal");
}
