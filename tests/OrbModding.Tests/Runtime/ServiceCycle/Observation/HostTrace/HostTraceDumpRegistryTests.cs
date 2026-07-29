using OrbModding.Common.Runtime.ServiceCycle.Observation.HostTrace.Control;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Observation.HostTrace;

public sealed class HostTraceDumpRegistryTests
{
    [Fact]
    public void WithoutAProducerTheAffordanceIsUnavailableRatherThanSilentlyIgnored()
    {
        var registry = new HostTraceDumpRegistry();

        Assert.Equal(HostTraceDumpState.Unavailable, registry.Status.State);
        Assert.Equal(HostTraceDumpRequestResult.Unavailable, registry.RequestDump());
        Assert.False(registry.DumpRequested);
    }

    [Fact]
    public void OneRequestIsTakenOnceAndItsOutcomeBecomesTheStatus()
    {
        var registry = new HostTraceDumpRegistry();
        Assert.True(registry.TryRegister(out var registration));
        using var producer = registration!;
        Assert.Equal(HostTraceDumpState.Idle, registry.Status.State);

        Assert.Equal(HostTraceDumpRequestResult.Accepted, registry.RequestDump());
        Assert.Equal(HostTraceDumpRequestResult.RequestPending, registry.RequestDump());
        Assert.True(registry.DumpRequested);

        Assert.True(producer.TryTakeRequest());
        Assert.False(producer.TryTakeRequest());
        Assert.False(registry.DumpRequested);

        var revision = registry.Revision;
        Assert.True(producer.Publish(new HostTraceDumpStatus(
            HostTraceDumpState.Written,
            writtenEvents: 12,
            bytesWritten: 3_456,
            overwrittenEvents: 7,
            "session-000000000000000a")));
        Assert.Equal(HostTraceDumpState.Written, registry.Status.State);
        Assert.Equal(7UL, registry.Status.OverwrittenEvents);
        Assert.NotEqual(revision, registry.Revision);
    }

    [Fact]
    public void ARemovedProducerLeavesNoStaleOutcomeBehind()
    {
        var registry = new HostTraceDumpRegistry();
        Assert.True(registry.TryRegister(out var registration));
        registration!.Publish(new HostTraceDumpStatus(
            HostTraceDumpState.Written, 12, 3_456, 0, "session-000000000000000a"));

        registration.Dispose();

        Assert.Equal(HostTraceDumpState.Unavailable, registry.Status.State);
        Assert.Equal(string.Empty, registry.Status.ArtifactName);
        Assert.True(registry.TryRegister(out _));
    }
}
