using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;
using static OrbModding.Tests.Runtime.ServiceCycle.TestSupport.ServiceCycleTypeSafetyFixtures;

namespace OrbModding.Tests.Runtime.ServiceCycle.Registration;

public sealed class ServiceCycleStateSurfaceTypeSafetyTests
{
    [Fact]
    public void FrameAndStateMustBeSealedAndStateCannotRetainFrame()
    {
        using var registry = new ServiceCycleRegistry(1);
        Assert.Throws<InvalidOperationException>(() => registry.Register(
            new TypeSafetyDefinition<UnsealedFrame, ImmutableConfig, SafeState, ImmutableAction>(
                new UnsealedFrame(), new SafeState()),
            new ImmutableConfig(1),
            new LifecycleGeneration(1)));
        Assert.Throws<InvalidOperationException>(() => registry.Register(
            new TypeSafetyDefinition<SafeFrame, ImmutableConfig, StateWithFrame, ImmutableAction>(
                new SafeFrame(), new StateWithFrame(new SafeFrame())),
            new ImmutableConfig(1),
            new LifecycleGeneration(1)));
    }

    [Fact]
    public void NeutralStorageIsNotRejectedByAdapterNamingConvention()
    {
        using var registry = new ServiceCycleRegistry(1);
        using var registration = registry.Register(
            new TypeSafetyDefinition<SafeFrame, ImmutableConfig, StateWithAdapterName, ImmutableAction>(
                new SafeFrame(), new StateWithAdapterName(new NeutralAdapter())),
            new ImmutableConfig(1),
            new LifecycleGeneration(1));
        Assert.NotNull(registration);
    }

    [Fact]
    public void FrameAndStateCannotExposeArraysOrCollectionProperties()
    {
        using var registry = new ServiceCycleRegistry(1);
        AssertFrameRejected(registry, new FrameWithPublicArray());
        Assert.Throws<InvalidOperationException>(() => registry.Register(
            new TypeSafetyDefinition<SafeFrame, ImmutableConfig, StateWithPublicCollection, ImmutableAction>(
                new SafeFrame(), new StateWithPublicCollection()),
            new ImmutableConfig(1),
            new LifecycleGeneration(1)));
    }

    [Fact]
    public void PublicAndProtectedBackingMemoryViewsAreRejected()
    {
        using var registry = new ServiceCycleRegistry(1);
        AssertFrameRejected(registry, new FrameWithPublicSpan());
        AssertFrameRejected(registry, new FrameWithProtectedReadOnlySpan());
        AssertFrameRejected(registry, new FrameWithPublicMemory());
        AssertFrameRejected(registry, new FrameWithPublicReadOnlyMemory());
    }

    [Fact]
    public void ComputedPropertiesCannotBypassGraphSafety()
    {
        using var registry = new ServiceCycleRegistry(1);
        AssertConfigurationRejected(registry, new ComputedCollectionConfig());
        Assert.Throws<InvalidOperationException>(() => registry.Register(
            new TypeSafetyDefinition<SafeFrame, ImmutableConfig, SafeState, ActionWithComputedFrame>(
                new SafeFrame(), new SafeState()),
            new ImmutableConfig(1),
            new LifecycleGeneration(1)));
        Assert.Throws<InvalidOperationException>(() => registry.Register(
            new TypeSafetyDefinition<SafeFrame, ImmutableConfig, SafeState, ActionWithComputedHandle>(
                new SafeFrame(), new SafeState()),
            new ImmutableConfig(1),
            new LifecycleGeneration(1)));
    }

    [Fact]
    public void PublicSettersAreRejectedWhileInternalMutationRemainsAllowed()
    {
        using var registry = new ServiceCycleRegistry(2);
        AssertConfigurationRejected(registry, new ComputedMutableConfig());
        AssertFrameRejected(registry, new FrameWithPublicSetter());
        using var registration = registry.Register(
            new TypeSafetyDefinition<FrameWithInternalSetter, ImmutableConfig, SafeState, ImmutableAction>(
                new FrameWithInternalSetter(), new SafeState()),
            new ImmutableConfig(1),
            new LifecycleGeneration(1));
        Assert.NotNull(registration);
    }

    private class UnsealedFrame { }
    private sealed class StateWithFrame
    {
        private readonly SafeFrame _frame;
        internal StateWithFrame(SafeFrame frame) => _frame = frame;
    }
    private sealed class StateWithAdapterName
    {
        private readonly NeutralAdapter _adapter;
        internal StateWithAdapterName(NeutralAdapter adapter) => _adapter = adapter;
    }
    private sealed class NeutralAdapter { }
    private sealed class FrameWithPublicArray
    {
        private readonly int[] _items = { 1 };
        public int[] Items => _items;
    }
    private sealed class StateWithPublicCollection
    {
        private readonly int[] _items = { 1 };
        public IReadOnlyList<int> Items => _items;
    }
    private sealed class FrameWithPublicSpan
    {
        private readonly int[] _items = { 1 };
        public Span<int> Items => _items;
    }
    private abstract class ProtectedReadOnlySpanSurface
    {
        private readonly int[] _items = { 1 };
        protected ReadOnlySpan<int> Items => _items;
    }
    private sealed class FrameWithProtectedReadOnlySpan : ProtectedReadOnlySpanSurface { }
    private sealed class FrameWithPublicMemory
    {
        private readonly int[] _items = { 1 };
        public Memory<int> Items => _items;
    }
    private sealed class FrameWithPublicReadOnlyMemory
    {
        private readonly int[] _items = { 1 };
        public ReadOnlyMemory<int> Items => _items;
    }
    private sealed class ComputedCollectionConfig
    {
        public IReadOnlyList<int> Values => new[] { 1 };
    }
    private sealed class ComputedMutableConfig
    {
        public int Value { get => 0; set { } }
    }
    private readonly struct ActionWithComputedFrame
    {
        public SafeFrame Frame => new();
    }
    private readonly struct ActionWithComputedHandle
    {
        public IntPtr Handle => IntPtr.Zero;
    }
    private sealed class FrameWithPublicSetter { public int Value { get; set; } }
    private sealed class FrameWithInternalSetter { internal int Value { get; set; } }
}
