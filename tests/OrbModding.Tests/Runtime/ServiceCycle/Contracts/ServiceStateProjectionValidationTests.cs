using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Contracts;

public sealed class ServiceStateProjectionValidationTests
{
    [Fact]
    public void DuplicateKeyIsRejectedBeforeTheProjectionBufferChanges()
    {
        var buffer = new ServiceStateProjectionWriteBuffer(
            ServiceStateProjectionSnapshot.MaximumEntryCount);
        var builder = new ServiceStateProjectionBuilder(buffer);
        var key = new ServiceProjectionKey(1);
        builder.Add(key, ServiceProjectionValue.FromInteger(10));

        InvalidOperationException? exception = null;
        try
        {
            builder.Add(key, ServiceProjectionValue.FromInteger(99));
        }
        catch (InvalidOperationException ex)
        {
            exception = ex;
        }

        Assert.NotNull(exception);
        Assert.Contains("more than once", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, builder.Count);
        builder.Add(new ServiceProjectionKey(2), ServiceProjectionValue.FromInteger(20));
        var snapshot = buffer.CreateSnapshot();
        Assert.Equal(2, snapshot.Count);
        Assert.Equal(10L, snapshot.GetEntry(0).Value.Integer);
        Assert.Equal(20L, snapshot.GetEntry(1).Value.Integer);
    }

}
