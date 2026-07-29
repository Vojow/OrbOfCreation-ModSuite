using System;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile.Control;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class PerformanceProfileControlRegistryTests
{
    [Fact]
    public void ControlAdmitsOneManualSessionAtATime()
    {
        var registry = new PerformanceProfileControlRegistry();

        Assert.Equal(PerformanceProfileControlState.Unavailable, registry.Status.State);
        Assert.Equal(PerformanceProfileCommandResult.Unavailable, registry.RequestStart());
        Assert.True(registry.TryRegister(out var registration));
        Assert.NotNull(registration);
        Assert.Equal(PerformanceProfileControlState.Idle, registry.Status.State);
        Assert.False(registry.TryRegister(out _));

        Assert.Equal(PerformanceProfileCommandResult.Accepted, registry.RequestStart());
        Assert.Equal(PerformanceProfileCommandResult.CommandPending, registry.RequestStop());
        Assert.True(registration!.TryTakeCommand(out var command));
        Assert.Equal(PerformanceProfileCommand.Start, command);

        registration.Publish(Recording());
        Assert.Equal(PerformanceProfileCommandResult.Accepted, registry.RequestStop());
        Assert.True(registration.TryTakeCommand(out command));
        Assert.Equal(PerformanceProfileCommand.Stop, command);

        registration.Publish(Complete());
        Assert.Equal(PerformanceProfileCommandResult.Accepted, registry.RequestStart());
        registration.Dispose();
        Assert.Equal(PerformanceProfileControlState.Unavailable, registry.Status.State);
    }

    [Fact]
    public void FaultedProfileRequiresRuntimeRestart()
    {
        var registry = new PerformanceProfileControlRegistry();
        using var registration = AssertRegistration(registry);
        registration.Publish(new PerformanceProfileControlStatus(
            PerformanceProfileControlState.Faulted,
            TimeSpan.FromSeconds(1),
            0,
            0,
            PerformanceProfileResult.InitializationFailed,
            string.Empty));

        Assert.Equal(PerformanceProfileCommandResult.InvalidState, registry.RequestStart());
        Assert.Equal(PerformanceProfileCommandResult.InvalidState, registry.RequestStop());
    }

    private static PerformanceProfileControlRegistration AssertRegistration(
        PerformanceProfileControlRegistry registry)
    {
        Assert.True(registry.TryRegister(out var registration));
        return Assert.IsType<PerformanceProfileControlRegistration>(registration);
    }

    private static PerformanceProfileControlStatus Recording() => new(
        PerformanceProfileControlState.Recording,
        TimeSpan.FromSeconds(1),
        0,
        0,
        PerformanceProfileResult.None,
        "session-0000000000000001");

    private static PerformanceProfileControlStatus Complete() => new(
        PerformanceProfileControlState.Complete,
        TimeSpan.FromSeconds(2),
        12,
        2048,
        PerformanceProfileResult.UserStopped,
        "session-0000000000000001");
}
