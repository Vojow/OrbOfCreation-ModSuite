using OrbModding.Common.Runtime;
using Xunit;

namespace OrbModding.Tests.Runtime.Diagnostics;

public sealed class DiagnosticsBundleRegistryTests
{
    [Fact]
    public void OnePendingRequestIsOwnedByOneProducer()
    {
        var registry = new DiagnosticsBundleRegistry();
        Assert.Equal(DiagnosticsBundleRequestResult.Unavailable, registry.RequestBundle());
        Assert.True(registry.TryRegister(out var producer));
        Assert.NotNull(producer);

        Assert.Equal(DiagnosticsBundleRequestResult.Accepted, registry.RequestBundle());
        Assert.Equal(DiagnosticsBundleRequestResult.RequestPending, registry.RequestBundle());
        Assert.True(producer!.TryTakeRequest());
        Assert.False(producer.TryTakeRequest());
        Assert.True(registry.BundleRequested);
        Assert.Equal(DiagnosticsBundleRequestResult.RequestPending, registry.RequestBundle());
        Assert.True(producer.Publish(new DiagnosticsBundleStatus(
            DiagnosticsBundleState.Written,
            "/fixture/bundle.zip",
            123)));
        Assert.Equal(DiagnosticsBundleState.Written, registry.Status.State);
        Assert.False(registry.BundleRequested);

        producer.Dispose();
        Assert.Equal(DiagnosticsBundleState.Unavailable, registry.Status.State);
    }
}
