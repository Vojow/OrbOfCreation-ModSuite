using System;
using System.IO;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Format;
using Xunit;
using static OrbModding.Tests.Runtime.ServiceCycle.Observation.Journal.DecisionJournalTestData;

namespace OrbModding.Tests.Runtime.ServiceCycle.Observation.Journal;

public sealed class DecisionJournalWindowAssemblerTests
{
    [Fact]
    public void RetainedSuffixPreservesRunLocalFencesAndAcceptsTimestampOverlap()
    {
        var assembler = new DecisionJournalWindowAssembler();
        Add(assembler, Run(11), 4, 9, Decision(1, 100));
        Add(assembler, Run(11), 5, 10, Decision(2, 50), Decision(3, 120));
        Add(assembler, Run(12), 6, 1, Decision(4, 10));

        var window = assembler.Complete();

        Assert.Equal((ulong)4, window.FirstStorageOrdinal);
        Assert.Equal((ulong)6, window.LastStorageOrdinal);
        Assert.Equal((ulong)3, window.SegmentCount);
        Assert.Equal((ulong)4, window.RecordCount);
        Assert.Equal(2, window.RunCount);
        var first = window.GetRun(0);
        Assert.Equal((ulong)9, first.FirstRecordSequence);
        Assert.Equal((ulong)11, first.LastRecordSequence);
        Assert.Equal(50, first.FirstTimestampTicks);
        Assert.Equal(121, first.LastTimestampTicks);
        Assert.Equal((ulong)1, window.GetRun(1).FirstRecordSequence);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RejectsBrokenStorageAndRunFences(int mutation)
    {
        var assembler = new DecisionJournalWindowAssembler();
        Add(assembler, Run(11), 4, 1, Decision(1, 10));

        switch (mutation)
        {
            case 0:
                Assert.Throws<InvalidDataException>(() =>
                    Add(assembler, Run(11), 6, 2, Decision(2, 20)));
                break;
            case 1:
                Assert.Throws<InvalidDataException>(() =>
                    Add(assembler, Run(11), 5, 3, Decision(2, 20)));
                break;
            case 2:
                Assert.Throws<InvalidDataException>(() =>
                    Add(assembler, Run(12), 5, 2, Decision(2, 20)));
                break;
            case 3:
                Add(assembler, Run(12), 5, 1, Decision(2, 20));
                Assert.Throws<InvalidDataException>(() =>
                    Add(assembler, Run(11), 6, 1, Decision(3, 30)));
                break;
        }
        Assert.Throws<InvalidOperationException>(() => assembler.Complete());
    }

    [Fact]
    public void RejectedNullInputPoisonsTheAssembler()
    {
        var assembler = new DecisionJournalWindowAssembler();

        Assert.Throws<ArgumentNullException>(() => assembler.Add(null!, 0));
        Assert.Throws<InvalidOperationException>(() => assembler.Complete());
    }

    private static DecisionJournalRecord Decision(ulong cycle, long timestamp) =>
        DecisionJournalRecord.Decision(CreateObservation(cycle, timestamp));

    private static DecisionJournalRunId Run(ulong value) => new(value);

    private static void Add(
        DecisionJournalWindowAssembler assembler,
        DecisionJournalRunId run,
        ulong ordinal,
        ulong firstSequence,
        params DecisionJournalRecord[] records) => assembler.Add(
        new DecisionJournalSegmentDocument(run, ordinal, firstSequence, records),
        DecisionJournalSegmentCodec.GetEncodedLength(records.Length));
}
