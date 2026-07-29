using System;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class ServiceCycleProfileAllocationCapabilityTests
{
    [Fact]
    public void KnownAllocationMustAdvanceCounterByWitnessPayload()
    {
        var available = new ScriptedProfileAllocationCounter(new long[] { 1, 100, 400 });
        var insufficient = new ScriptedProfileAllocationCounter(new long[] { 1, 100, 355 });

        Assert.True(ServiceCycleProfileAllocationCapability.Probe(available).IsAvailable);
        Assert.False(ServiceCycleProfileAllocationCapability.Probe(insufficient).IsAvailable);
        Assert.Equal(3, available.ReadCount);
        Assert.Equal(3, insufficient.ReadCount);
    }

    [Fact]
    public void OnlyKnownPlatformAbsenceBecomesUnavailable()
    {
        var unavailable = new ScriptedProfileAllocationCounter(
            Array.Empty<long>(),
            terminalFailure: new PlatformNotSupportedException());
        var unexpected = new ScriptedProfileAllocationCounter(
            Array.Empty<long>(),
            terminalFailure: new InvalidOperationException("broken"));

        Assert.False(ServiceCycleProfileAllocationCapability.Probe(unavailable).IsAvailable);
        Assert.Throws<InvalidOperationException>(
            () => ServiceCycleProfileAllocationCapability.Probe(unexpected));
    }

    [Fact]
    public void BackwardCapabilityCounterIsAProbeFault()
    {
        var backwards = new ScriptedProfileAllocationCounter(new long[] { 1, 100, 99 });

        Assert.Throws<InvalidOperationException>(
            () => ServiceCycleProfileAllocationCapability.Probe(backwards));
    }

    [Fact]
    public void NegativeCapabilityEvidenceIsAProbeFault()
    {
        var negativeWarmup = new ScriptedProfileAllocationCounter(new long[] { -1 });
        var negativeBaseline = new ScriptedProfileAllocationCounter(new long[] { 0, -1 });
        var negativeResult = new ScriptedProfileAllocationCounter(new long[] { 0, 100, -1 });

        Assert.Throws<InvalidOperationException>(
            () => ServiceCycleProfileAllocationCapability.Probe(negativeWarmup));
        Assert.Throws<InvalidOperationException>(
            () => ServiceCycleProfileAllocationCapability.Probe(negativeBaseline));
        Assert.Throws<InvalidOperationException>(
            () => ServiceCycleProfileAllocationCapability.Probe(negativeResult));
    }
}
