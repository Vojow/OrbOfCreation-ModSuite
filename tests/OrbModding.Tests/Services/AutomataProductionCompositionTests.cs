using System;
using System.Collections.Generic;
using OrbAutomata;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutomataProductionCompositionTests
{
    [Fact]
    public void ProductionFactoriesAndRuntimeLifecyclePutAutoHarvestBeforeSiblingMutationDemand()
    {
        var calls = new List<string>();
        using var registry = new AutomataServiceRegistry();

        AutomataProductionComposition.Register(
            registry,
            () => Create("harvest", calls),
            () => Create("concept", calls));
        registry.Tick(0.25f);

        Assert.Equal(AutomataProductionComposition.FullServiceCount, registry.Count);
        Assert.Equal(new[]
        {
            "harvest.create",
            "concept.create",
            "harvest.tick",
            "concept.tick",
        }, calls);
    }

    [Fact]
    public void FailedOptionalAutoHarvestConstructionLeavesTheCoreServicesRunnable()
    {
        var calls = new List<string>();
        using var registry = new AutomataServiceRegistry();

        AutomataProductionComposition.Register(
            registry,
            () => null,
            () => Create("concept", calls));
        registry.Tick(0.25f);

        Assert.Equal(AutomataProductionComposition.CoreServiceCount, registry.Count);
        Assert.DoesNotContain(calls, item => item.StartsWith("harvest", StringComparison.Ordinal));
    }

    [Fact]
    public void ProductionCompositionRejectsARegistryWithAnUnknownPredecessor()
    {
        using var registry = new AutomataServiceRegistry();
        registry.Register(new RecordingService("unknown", new List<string>()));

        Assert.Throws<InvalidOperationException>(() => AutomataProductionComposition.Register(
            registry,
            () => Create("harvest", new List<string>()),
            () => Create("concept", new List<string>())));
    }

    [Fact]
    public void ServiceCycleHostStartupContainsOnlyRecoverableFailures()
    {
        Assert.True(AutomataServiceCycleProductionComposition.IsContainedStartupFailure(
            new InvalidOperationException("recoverable")));
        Assert.False(AutomataServiceCycleProductionComposition.IsContainedStartupFailure(
            new StackOverflowException()));
        Assert.False(AutomataServiceCycleProductionComposition.IsContainedStartupFailure(
            new OutOfMemoryException()));
        Assert.False(AutomataServiceCycleProductionComposition.IsContainedStartupFailure(
            new AccessViolationException()));
    }

    private static RecordingService Create(string name, List<string> calls)
    {
        calls.Add(name + ".create");
        return new RecordingService(name, calls);
    }

    private sealed class RecordingService : IAutomataService
    {
        private readonly string _name;
        private readonly List<string> _calls;

        public RecordingService(string name, List<string> calls)
        {
            _name = name;
            _calls = calls;
        }

        public void Tick(float unscaledDeltaTime) => _calls.Add(_name + ".tick");
        public void CancelPreparedWork() => _calls.Add(_name + ".cancel");
        public void InvalidateLifecycle() => _calls.Add(_name + ".invalidate");
        public void Dispose() => _calls.Add(_name + ".dispose");
    }
}
