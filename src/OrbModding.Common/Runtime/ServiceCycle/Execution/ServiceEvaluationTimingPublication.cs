using System.Threading;
using OrbModding.Common.Runtime;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

internal readonly struct ServiceEvaluationTimingReadCandidate
{
    internal ServiceEvaluationTimingReadCandidate(
        int stampBefore,
        long requestSequence,
        long startedTicks,
        long completedTicks,
        bool complete,
        int stampAfter,
        long trailingRequestSequence)
    {
        StampBefore = stampBefore;
        RequestSequence = requestSequence;
        StartedTicks = startedTicks;
        CompletedTicks = completedTicks;
        Complete = complete;
        StampAfter = stampAfter;
        TrailingRequestSequence = trailingRequestSequence;
    }

    internal int StampBefore { get; }
    internal long RequestSequence { get; }
    internal long StartedTicks { get; }
    internal long CompletedTicks { get; }
    internal bool Complete { get; }
    internal int StampAfter { get; }
    internal long TrailingRequestSequence { get; }
}

internal struct ServiceEvaluationTimingPublication
{
    private const int MaximumReadAttempts = 3;

    private int _stamp;
    private long _requestSequence;
    private long _startedTicks;
    private long _completedTicks;
    private int _complete;

    internal void Begin(long requestSequence, MonotonicTimestamp startedAt)
    {
        BeginWrite();
        Interlocked.Exchange(ref _requestSequence, requestSequence);
        Interlocked.Exchange(ref _startedTicks, startedAt.Ticks);
        Interlocked.Exchange(ref _completedTicks, 0);
        Volatile.Write(ref _complete, 0);
        EndWrite();
    }

    internal void Complete(MonotonicTimestamp completedAt)
    {
        BeginWrite();
        Interlocked.Exchange(ref _completedTicks, completedAt.Ticks);
        Volatile.Write(ref _complete, 1);
        EndWrite();
    }

    internal bool TryRead(out ServiceEvaluationTimingFact timing)
    {
        for (var attempt = 0; attempt < MaximumReadAttempts; attempt++)
        {
            var stampBefore = ReadStamp();
            if ((stampBefore & 1) != 0) continue;

            var requestSequence = Interlocked.Read(ref _requestSequence);
            var startedTicks = Interlocked.Read(ref _startedTicks);
            var complete = Volatile.Read(ref _complete) != 0;
            var completedTicks = complete ? Interlocked.Read(ref _completedTicks) : 0;
            var stampAfter = ReadStamp();
            var trailingRequestSequence = Interlocked.Read(ref _requestSequence);
            var candidate = new ServiceEvaluationTimingReadCandidate(
                stampBefore,
                requestSequence,
                startedTicks,
                completedTicks,
                complete,
                stampAfter,
                trailingRequestSequence);
            if (TryMaterialize(in candidate, out timing)) return true;
        }

        timing = default;
        return false;
    }

    internal static bool TryMaterialize(
        in ServiceEvaluationTimingReadCandidate candidate,
        out ServiceEvaluationTimingFact timing)
    {
        if ((candidate.StampBefore & 1) != 0 ||
            candidate.StampBefore != candidate.StampAfter ||
            candidate.RequestSequence != candidate.TrailingRequestSequence)
        {
            timing = default;
            return false;
        }

        timing = candidate.RequestSequence > 0
            ? new ServiceEvaluationTimingFact(
                candidate.RequestSequence,
                new MonotonicTimestamp(candidate.StartedTicks),
                candidate.Complete
                    ? new MonotonicTimestamp(candidate.CompletedTicks)
                    : default,
                candidate.Complete)
            : default;
        return true;
    }

    private void BeginWrite() => Interlocked.Increment(ref _stamp);

    private void EndWrite() => Interlocked.Increment(ref _stamp);

    private int ReadStamp() => Interlocked.CompareExchange(ref _stamp, 0, 0);
}
