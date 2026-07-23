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
            () => Create("buy", calls),
            () => Create("cast", calls),
            () => Create("concept", calls),
            () => Create("spell-level", calls));
        registry.Tick(0.25f);

        Assert.Equal(AutomataProductionComposition.FullServiceCount, registry.Count);
        Assert.Equal(new[]
        {
            "harvest.create",
            "buy.create",
            "cast.create",
            "concept.create",
            "spell-level.create",
            "harvest.tick",
            "buy.tick",
            "cast.tick",
            "concept.tick",
            "spell-level.tick",
        }, calls);
    }

    [Fact]
    public void FailedOptionalAutoHarvestConstructionLeavesTheFourCoreServicesRunnable()
    {
        var calls = new List<string>();
        using var registry = new AutomataServiceRegistry();

        AutomataProductionComposition.Register(
            registry,
            () => null,
            () => Create("buy", calls),
            () => Create("cast", calls),
            () => Create("concept", calls),
            () => Create("spell-level", calls));
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
            () => Create("buy", new List<string>()),
            () => Create("cast", new List<string>()),
            () => Create("concept", new List<string>()),
            () => Create("spell-level", new List<string>())));
    }

    [Fact]
    public void AutoHarvestStartupContainsOnlyRecoverableFailures()
    {
        Assert.True(AutoHarvestProductionComposition.IsContainedStartupFailure(
            new InvalidOperationException("recoverable")));
        Assert.False(AutoHarvestProductionComposition.IsContainedStartupFailure(
            new StackOverflowException()));
        Assert.False(AutoHarvestProductionComposition.IsContainedStartupFailure(
            new OutOfMemoryException()));
        Assert.False(AutoHarvestProductionComposition.IsContainedStartupFailure(
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
