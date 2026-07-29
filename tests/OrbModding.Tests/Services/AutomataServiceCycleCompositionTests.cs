using System;
using OrbAutomata;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutomataServiceCycleCompositionTests
{
    [Fact]
    public void ServiceCycleHostStartupContainsOnlyRecoverableFailures()
    {
        Assert.True(AutomataServiceCycleComposition.IsContainedStartupFailure(
            new InvalidOperationException("recoverable")));
        Assert.False(AutomataServiceCycleComposition.IsContainedStartupFailure(
            new StackOverflowException()));
        Assert.False(AutomataServiceCycleComposition.IsContainedStartupFailure(
            new OutOfMemoryException()));
        Assert.False(AutomataServiceCycleComposition.IsContainedStartupFailure(
            new AccessViolationException()));
    }
}
