using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Tracing.Emission;

public sealed class ServiceCycleProjectionFingerprintTests
{
    [Fact]
    public void ProjectionFingerprintIsDeterministicAndOrderSensitive()
    {
        var first = Projection(reverse: false, changed: false);
        var same = Projection(reverse: false, changed: false);
        var reordered = Projection(reverse: true, changed: false);
        var changed = Projection(reverse: false, changed: true);

        var expected = ServiceCycleProjectionFingerprint.Compute(in first);

        Assert.Equal(3_899_848_411_340_931_822UL, expected);
        Assert.Equal(expected, ServiceCycleProjectionFingerprint.Compute(in same));
        Assert.NotEqual(expected, ServiceCycleProjectionFingerprint.Compute(in reordered));
        Assert.NotEqual(expected, ServiceCycleProjectionFingerprint.Compute(in changed));
    }

    private static ServiceStateProjectionSnapshot Projection(bool reverse, bool changed)
    {
        var buffer = new ServiceStateProjectionWriteBuffer(ServiceStateProjectionSnapshot.MaximumEntryCount);
        var builder = new ServiceStateProjectionBuilder(buffer);
        if (reverse)
        {
            builder.Add(new ServiceProjectionKey(2), ServiceProjectionValue.FromFloatingPoint(3.5));
            builder.Add(new ServiceProjectionKey(1), ServiceProjectionValue.FromInteger(changed ? 8 : 7));
        }
        else
        {
            builder.Add(new ServiceProjectionKey(1), ServiceProjectionValue.FromInteger(changed ? 8 : 7));
            builder.Add(new ServiceProjectionKey(2), ServiceProjectionValue.FromFloatingPoint(3.5));
        }

        return buffer.CreateSnapshot();
    }
}
