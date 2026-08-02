using System;

namespace OrbAutomata;

internal enum ChallengeActionKind
{
    Select = 1,
    Queue = 2,
    Abandon = 3,
    FetchTime = 4,
    FetchPrestige = 5,
}

internal readonly struct ChallengeAction
{
    internal ChallengeAction(ChallengeActionKind kind, Guid targetId, long lifecycleEpoch)
    {
        Kind = kind;
        TargetId = targetId;
        LifecycleEpoch = lifecycleEpoch;
    }

    internal ChallengeActionKind Kind { get; }
    internal Guid TargetId { get; }
    internal long LifecycleEpoch { get; }
    internal bool HasTarget => Kind is ChallengeActionKind.Select or ChallengeActionKind.Queue or ChallengeActionKind.Abandon;
}
