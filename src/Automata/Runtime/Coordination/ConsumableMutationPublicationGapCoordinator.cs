using System;
using System.Threading;

namespace OrbAutomata;

/// <summary>
/// Prevents Auto Items and Auto Scribe from crossing the gap between a native mutation and the
/// first consumables reading that can contain it.
/// </summary>
/// <remarks>
/// Mutation and publication updates occur on the Unity main thread, while Auto Items also reads the
/// immutable snapshot from its worker. Atomic snapshot replacement keeps that crossing coherent.
/// The mutation frame and collection frame deliberately use the host's one shared frame counter.
/// </remarks>
internal sealed class ConsumableMutationPublicationGapCoordinator
{
    private sealed class Snapshot
    {
        internal Snapshot(bool open, long lifecycle, long mutationFrame)
        {
            Open = open;
            Lifecycle = lifecycle;
            MutationFrame = mutationFrame;
        }

        internal bool Open { get; }
        internal long Lifecycle { get; }
        internal long MutationFrame { get; }
    }

    private Snapshot _snapshot = new(open: false, lifecycle: 0, mutationFrame: 0);

    internal bool BlocksMutation(long lifecycle)
    {
        var snapshot = Volatile.Read(ref _snapshot);
        return snapshot.Open && lifecycle > 0 && lifecycle == snapshot.Lifecycle;
    }

    /// <summary>Records one native mutation attempt, whether or not its postcondition verified.</summary>
    internal void ObserveMutationAttempt(long lifecycle, long mutationFrame)
    {
        if (lifecycle <= 0)
            throw new ArgumentOutOfRangeException(nameof(lifecycle));
        if (mutationFrame < 0)
            throw new ArgumentOutOfRangeException(nameof(mutationFrame));

        while (true)
        {
            var observed = Volatile.Read(ref _snapshot);
            var requiredFrame = observed.Open && observed.Lifecycle == lifecycle
                ? Math.Max(observed.MutationFrame, mutationFrame)
                : mutationFrame;
            if (observed.Open && observed.Lifecycle == lifecycle &&
                observed.MutationFrame == requiredFrame)
                return;
            var updated = new Snapshot(open: true, lifecycle, requiredFrame);
            if (ReferenceEquals(
                    Interlocked.CompareExchange(ref _snapshot, updated, observed),
                    observed))
                return;
        }
    }

    /// <summary>
    /// Clears only from a complete consumables read in the same lifecycle strictly after the last
    /// mutation attempt. A pre-mutation capture published later never reaches this boundary again.
    /// </summary>
    internal bool ObserveConsumablesCapture(
        long lifecycle,
        long collectedAtFrame,
        bool consumablesClean)
    {
        while (true)
        {
            var observed = Volatile.Read(ref _snapshot);
            if (!observed.Open || !consumablesClean || lifecycle != observed.Lifecycle ||
                collectedAtFrame <= observed.MutationFrame)
                return false;
            var updated = new Snapshot(
                open: false,
                observed.Lifecycle,
                observed.MutationFrame);
            if (ReferenceEquals(
                    Interlocked.CompareExchange(ref _snapshot, updated, observed),
                    observed))
                return true;
        }
    }

    internal long MutationFrame
    {
        get { return Volatile.Read(ref _snapshot).MutationFrame; }
    }

    internal long Lifecycle
    {
        get { return Volatile.Read(ref _snapshot).Lifecycle; }
    }
}
