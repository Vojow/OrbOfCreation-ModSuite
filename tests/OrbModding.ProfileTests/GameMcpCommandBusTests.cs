using System;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpCommandBusTests
{
    [Fact]
    public void SubmittedCommandIsImmutableAndNotTerminalUntilMainThreadCompletesIt()
    {
        var bus = new GameMcpCommandBus();
        var command = SubmitHarvest(bus);
        Assert.False(command.Completion.TryWait(TimeSpan.FromMilliseconds(1), out _));
        Assert.True(bus.TryDequeue(out var dequeued));
        Assert.Same(command, dequeued);

        bus.Complete(
            command,
            GameMcpCommandResult.Rejected("test", "terminal"));
        Assert.True(command.Completion.TryWait(TimeSpan.FromMilliseconds(50), out var terminal));
        Assert.Equal("test", terminal.Code);
    }

    [Fact]
    public void CompletingTwiceFailsLoudly()
    {
        var bus = new GameMcpCommandBus();
        var command = SubmitHarvest(bus);
        Assert.True(bus.TryDequeue(out _));
        bus.Complete(command, GameMcpCommandResult.Rejected("first", "first"));
        Assert.Throws<InvalidOperationException>(() =>
            bus.Complete(command, GameMcpCommandResult.Rejected("second", "second")));
    }

    [Fact]
    public void QueueCapacityRejectsOverflowWithoutDiscardingAcceptedCommands()
    {
        var bus = new GameMcpCommandBus();
        for (var index = 0; index < GameMcpCommandBus.MaximumPending; index++)
            SubmitHarvest(bus);
        var overflow = SubmitHarvest(bus);
        Assert.True(overflow.Completion.TryWait(TimeSpan.FromMilliseconds(50), out var terminal));
        Assert.Equal("command_queue_full", terminal.Code);
        Assert.Equal(GameMcpCommandBus.MaximumPending, bus.PendingCount);
    }

    [Fact]
    public void CloseCompletesEveryPendingCommandInline()
    {
        var bus = new GameMcpCommandBus();
        var first = SubmitHarvest(bus);
        var second = SubmitHarvest(bus);
        bus.Close("shutdown", "server stopping");
        Assert.True(first.Completion.TryWait(TimeSpan.FromMilliseconds(50), out var firstResult));
        Assert.True(second.Completion.TryWait(TimeSpan.FromMilliseconds(50), out var secondResult));
        Assert.Equal("shutdown", firstResult.Code);
        Assert.Equal("shutdown", secondResult.Code);
        Assert.Equal(0, bus.PendingCount);
    }

    [Fact]
    public void StopEngageOwnsDedicatedHeadOfLineSlot()
    {
        var bus = new GameMcpCommandBus();
        var gameplay = SubmitHarvest(bus);
        var stop = bus.SubmitEmergencyStop(1, engaged: true);
        Assert.True(bus.TryDequeue(out var first));
        Assert.Same(stop, first);
        Assert.True(bus.TryDequeue(out var second));
        Assert.Same(gameplay, second);
    }

    [Fact]
    public void AcceptedStopClosesNewNativeAdmissionImmediately()
    {
        var bus = new GameMcpCommandBus();
        bus.SubmitEmergencyStop(1, engaged: true);
        var blocked = SubmitHarvest(bus);
        Assert.True(blocked.Completion.TryWait(TimeSpan.FromMilliseconds(50), out var result));
        Assert.Equal("emergency_stop_pending", result.Code);
    }

    [Fact]
    public void StopSupersedesQueuedResume()
    {
        var bus = new GameMcpCommandBus();
        var resume = bus.SubmitEmergencyStop(1, engaged: false);
        var stop = bus.SubmitEmergencyStop(1, engaged: true);
        Assert.True(resume.Completion.TryWait(TimeSpan.FromMilliseconds(50), out var result));
        Assert.Equal("superseded_by_emergency_stop", result.Code);
        Assert.True(bus.TryDequeue(out var dequeued));
        Assert.Same(stop, dequeued);
    }

    [Fact]
    public void GadgetSubmissionHasNoConfigurationGenerationPrecondition()
    {
        var bus = new GameMcpCommandBus();
        var screenshot = bus.SubmitGadget(
            GameMcpCommandKind.Screenshot,
            "capture",
            Guid.Empty,
            1,
            string.Empty,
            capture: true,
            saveCapture: false);
        Assert.Equal((ulong)0, screenshot.ExpectedConfigurationGeneration);
        Assert.True(bus.TryDequeue(out var dequeued));
        Assert.Same(screenshot, dequeued);
    }

    [Fact]
    public void AudioLoopControlIsAConfigurationIndependentMainThreadGadget()
    {
        var bus = new GameMcpCommandBus();
        var command = bus.SubmitGadget(
            GameMcpCommandKind.AudioLoopControl,
            "disable",
            Guid.Empty,
            1,
            string.Empty,
            capture: false,
            saveCapture: false);

        Assert.Equal((ulong)0, command.ExpectedConfigurationGeneration);
        Assert.Equal("disable", command.Mode);
        Assert.True(bus.TryDequeue(out var dequeued));
        Assert.Same(command, dequeued);
    }

    [Fact]
    public void ExactQueueRecoveryCarriesDetachedEvidenceAndUsesPriorityAdmission()
    {
        var bus = new GameMcpCommandBus();
        var member = Guid.NewGuid();
        var queue = Guid.NewGuid();
        var command = bus.SubmitActionQueueRecovery(
            decisionWorldGeneration: 27,
            expectedLifecycleGeneration: 8,
            expectedConfigurationGeneration: 9,
            queue,
            member,
            "UpgradeSO",
            excessStacks: 1,
            observedStacks: 1,
            observedPending: 0);

        Assert.True(bus.TryDequeue(out var dequeued));
        Assert.Same(command, dequeued);
        Assert.Equal(GameMcpCommandKind.ActionQueueRecovery, command.Kind);
        Assert.Equal(member, command.TargetId);
        Assert.Equal(queue, command.SecondaryId);
        Assert.Equal("UpgradeSO", command.DerivedNativeType);
        Assert.Equal(1, command.Amount);
        var payload = JObject.Parse(command.PayloadValue);
        Assert.Equal(1, (int?)payload["observedStacks"]);
        Assert.Equal(0, (int?)payload["observedPending"]);
    }

    private static GameMcpCommand SubmitHarvest(GameMcpCommandBus bus) =>
        bus.Submit(
            GameMcpCommandKind.Harvest,
            decisionWorldGeneration: null,
            expectedLifecycleGeneration: 1,
            expectedConfigurationGeneration: 1,
            mode: "fruit_tree",
            Guid.Parse("6782dd13-e229-4385-a1aa-8ed86e6ea1ed"),
            Guid.Empty,
            derivedNativeType: "PlotNodeSO",
            expectedNativeType: string.Empty,
            amount: 1);
}
