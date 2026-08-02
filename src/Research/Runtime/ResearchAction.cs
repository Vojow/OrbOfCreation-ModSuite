using System;

namespace OrbAutomata;

internal enum ResearchActionKind { Develop = 1, Pause = 2, Resume = 3, Cancel = 4, Bonus = 5 }

internal readonly struct ResearchAction
{
    internal ResearchAction(ResearchActionKind kind, Guid targetId, long lifecycleEpoch)
    {
        if (targetId == Guid.Empty) throw new ArgumentException("A research identity is required.", nameof(targetId));
        Kind = kind;
        TargetId = targetId;
        LifecycleEpoch = lifecycleEpoch;
    }

    internal ResearchActionKind Kind { get; }
    internal Guid TargetId { get; }
    internal long LifecycleEpoch { get; }
}
