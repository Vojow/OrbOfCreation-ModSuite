using OrbAutomata;
using Xunit;

namespace OrbModding.Tests.Runtime.Coordination;

public sealed class ConsumableMutationPublicationGapCoordinatorTests
{
    [Fact]
    public void OnlyASameLifecycleCleanCaptureStrictlyAfterMutationClosesTheGap()
    {
        var gap = new ConsumableMutationPublicationGapCoordinator();
        gap.ObserveMutationAttempt(lifecycle: 7, mutationFrame: 40);

        Assert.True(gap.BlocksMutation(7));
        Assert.False(gap.BlocksMutation(8));
        Assert.False(gap.ObserveConsumablesCapture(7, 41, consumablesClean: false));
        Assert.False(gap.ObserveConsumablesCapture(8, 41, consumablesClean: true));
        Assert.False(gap.ObserveConsumablesCapture(7, 39, consumablesClean: true));
        Assert.False(gap.ObserveConsumablesCapture(7, 40, consumablesClean: true));
        Assert.True(gap.BlocksMutation(7));

        Assert.True(gap.ObserveConsumablesCapture(7, 41, consumablesClean: true));
        Assert.False(gap.BlocksMutation(7));
    }

    [Fact]
    public void CaptureObservedBeforeMutationCannotClearWhenItsPublicationArrivesLater()
    {
        var gap = new ConsumableMutationPublicationGapCoordinator();

        Assert.False(gap.ObserveConsumablesCapture(3, 10, consumablesClean: true));
        gap.ObserveMutationAttempt(3, 11);

        // Publication has no coordinator callback. Its capture was already observed at frame 10,
        // so delivering it after the frame-11 mutation cannot acknowledge the open gap.
        Assert.True(gap.BlocksMutation(3));
    }

    [Fact]
    public void LaterAttemptExtendsTheRequiredCaptureFrame()
    {
        var gap = new ConsumableMutationPublicationGapCoordinator();
        gap.ObserveMutationAttempt(5, 20);
        gap.ObserveMutationAttempt(5, 22);

        Assert.Equal(22, gap.MutationFrame);
        Assert.False(gap.ObserveConsumablesCapture(5, 21, consumablesClean: true));
        Assert.True(gap.ObserveConsumablesCapture(5, 23, consumablesClean: true));
    }
}
