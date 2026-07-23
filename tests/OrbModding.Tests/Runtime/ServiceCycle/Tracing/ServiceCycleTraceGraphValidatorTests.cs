using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Tracing;

public sealed class ServiceCycleTraceGraphValidatorTests
{
    [Fact]
    public void CompleteCausalGraphIsValidAndComplete()
    {
        var result = Validate(
            ServiceCycleTraceFixtures.Event(1),
            ServiceCycleTraceFixtures.Event(2, parentSequence: 1),
            ServiceCycleTraceFixtures.Event(3, parentSequence: 2));
        Assert.True(result.IsValid);
        Assert.True(result.IsComplete);
        Assert.Equal(ServiceCycleTraceGraphIssue.None, result.Issue);
    }

    [Fact]
    public void OverwrittenParentIsReportedAsValidButIncomplete()
    {
        var drop = new ServiceCycleTraceDropRange(ServiceCycleTraceFixtures.Session, 1, 2);
        var result = Validate(drop, ServiceCycleTraceFixtures.Event(3, parentSequence: 2));
        Assert.True(result.IsValid);
        Assert.False(result.IsComplete);
        Assert.Equal(ServiceCycleTraceGraphIssue.OverwrittenParent, result.Issue);
        Assert.Equal(1, result.OverwrittenParentReferences);
    }

    [Fact]
    public void ZeroDuplicateAndNonMonotonicIdentitiesAreRejected()
    {
        Assert.Equal(ServiceCycleTraceGraphIssue.ZeroEventIdentity,
            Validate(default(ServiceCycleSemanticEvent)).Issue);
        Assert.Equal(ServiceCycleTraceGraphIssue.DuplicateEventIdentity,
            Validate(ServiceCycleTraceFixtures.Event(1), ServiceCycleTraceFixtures.Event(1)).Issue);
        Assert.Equal(ServiceCycleTraceGraphIssue.NonMonotonicEventIdentity,
            Validate(ServiceCycleTraceFixtures.Event(1), ServiceCycleTraceFixtures.Event(2),
                ServiceCycleTraceFixtures.Event(1)).Issue);
    }

    [Fact]
    public void SelfForwardUnknownAndCrossSessionParentsAreRejected()
    {
        Assert.Equal(ServiceCycleTraceGraphIssue.SelfParent,
            Validate(ServiceCycleTraceFixtures.Event(1, parentSequence: 1)).Issue);
        Assert.Equal(ServiceCycleTraceGraphIssue.ForwardParent,
            Validate(ServiceCycleTraceFixtures.Event(1, parentSequence: 2)).Issue);
        Assert.Equal(ServiceCycleTraceGraphIssue.CrossSessionParent,
            Validate(ServiceCycleTraceFixtures.Event(1, parentSequence: 1,
                parentSession: new ServiceCycleTraceSessionId(999))).Issue);
    }

    [Fact]
    public void ParentTimestampCannotBeLaterThanItsResidentChild()
    {
        var parentPayload = ServiceCycleSemanticPayload.CycleFact(
            in ServiceCycleTraceFixtures.Cycle, CommonServiceDecisionCodes.Ready.Value, 30, 0);
        var childPayload = ServiceCycleSemanticPayload.CycleFact(
            in ServiceCycleTraceFixtures.Cycle, 0, 20, 0);
        var parent = new ServiceCycleSemanticEvent(
            new ServiceCycleTraceEventId(ServiceCycleTraceFixtures.Session, 1),
            default,
            ServiceCycleSemanticEventKind.CycleQueued,
            in parentPayload);
        var child = new ServiceCycleSemanticEvent(
            new ServiceCycleTraceEventId(ServiceCycleTraceFixtures.Session, 2),
            parent.Id,
            ServiceCycleSemanticEventKind.CycleStarted,
            in childPayload);

        var result = Validate(parent, child);

        Assert.False(result.IsValid);
        Assert.Equal(ServiceCycleTraceGraphIssue.BackwardTimestampParent, result.Issue);
        Assert.Equal(1, result.EventIndex);
        Assert.Equal(parent.Id, result.AffectedIdentity);
    }

