using System;
using System.Collections.Generic;
using OrbAutomata;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutomataServiceRegistryTests
{
    [Fact]
    public void LifecycleOperationsPreserveRegistrationOrder()
    {
        var calls = new List<string>();
        var registry = new AutomataServiceRegistry();
        var first = registry.Register(new RecordingService("buy", calls));
        var second = registry.Register(new RecordingService("cast", calls));

        registry.Tick(0.25f);
        registry.CancelPreparedWork();
        registry.InvalidateLifecycle();
        registry.Dispose();

        Assert.Equal(1, first.TickCount);
        Assert.Equal(1, second.TickCount);
        Assert.Equal(0.25f, first.LastTickDelta);
        Assert.Equal(0.25f, second.LastTickDelta);
        Assert.Equal(new[]
        {
            "buy.tick",
            "cast.tick",
            "buy.cancel",
            "cast.cancel",
            "buy.invalidate",
            "cast.invalidate",
            "buy.dispose",
            "cast.dispose",
        }, calls);
    }

    [Fact]
    public void RegisterReturnsTypedServiceAndRejectsDuplicateInstance()
    {
        using var registry = new AutomataServiceRegistry();
        var service = new RecordingService("harvest", new List<string>());

        var registered = registry.Register(service);

        Assert.Same(service, registered);
        Assert.Equal(1, registry.Count);
        Assert.Throws<InvalidOperationException>(() => registry.Register(service));
    }

    [Fact]
    public void RegistryHasAnExplicitBoundAndCannotBeReusedAfterDisposal()
    {
        var registry = new AutomataServiceRegistry(capacity: 3);
        for (var index = 0; index < registry.Capacity; index++)
            registry.Register(new RecordingService(index.ToString(), new List<string>()));

        Assert.Throws<InvalidOperationException>(() =>
            registry.Register(new RecordingService("overflow", new List<string>())));

        registry.Dispose();
        registry.Dispose();

        Assert.Equal(0, registry.Count);
        Assert.Throws<ObjectDisposedException>(() => registry.Tick(0.1f));
        Assert.Throws<ObjectDisposedException>(() => registry.InvalidateLifecycle());
        Assert.Throws<ObjectDisposedException>(() =>
            registry.Register(new RecordingService("late", new List<string>())));
    }

    [Fact]
    public void DefaultCapacityLeavesRoomForThePlannedServicePortfolio()
    {
        using var registry = new AutomataServiceRegistry();

        Assert.Equal(AutomataServiceRegistry.DefaultCapacity, registry.Capacity);
        Assert.True(registry.Capacity >= 20);
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

        public int TickCount { get; private set; }
        public float LastTickDelta { get; private set; }

        public void Tick(float unscaledDeltaTime)
        {
            TickCount++;
            LastTickDelta = unscaledDeltaTime;
            _calls.Add($"{_name}.tick");
        }

        public void CancelPreparedWork() => _calls.Add($"{_name}.cancel");

        public void InvalidateLifecycle() => _calls.Add($"{_name}.invalidate");

        public void Dispose() => _calls.Add($"{_name}.dispose");
    }
}
