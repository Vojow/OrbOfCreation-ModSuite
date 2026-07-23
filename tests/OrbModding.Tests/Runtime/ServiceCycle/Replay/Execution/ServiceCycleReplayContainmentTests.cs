using OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Replay.Execution;

public sealed class ServiceCycleReplayContainmentTests
{
    [Fact]
    public void CleanupPhaseCannotRelabelCapturedPumpException()
    {
        var cycle = new ServiceCycleReplayCycleKey(1, 1, 1, 1, 1, 1);
        var cursor = new ServiceCycleReplayFailureCursor(cycle);
        cursor.Enter(ServiceCycleReplayExecutionDetailCode.ProductionPumpRejected, cycle);
        cursor.CapturePrimaryException();

        cursor.Enter(ServiceCycleReplayExecutionDetailCode.ProductionCleanupRejected, cycle);
        var result = cursor.TranslateException();

        Assert.Equal(
            (int)ServiceCycleReplayExecutionDetailCode.ProductionPumpRejected,
            result.Failure.Fault.DetailCode);
    }

    [Fact]
    public void CleanupOnlyExceptionRemainsCleanupTyped()
    {
        var cycle = new ServiceCycleReplayCycleKey(1, 1, 1, 1, 1, 1);
        var cursor = new ServiceCycleReplayFailureCursor(cycle);
        cursor.Enter(ServiceCycleReplayExecutionDetailCode.ProductionCleanupRejected, cycle);

        var result = cursor.TranslateException();

        Assert.Equal(
            (int)ServiceCycleReplayExecutionDetailCode.ProductionCleanupRejected,
            result.Failure.Fault.DetailCode);
    }
}