    [Fact]
    public void EventFromAnotherSessionIsRejected()
    {
        var result = Validate(ServiceCycleTraceFixtures.Event(1,
            eventSession: new ServiceCycleTraceSessionId(999)));
        Assert.False(result.IsValid);
        Assert.Equal(ServiceCycleTraceGraphIssue.CrossSessionEvent, result.Issue);
    }

    [Fact]
    public void StreamAccountingMustBeRootedAdjacentAndContiguous()
    {
        Assert.Equal(ServiceCycleTraceGraphIssue.CompleteStreamDoesNotStartAtRoot,
            Validate(ServiceCycleTraceFixtures.Event(2)).Issue);
        Assert.Equal(ServiceCycleTraceGraphIssue.DropDoesNotStartAtRoot,
            Validate(new ServiceCycleTraceDropRange(ServiceCycleTraceFixtures.Session, 2, 2),
                ServiceCycleTraceFixtures.Event(3)).Issue);
        Assert.Equal(ServiceCycleTraceGraphIssue.DropIsNotAdjacentToResidentStream,
            Validate(new ServiceCycleTraceDropRange(ServiceCycleTraceFixtures.Session, 1, 1),
                ServiceCycleTraceFixtures.Event(3)).Issue);
        Assert.Equal(ServiceCycleTraceGraphIssue.NonContiguousResidentIdentities,
            Validate(ServiceCycleTraceFixtures.Event(1), ServiceCycleTraceFixtures.Event(3)).Issue);
    }

    [Fact]
    public void SessionAndDropSessionMustBeValidAndEqual()
    {
        var invalid = new ServiceCycleTraceDocument(ServiceCycleTraceCodec.SchemaVersion, default, default,
            System.Array.Empty<ServiceCycleSemanticEvent>());
        Assert.Equal(ServiceCycleTraceGraphIssue.InvalidSession, ServiceCycleTraceGraphValidator.Validate(invalid).Issue);
        var foreignDrop = new ServiceCycleTraceDropRange(new ServiceCycleTraceSessionId(999), 1, 1);
        Assert.Equal(ServiceCycleTraceGraphIssue.CrossSessionDrop,
            Validate(foreignDrop, ServiceCycleTraceFixtures.Event(2)).Issue);
    }

    [Fact]
    public void EmptyCompleteAndAllDroppedStreamsHaveExplicitSemantics()
    {
        var empty = Validate();
        Assert.True(empty.IsValid);
        Assert.True(empty.IsComplete);
        var dropped = Validate(new ServiceCycleTraceDropRange(ServiceCycleTraceFixtures.Session, 1, 8));
        Assert.True(dropped.IsValid);
        Assert.False(dropped.IsComplete);
        Assert.Equal(ServiceCycleTraceGraphIssue.OverwrittenParent, dropped.Issue);
    }

    [Fact]
    public void InternalDocumentsCannotClaimUnrepresentableMaximumSequence()
    {
        var impossibleDrop = ServiceCycleTraceDropRange.UncheckedForValidationTests(
            ServiceCycleTraceFixtures.Session, 1, ulong.MaxValue);
        Assert.Equal(ServiceCycleTraceGraphIssue.SequenceOutOfRange,
            Validate(impossibleDrop).Issue);

        var impossibleId = ServiceCycleTraceEventId.UncheckedForValidationTests(
            ServiceCycleTraceFixtures.Session, ulong.MaxValue);
        var payload = ServiceCycleTraceFixtures.Payload(ServiceCycleSemanticEventKind.CycleStarted);
        var impossibleEvent = ServiceCycleSemanticEvent.UncheckedForValidationTests(
            impossibleId, ServiceCycleSemanticEventKind.CycleStarted, in payload);
        Assert.Equal(ServiceCycleTraceGraphIssue.SequenceOutOfRange,
            Validate(impossibleEvent).Issue);
    }

    private static ServiceCycleTraceGraphValidation Validate(params ServiceCycleSemanticEvent[] events) =>
        Validate(default, events);

    private static ServiceCycleTraceGraphValidation Validate(
        ServiceCycleTraceDropRange dropped,
        params ServiceCycleSemanticEvent[] events) =>
        ServiceCycleTraceGraphValidator.Validate(new ServiceCycleTraceDocument(
            ServiceCycleTraceCodec.SchemaVersion,
            ServiceCycleTraceFixtures.Session,
            dropped,
            events));
}
