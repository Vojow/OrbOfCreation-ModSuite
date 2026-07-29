using System;
using System.Linq;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Tracing;

public sealed class ServiceCycleSemanticEventTests
{
    [Fact]
    public void EveryDeclaredEventKindHasAValidatedFixedPayload()
    {
        var kinds = (ServiceCycleSemanticEventKind[])Enum.GetValues(typeof(ServiceCycleSemanticEventKind));
        Assert.Equal(Enumerable.Range(1, kinds.Length), kinds.Select(kind => (int)kind));
        var ring = new ServiceCycleEventRing(ServiceCycleTraceFixtures.Session, kinds.Length);
        ServiceCycleTraceEventId parent = default;
        foreach (var kind in kinds)
        {
            var payload = ServiceCycleTraceFixtures.Payload(kind);
            parent = ring.Append(kind, in payload, parent);
        }

        Assert.Equal(kinds.Length, ring.Count);
    }

    [Fact]
    public void PayloadShapeCannotBeUsedForAnotherEventCategory()
    {
        var payload = ServiceCycleTraceFixtures.Payload(ServiceCycleSemanticEventKind.ConfigurationPublished);
        Assert.Throws<ArgumentException>(() => new ServiceCycleSemanticEvent(
            new ServiceCycleTraceEventId(ServiceCycleTraceFixtures.Session, 1),
            default,
            ServiceCycleSemanticEventKind.ActionCommitted,
            in payload));
    }

    [Fact]
    public void InvalidKindAndIncoherentBatchAreRejected()
    {
        var valid = ServiceCycleTraceFixtures.Payload(ServiceCycleSemanticEventKind.CycleStarted);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ServiceCycleSemanticEvent(
            new ServiceCycleTraceEventId(ServiceCycleTraceFixtures.Session, 1), default,
            (ServiceCycleSemanticEventKind)999, in valid));

        var invalidBatch = ServiceCycleSemanticPayload.BatchFact(
            in ServiceCycleTraceFixtures.Cycle, 8, 2, 5, 3, 1, 1, 0, 1, 1, 1, 100);
        Assert.Throws<ArgumentException>(() => new ServiceCycleSemanticEvent(
            new ServiceCycleTraceEventId(ServiceCycleTraceFixtures.Session, 1), default,
            ServiceCycleSemanticEventKind.BatchAborted, in invalidBatch));

        var falseCompletion = ServiceCycleSemanticPayload.CycleFact(
            in ServiceCycleTraceFixtures.Cycle, 7, 100, 10);
        Assert.Throws<ArgumentException>(() => new ServiceCycleSemanticEvent(
            new ServiceCycleTraceEventId(ServiceCycleTraceFixtures.Session, 1), default,
            ServiceCycleSemanticEventKind.CycleCompleted, in falseCompletion));
    }

    [Fact]
    public void DirectConstructionRejectsWrongCodeDomainsAndNativeEvidence()
    {
        var invalid = new[]
        {
            (ServiceCycleSemanticEventKind.CycleQueued,
                ServiceCycleSemanticPayload.CycleFact(in ServiceCycleTraceFixtures.Cycle, 4, 100, 0)),
            (ServiceCycleSemanticEventKind.CaptureCompleted,
                ServiceCycleSemanticPayload.CaptureFact(
                    in ServiceCycleTraceFixtures.Capture, 4, 3, 100, 0, ServiceCycleTraceFixtures.Frame)),
            (ServiceCycleSemanticEventKind.ActionCommitted,
                ServiceCycleSemanticPayload.ActionFact(in ServiceCycleTraceFixtures.Cycle, 8, 10, 0, 1, 2, NativeMutationOutcome.Verified, 1, 1, 1, 100, 0, ServiceCycleTraceFixtures.Frame)),
            (ServiceCycleSemanticEventKind.ActionCommitted,
                ServiceCycleSemanticPayload.ActionFact(in ServiceCycleTraceFixtures.Cycle, 8, 10, 0, 1, 1, NativeMutationOutcome.Verified, 1, 1, 0, 100, 0, ServiceCycleTraceFixtures.Frame)),
            (ServiceCycleSemanticEventKind.ActionRejected,
                ServiceCycleSemanticPayload.ActionFact(in ServiceCycleTraceFixtures.Cycle, 8, 10, 0, 2, 1, null, 0, 0, 0, 100, 0, ServiceCycleTraceFixtures.Frame)),
            (ServiceCycleSemanticEventKind.ActionFaulted,
                ServiceCycleSemanticPayload.ActionFact(in ServiceCycleTraceFixtures.Cycle, 8, 10, 0, 3, 5, NativeMutationOutcome.ExecutionThrew, 1, 1, 0, 100, 0, ServiceCycleTraceFixtures.Frame)),
            (ServiceCycleSemanticEventKind.ActionFaulted,
                ServiceCycleSemanticPayload.ActionFact(in ServiceCycleTraceFixtures.Cycle, 8, 10, 0, 3, 7, NativeMutationOutcome.Verified, 1, 1, 1, 100, 0, ServiceCycleTraceFixtures.Frame)),
            (ServiceCycleSemanticEventKind.ActionFaulted,
                ServiceCycleSemanticPayload.ActionFact(in ServiceCycleTraceFixtures.Cycle, 8, 10, 0, 3, 7, NativeMutationOutcome.ExecutionThrew, 1, 0, 0, 100, 0, ServiceCycleTraceFixtures.Frame)),
            (ServiceCycleSemanticEventKind.BatchCompleted,
                ServiceCycleSemanticPayload.BatchFact(in ServiceCycleTraceFixtures.Cycle, 8, 1, 1, 3, 3, -1, 0, 2, 2, 2, 100)),
            (ServiceCycleSemanticEventKind.BatchCompleted,
                ServiceCycleSemanticPayload.BatchFact(in ServiceCycleTraceFixtures.Cycle, 8, 1, 1024, 1, 1, -1, 0, 1, 1, 1, 100)),
            (ServiceCycleSemanticEventKind.BatchOrphaned,
                ServiceCycleSemanticPayload.BatchFact(in ServiceCycleTraceFixtures.Cycle, 8, 4, 3, 3, 1, -1, 2, 0, 0, 0, 100)),
            (ServiceCycleSemanticEventKind.FaultObserved,
                ServiceCycleSemanticPayload.FaultOrRetry(ServiceCycleTraceFixtures.Service, 2, 1, 5, 1, 100, 0)),
            (ServiceCycleSemanticEventKind.LifecycleConstructionDeferred,
                ServiceCycleSemanticPayload.LifecycleConstructionDeferred(
                    ServiceCycleTraceFixtures.Service, 2, CommonServiceDecisionCodes.Ready.Value, 100, 200)),
            (ServiceCycleSemanticEventKind.LifecycleConstructionDeferred,
                ServiceCycleSemanticPayload.LifecycleConstructionDeferred(
                    ServiceCycleTraceFixtures.Service, 2,
                    CommonServiceDecisionCodes.TransientContention.Value, 100, 99)),
        };
        foreach (var item in invalid)
        {
            var payload = item.Item2;
            Assert.Throws<ArgumentException>(() => new ServiceCycleSemanticEvent(
                new ServiceCycleTraceEventId(ServiceCycleTraceFixtures.Session, 1), default, item.Item1, in payload));
        }
    }
}
