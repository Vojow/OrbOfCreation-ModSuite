using System;

namespace OrbChronicle;

internal interface IChronicleRuntime
{
    ChronicleRunSnapshot Snapshot { get; }
    ChronicleHistorySnapshot History { get; }
    long PresentationRevision { get; }
    ChronicleCommandOutcome Start();
    ChronicleCommandOutcome Pause();
    ChronicleCommandOutcome Resume();
    ChronicleCommandOutcome Abandon();
    void CycleComparison();
    bool TrySelectComparison(string mode, string runId, out string reason);
}

internal sealed class ChronicleRuntime : IChronicleRuntime
{
    private readonly ChronicleRunTracker _tracker = new();
    private readonly ChronicleHistory _history;

    internal ChronicleRuntime(string historyPath, Action<string> logWarning)
    {
        _history = new ChronicleHistory(historyPath, logWarning);
    }

    public ChronicleRunSnapshot Snapshot => _tracker.Snapshot;
    public ChronicleHistorySnapshot History => _history.Project(Snapshot);
    public long PresentationRevision => unchecked(Snapshot.Revision * 397 ^ History.Revision);
    internal ChronicleWorldObservation LatestObservation => _tracker.LatestObservation;

    internal void Observe(in ChronicleWorldObservation observation)
    {
        _tracker.Observe(in observation);
        _history.Observe(_tracker.Snapshot);
    }

    public ChronicleCommandOutcome Start() => Apply(_tracker.Start());
    public ChronicleCommandOutcome Pause() => Apply(_tracker.Pause());
    public ChronicleCommandOutcome Resume() => Apply(_tracker.Resume());
    public ChronicleCommandOutcome Abandon() => Apply(_tracker.Abandon());

    public void CycleComparison()
    {
        _history.CycleComparison(_tracker.Snapshot);
    }

    public bool TrySelectComparison(string mode, string runId, out string reason) =>
        _history.TrySelect(_tracker.Snapshot, mode, runId, out reason);

    private ChronicleCommandOutcome Apply(ChronicleCommandOutcome outcome)
    {
        if (outcome.Accepted) _history.Observe(_tracker.Snapshot);
        return outcome;
    }
}
