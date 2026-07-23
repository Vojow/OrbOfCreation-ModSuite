namespace OrbModding.Common.Runtime.ServiceCycle.Tracing;

public enum ServiceCycleTraceGraphIssue
{
    None = 0,
    OverwrittenParent = 1,
    ZeroEventIdentity = 2,
    CrossSessionEvent = 3,
    DuplicateEventIdentity = 4,
    NonMonotonicEventIdentity = 5,
    SelfParent = 6,
    ForwardParent = 7,
    CrossSessionParent = 8,
    UnknownParent = 9,
    InvalidSession = 10,
    CrossSessionDrop = 11,
    CompleteStreamDoesNotStartAtRoot = 12,
    DropDoesNotStartAtRoot = 13,
    DropIsNotAdjacentToResidentStream = 14,
    NonContiguousResidentIdentities = 15,
    SequenceOutOfRange = 16,
    BackwardTimestampParent = 17,
}

public readonly struct ServiceCycleTraceGraphValidation
{
    internal ServiceCycleTraceGraphValidation(
        bool valid,
        bool complete,
        ServiceCycleTraceGraphIssue issue,
        int eventIndex,
        ServiceCycleTraceEventId affectedIdentity,
        int overwrittenParentReferences)
    {
        IsValid = valid;
        IsComplete = complete;
        Issue = issue;
        EventIndex = eventIndex;
        AffectedIdentity = affectedIdentity;
        OverwrittenParentReferences = overwrittenParentReferences;
    }

    public bool IsValid { get; }
    public bool IsComplete { get; }
    public ServiceCycleTraceGraphIssue Issue { get; }
    public int EventIndex { get; }
    public ServiceCycleTraceEventId AffectedIdentity { get; }
    public int OverwrittenParentReferences { get; }
}

/// <summary>Validates causal graph structure only. This type does not replay service behavior.</summary>
public static class ServiceCycleTraceGraphValidator
{
    public static ServiceCycleTraceGraphValidation Validate(ServiceCycleTraceDocument document)
    {
        if (document is null) throw new System.ArgumentNullException(nameof(document));
        if (!document.Session.IsValid)
            return Invalid(ServiceCycleTraceGraphIssue.InvalidSession, -1, default);
        if (document.ServiceCapacity <= 0)
            return Invalid(ServiceCycleTraceGraphIssue.InvalidSession, -1, default);
        if (document.Dropped.IsPresent && document.Dropped.Session != document.Session)
            return Invalid(ServiceCycleTraceGraphIssue.CrossSessionDrop, -1, default);
        if (document.Dropped.IsPresent && document.Dropped.LastSequence > ServiceCycleTraceEventId.MaximumSequence)
            return Invalid(ServiceCycleTraceGraphIssue.SequenceOutOfRange, -1, default);
        if (document.Dropped.IsPresent && document.Dropped.FirstSequence != 1)
            return Invalid(ServiceCycleTraceGraphIssue.DropDoesNotStartAtRoot, -1, default);
        if (document.Count == 0)
        {
            return new ServiceCycleTraceGraphValidation(
                true,
                !document.Dropped.IsPresent,
                document.Dropped.IsPresent
                    ? ServiceCycleTraceGraphIssue.OverwrittenParent
                    : ServiceCycleTraceGraphIssue.None,
                -1,
                default,
                0);
        }

        var first = document[0].Id;
        if (!first.IsValid)
            return Invalid(first.Sequence > ServiceCycleTraceEventId.MaximumSequence
                ? ServiceCycleTraceGraphIssue.SequenceOutOfRange
                : ServiceCycleTraceGraphIssue.ZeroEventIdentity, 0, first);
        if (first.Session != document.Session)
            return Invalid(ServiceCycleTraceGraphIssue.CrossSessionEvent, 0, first);
        if (!document.Dropped.IsPresent && first.Sequence != 1)
            return Invalid(ServiceCycleTraceGraphIssue.CompleteStreamDoesNotStartAtRoot, 0, first);
        if (document.Dropped.IsPresent &&
            (document.Dropped.LastSequence == ulong.MaxValue ||
             document.Dropped.LastSequence + 1 != first.Sequence))
            return Invalid(ServiceCycleTraceGraphIssue.DropIsNotAdjacentToResidentStream, 0, first);

        var overwrittenParentReferences = 0;
        ulong previousSequence = 0;
        for (var index = 0; index < document.Count; index++)
        {
            var item = document[index];
            if (item.Payload.Service > (ulong)document.ServiceCapacity)
                return Invalid(ServiceCycleTraceGraphIssue.InvalidSession, index, item.Id);
            if (!item.Id.IsValid)
                return Invalid(item.Id.Sequence > ServiceCycleTraceEventId.MaximumSequence
                    ? ServiceCycleTraceGraphIssue.SequenceOutOfRange
                    : ServiceCycleTraceGraphIssue.ZeroEventIdentity, index, item.Id);
            if (item.Id.Session != document.Session)
                return Invalid(ServiceCycleTraceGraphIssue.CrossSessionEvent, index, item.Id);
            if (index != 0 && item.Id.Sequence == previousSequence)
                return Invalid(ServiceCycleTraceGraphIssue.DuplicateEventIdentity, index, item.Id);
            if (item.Id.Sequence <= previousSequence)
                return Invalid(ServiceCycleTraceGraphIssue.NonMonotonicEventIdentity, index, item.Id);
            if (index != 0 && item.Id.Sequence != previousSequence + 1)
                return Invalid(ServiceCycleTraceGraphIssue.NonContiguousResidentIdentities, index, item.Id);
            previousSequence = item.Id.Sequence;

            if (item.Parent.Sequence > ServiceCycleTraceEventId.MaximumSequence)
                return Invalid(ServiceCycleTraceGraphIssue.SequenceOutOfRange, index, item.Parent);
            if (!item.HasParent) continue;
            if (item.Parent.Session != document.Session)
                return Invalid(ServiceCycleTraceGraphIssue.CrossSessionParent, index, item.Parent);
            if (item.Parent.Sequence == item.Id.Sequence)
                return Invalid(ServiceCycleTraceGraphIssue.SelfParent, index, item.Parent);
            if (item.Parent.Sequence > item.Id.Sequence)
                return Invalid(ServiceCycleTraceGraphIssue.ForwardParent, index, item.Parent);
            if (item.Parent.Sequence >= first.Sequence)
            {
                var parentIndex = checked((int)(item.Parent.Sequence - first.Sequence));
                if (document[parentIndex].Payload.TimestampTicks > item.Payload.TimestampTicks)
                    return Invalid(ServiceCycleTraceGraphIssue.BackwardTimestampParent, index, item.Parent);
                continue;
            }
            if (document.Dropped.IsPresent &&
                item.Parent.Sequence >= document.Dropped.FirstSequence &&
                item.Parent.Sequence <= document.Dropped.LastSequence)
            {
                overwrittenParentReferences++;
                continue;
            }

            return Invalid(ServiceCycleTraceGraphIssue.UnknownParent, index, item.Parent);
        }

        var complete = !document.Dropped.IsPresent && overwrittenParentReferences == 0;
        return new ServiceCycleTraceGraphValidation(
            true,
            complete,
            complete ? ServiceCycleTraceGraphIssue.None : ServiceCycleTraceGraphIssue.OverwrittenParent,
            -1,
            default,
            overwrittenParentReferences);
    }

    private static ServiceCycleTraceGraphValidation Invalid(
        ServiceCycleTraceGraphIssue issue,
        int index,
        ServiceCycleTraceEventId identity) =>
        new(false, false, issue, index, identity, 0);
}
