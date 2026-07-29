using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class ServiceCycleProfileOperationCountersTests
{
    [Fact]
    public void ExactMaximumIsRepresentableButOverflowIsRejected()
    {
        var counters = new ServiceCycleProfileOperationCounters();
        counters.AddListEntries(uint.MaxValue);
        counters.AddReflectedFieldReads(3);
        counters.AddRecordCopies(2);

        Assert.True(counters.TrySnapshot(out var operations));

        Assert.Equal(uint.MaxValue, operations.ListEntries);
        Assert.Equal((uint)3, operations.ReflectedFieldReads);
        Assert.Equal((uint)2, operations.RecordCopies);

        counters.AddListEntries();
        Assert.False(counters.TrySnapshot(out var rejected));
        Assert.Equal(default(ServiceCycleProfileOperations), rejected);
    }
}
