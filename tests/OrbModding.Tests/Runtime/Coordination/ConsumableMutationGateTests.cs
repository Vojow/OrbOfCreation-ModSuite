using OrbAutomata;
using Xunit;

namespace OrbModding.Tests.Runtime.Coordination;

public sealed class ConsumableMutationGateTests
{
    [Fact]
    public void BlocksOnlyWorldsCapturedNoLaterThanTheMutationInTheSameLifecycle()
    {
        var gate = new ConsumableMutationGate();

        gate.ObserveAttempt(lifecycle: 7, mutationFrame: 100);

        Assert.True(gate.Blocks(lifecycle: 7, collectedFrame: 99));
        Assert.True(gate.Blocks(lifecycle: 7, collectedFrame: 100));
        Assert.False(gate.Blocks(lifecycle: 7, collectedFrame: 101));
        Assert.False(gate.Blocks(lifecycle: 8, collectedFrame: 1));
    }

    [Fact]
    public void LaterAttemptExtendsTheGapAndLifecycleInvalidationClearsIt()
    {
        var gate = new ConsumableMutationGate();
        gate.ObserveAttempt(7, 100);
        gate.ObserveAttempt(7, 105);

        Assert.True(gate.Blocks(7, 104));
        gate.Invalidate(7);
        Assert.False(gate.Blocks(7, 104));
    }
}
