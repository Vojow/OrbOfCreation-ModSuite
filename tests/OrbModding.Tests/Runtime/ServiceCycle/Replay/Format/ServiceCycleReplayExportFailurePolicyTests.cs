using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common.Runtime.Tracing;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Replay.Format;

public sealed class ServiceCycleReplayExportFailurePolicyTests
{
    public static IEnumerable<object[]> ProcessFatalTypes()
    {
        yield return new object[] { typeof(StackOverflowException) };
        yield return new object[] { typeof(OutOfMemoryException) };
        yield return new object[] { typeof(AccessViolationException) };
    }

    [Theory]
    [MemberData(nameof(ProcessFatalTypes))]
    public void WriterDoesNotConvertProcessFatalStorageFailure(Type exceptionType)
    {
        var fixture = ServiceCycleReplayArtifactCodecTests.ArtifactFixture.Create();
        var semantic = ServiceCycleTraceCodec.Decode(fixture.Semantic);
        var slot = new ServiceCycleReplayExportSlot(semantic.Count)
        {
            SemanticSession = semantic.Session,
            Dropped = semantic.Dropped,
            EventCount = semantic.Count,
            Recording = fixture.Snapshot,
        };
        for (var index = 0; index < semantic.Count; index++) slot.Events[index] = semantic[index];
        var expected = Assert.IsAssignableFrom<Exception>(Activator.CreateInstance(exceptionType));

        var actual = Assert.Throws(exceptionType, () => ServiceCycleReplayExportWriter.Write(
            fixture.Session,
            new ThrowingStorage(expected),
            slot,
            retained: 0,
            maximumCommitted: 1));

        Assert.Same(expected, actual);
    }

    [Fact]
    public void OrdinaryStorageFailureRemainsContained()
    {
        Assert.False(ServiceCycleReplayExportFailurePolicy.IsProcessFatal(
            new InvalidOperationException()));
    }

    private sealed class ThrowingStorage : IRestartAwareTraceSegmentStorage
    {
        private readonly Exception _failure;

        internal ThrowingStorage(Exception failure) => _failure = failure;

        public TraceSegmentStorageRecovery Reconcile(int maximumCommittedSegments) => default;
        public object BeginSegment(int ordinal) => new object();
        public void Append(object segment, ReadOnlySpan<byte> record) => throw _failure;
        public void CommitSegment(object segment) => throw new InvalidOperationException();
        public void DiscardSegment(object segment) => throw new InvalidOperationException();
        public void DeleteOldestCommitted() => throw new InvalidOperationException();
    }
}
