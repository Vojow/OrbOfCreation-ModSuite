using OrbAutomata.Runtime.ServiceCycle.Profile;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class AutoHarvestProfileTemperatureTrackerTests
{
    [Fact]
    public void TracksColdWarmAndLifecycleRebindWithoutRelabeling()
    {
        var temperature = new AutoHarvestProfileTemperatureTracker();

        Assert.Equal(ServiceCycleProfileTemperature.ColdProcess, temperature.Current);
        temperature.InvalidateLifecycle();
        Assert.Equal(ServiceCycleProfileTemperature.ColdProcess, temperature.Current);
        Assert.True(temperature.TryComplete(ServiceCycleProfileTemperature.ColdProcess));
        Assert.Equal(ServiceCycleProfileTemperature.Warm, temperature.Current);

        temperature.ObserveUnexpectedDrift();
        Assert.Equal(ServiceCycleProfileTemperature.LifecycleRebind, temperature.Current);
        Assert.False(temperature.TryComplete(ServiceCycleProfileTemperature.Warm));
        Assert.Equal(ServiceCycleProfileTemperature.LifecycleRebind, temperature.Current);
        Assert.True(temperature.TryComplete(ServiceCycleProfileTemperature.LifecycleRebind));
        Assert.Equal(ServiceCycleProfileTemperature.Warm, temperature.Current);
    }
}
