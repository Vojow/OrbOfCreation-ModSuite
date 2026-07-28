using System;
using OrbModding.Common.Runtime.World;

namespace OrbMentor;

internal readonly struct MentorCycleAction
{
    internal MentorCycleAction(
        MasteryExperienceDomain domain,
        Guid recipientId,
        MentorAmount amount,
        int masteryCeilingExclusive,
        long collectedAtEpoch)
    {
        Domain = domain;
        RecipientId = recipientId;
        Amount = amount;
        MasteryCeilingExclusive = masteryCeilingExclusive;
        CollectedAtEpoch = collectedAtEpoch;
    }

    public MasteryExperienceDomain Domain { get; }
    public Guid RecipientId { get; }
    public MentorAmount Amount { get; }
    public int MasteryCeilingExclusive { get; }
    public long CollectedAtEpoch { get; }
}

internal readonly struct MentorDecisionMetrics
{
    internal MentorDecisionMetrics(
        MasteryExperienceDomain domain,
        long sequence,
        int candidates,
        int recipients,
        int plannedActions,
        long missedInputs)
    {
        Domain = domain;
        Sequence = sequence;
        Candidates = candidates;
        Recipients = recipients;
        PlannedActions = plannedActions;
        MissedInputs = missedInputs;
    }

    public MasteryExperienceDomain Domain { get; }
    public long Sequence { get; }
    public int Candidates { get; }
    public int Recipients { get; }
    public int PlannedActions { get; }
    public long MissedInputs { get; }
}
