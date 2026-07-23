using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
using System;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class ServiceCycleProfileBuildTests
{
    [Fact]
    public void ProfileBuildContainsValidatedProfileContracts()
    {
        var session = new ServiceCycleProfileSessionId(17);

        Assert.True(session.IsValid);
        Assert.Equal((ulong)17, session.Value);
        Assert.Equal(new ServiceCycleProfileSessionId(17), session);
        Assert.Equal(new ServiceCycleProfileSessionId(17).GetHashCode(), session.GetHashCode());
        Assert.False(default(ServiceCycleProfileSessionId).IsValid);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ServiceCycleProfileSessionId(0));
    }
}
