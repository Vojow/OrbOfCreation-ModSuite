using System;
using OrbAutomata;
using Xunit;

namespace OrbModding.Tests.Services.AutoHarvest.Native;

public sealed class AutoHarvestNativeFailureTests
{
    [Fact]
    public void FailureRejectsNoneUnknownKindsAndUnknownScopes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AutoHarvestNativeFailure.Create(
            AutoHarvestRuntimeFailureKind.None,
            AutoHarvestRuntimeFailureScope.Pair));
        Assert.Throws<ArgumentOutOfRangeException>(() => AutoHarvestNativeFailure.Create(
            (AutoHarvestRuntimeFailureKind)99,
            AutoHarvestRuntimeFailureScope.Pair));
        Assert.Throws<ArgumentOutOfRangeException>(() => AutoHarvestNativeFailure.Create(
            AutoHarvestRuntimeFailureKind.Retryable,
            (AutoHarvestRuntimeFailureScope)99));
    }

    [Fact]
    public void FailedResolutionRequiresTypedFailureEvidence()
    {
        Assert.Throws<ArgumentException>(() => AutoHarvestPairResolution.Failed(default));

        var failure = AutoHarvestNativeFailure.Create(
            AutoHarvestRuntimeFailureKind.Contract,
            AutoHarvestRuntimeFailureScope.Feature);
        var resolution = AutoHarvestPairResolution.Failed(failure);

        Assert.False(resolution.Succeeded);
        Assert.Equal(AutoHarvestRuntimeFailureKind.Contract, resolution.Failure.Kind);
        Assert.Equal(AutoHarvestRuntimeFailureScope.Feature, resolution.Failure.Scope);
    }
}
