using OrbAutomata;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using Xunit;

namespace OrbModding.Tests.Services.AutoItems.Runtime.ServiceCycle;

public sealed class AutoItemsServiceCycleTypeSafetyTests
{
    [Fact]
    public void WorkerStateUsesOnlyAuditedServiceCycleStorage()
    {
        var violation = ServiceCycleTypeGraphWalker.Validate(
            typeof(AutoItemsCycleState),
            ServiceCycleTypeRole.State,
            "state");

        Assert.False(violation.HasValue, violation?.Message);
    }
}
