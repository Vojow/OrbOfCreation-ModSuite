using System;
using System.Threading;
using OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Control;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Observation.FullTrace;

public sealed class ManualFullTraceControlRegistryTests
{
    [Fact]
    public void RegistryOwnsOneProducerAndPublishesAvailabilityTransitions()
    {
        var registry = new ManualFullTraceControlRegistry();

        Assert.Equal(ManualFullTraceState.Unavailable, registry.Status.State);
        Assert.Equal(0, registry.Revision);
        Assert.Equal(ManualFullTraceCommandResult.Unavailable, registry.RequestStart());

        var registration = registry.Register();
        Assert.Equal(ManualFullTraceState.Idle, registry.Status.State);
        Assert.Equal(1, registry.Revision);
        Assert.Throws<InvalidOperationException>(() => registry.Register());
        Assert.False(registration.Publish(ManualFullTraceStatus.Idle));
        Assert.Equal(1, registry.Revision);

        registration.Dispose();
        Assert.Equal(ManualFullTraceState.Unavailable, registry.Status.State);
        Assert.Equal(2, registry.Revision);
        Assert.Throws<ObjectDisposedException>(() => registration.TryTakeCommand(out _));
    }

    [Fact]
    public void CommandsAreSingleSlotAndAdmittedOnlyForCompatibleStates()
    {
        var registry = new ManualFullTraceControlRegistry();
        using var registration = registry.Register();

        Assert.Equal(ManualFullTraceCommandResult.InvalidState, registry.RequestStop());
        Assert.Equal(ManualFullTraceCommandResult.Accepted, registry.RequestStart());
        Assert.Equal(ManualFullTraceCommand.Start, registry.PendingCommand);
        Assert.Equal(2, registry.Revision);
        Assert.Equal(ManualFullTraceCommandResult.CommandPending, registry.RequestStart());
        Assert.Equal(ManualFullTraceCommandResult.CommandPending, registry.RequestStop());
        Assert.True(registration.TryTakeCommand(out var command));
        Assert.Equal(ManualFullTraceCommand.Start, command);
        Assert.Equal(ManualFullTraceCommand.None, registry.PendingCommand);
        Assert.Equal(3, registry.Revision);
        Assert.False(registration.TryTakeCommand(out command));
        Assert.Equal(ManualFullTraceCommand.None, command);

        registration.Publish(Active(ManualFullTraceState.Arming));
        Assert.Equal(ManualFullTraceCommandResult.InvalidState, registry.RequestStart());
        Assert.Equal(ManualFullTraceCommandResult.Accepted, registry.RequestStop());
        Assert.True(registration.TryTakeCommand(out command));
        Assert.Equal(ManualFullTraceCommand.Stop, command);

        registration.Publish(Complete());
        Assert.Equal(ManualFullTraceCommandResult.Accepted, registry.RequestStart());
    }

    [Fact]
    public void StatusRejectsPathsAndContradictoryTerminalEvidence()
    {
        Assert.Throws<ArgumentException>(() => new ManualFullTraceStatus(
            ManualFullTraceState.Recording,
            TimeSpan.Zero,
            0,
            0,
            0,
            0,
            0,
            false,
            ManualFullTraceResult.None,
            "private/session-1",
            storesLost: false));
        Assert.Throws<ArgumentException>(() => new ManualFullTraceStatus(
            ManualFullTraceState.Complete,
            TimeSpan.FromSeconds(1),
            1,
            1,
            320,
            1,
            0,
            false,
            ManualFullTraceResult.UserStopped,
            "session-0000000000000001",
            storesLost: false));
        Assert.Throws<ArgumentException>(() => new ManualFullTraceStatus(
            ManualFullTraceState.Incomplete,
            TimeSpan.FromSeconds(1),
            1,
            1,
            320,
            1,
            0,
            true,
            ManualFullTraceResult.WriteFailed,
            "session-0000000000000001",
            storesLost: false));
    }

    [Fact]
    public void RegistryRejectsCrossThreadControlAndProducerAccess()
    {
        var registry = new ManualFullTraceControlRegistry();
        using var registration = registry.Register();
        Exception? readFailure = null;
        Exception? producerFailure = null;
        var thread = new Thread(() =>
        {
            try { _ = registry.Status; }
            catch (Exception exception) { readFailure = exception; }
            try { registration.Publish(Active(ManualFullTraceState.Arming)); }
            catch (Exception exception) { producerFailure = exception; }
        });

        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(2)));
        Assert.IsType<InvalidOperationException>(readFailure);
        Assert.IsType<InvalidOperationException>(producerFailure);
    }

    [Fact]
    public void RejectedCrossThreadDisposeRetainsOwnerHandleForSafeRelease()
    {
        var registry = new ManualFullTraceControlRegistry();
        var registration = registry.Register();
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { registration.Dispose(); }
            catch (Exception exception) { failure = exception; }
        });

        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(2)));
        Assert.IsType<InvalidOperationException>(failure);

        registration.Dispose();
        using var replacement = registry.Register();
        Assert.Equal(ManualFullTraceState.Idle, registry.Status.State);
    }

    private static ManualFullTraceStatus Active(ManualFullTraceState state) => new(
        state,
        TimeSpan.FromSeconds(1),
        3,
        0,
        0,
        0,
        0,
        false,
        ManualFullTraceResult.None,
        "session-0000000000000001",
        storesLost: false);

    private static ManualFullTraceStatus Complete() => new(
        ManualFullTraceState.Complete,
        TimeSpan.FromSeconds(2),
        3,
        3,
        960,
        1,
        0,
        true,
        ManualFullTraceResult.UserStopped,
        "session-0000000000000001",
        storesLost: false);
}
