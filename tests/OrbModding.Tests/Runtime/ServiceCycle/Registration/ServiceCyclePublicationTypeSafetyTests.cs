using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;
using static OrbModding.Tests.Runtime.ServiceCycle.TestSupport.ServiceCycleTypeSafetyFixtures;

namespace OrbModding.Tests.Runtime.ServiceCycle.Registration;

public sealed class ServiceCyclePublicationTypeSafetyTests
{
    [Fact]
    public void RegistrationAcceptsSealedStorageAndDeepReadonlyPublishedValues()
    {
        using var registry = new ServiceCycleRegistry(1);
        using var registration = registry.Register(
            new TypeSafetyDefinition<SafeFrame, ImmutableConfig, SafeState, ImmutableAction>(
                new SafeFrame(), new SafeState()),
            new ImmutableConfig(1),
            new LifecycleGeneration(1));

        Assert.Equal(1, registration.Configuration.ReadLatest().Snapshot.Value);
    }

    [Fact]
    public void CyclicSealedReadonlyPublicationGraphTerminatesAndIsAccepted()
    {
        using var registry = new ServiceCycleRegistry(1);
        using var registration = registry.Register(
            new TypeSafetyDefinition<SafeFrame, CyclicConfig, SafeState, ImmutableAction>(
                new SafeFrame(), new SafeState()),
            new CyclicConfig(null),
            new LifecycleGeneration(1));

        Assert.Null(registration.Configuration.ReadLatest().Snapshot.Next);
    }

    [Fact]
    public void MutableConfigurationAndActionShapesAreRejectedBeforeConstruction()
    {
        using var registry = new ServiceCycleRegistry(1);
        var mutableConfig = new TypeSafetyDefinition<SafeFrame, MutableConfig, SafeState, ImmutableAction>(
            new SafeFrame(), new SafeState());
        Assert.Throws<InvalidOperationException>(() => registry.Register(
            mutableConfig, new MutableConfig { Value = 1 }, new LifecycleGeneration(1)));
        Assert.Equal(0, mutableConfig.FrameCreates);

        var mutableAction = new TypeSafetyDefinition<SafeFrame, ImmutableConfig, SafeState, MutableAction>(
            new SafeFrame(), new SafeState());
        Assert.Throws<InvalidOperationException>(() => registry.Register(
            mutableAction, new ImmutableConfig(1), new LifecycleGeneration(1)));
        Assert.Equal(0, mutableAction.FrameCreates);
    }

    [Fact]
    public void ArraysDelegatesAndUnityTypesAreRejectedFromPublishedGraphs()
    {
        using var registry = new ServiceCycleRegistry(1);
        AssertConfigurationRejected(registry, new ArrayConfig(new[] { 1 }));
        AssertConfigurationRejected(registry, new DelegateConfig(() => { }));
        AssertConfigurationRejected(registry, new UnityConfig(null));
    }

    [Fact]
    public void StaticStorageIsRejectedButLiteralConstantsRemainSafe()
    {
        using var registry = new ServiceCycleRegistry(1);
        AssertConfigurationRejected(registry, new StaticUnityCacheConfig(1));

        using var registration = registry.Register(
            new TypeSafetyDefinition<SafeFrame, ConstantBearingConfig, SafeState, ImmutableAction>(
                new SafeFrame(), new SafeState()),
            new ConstantBearingConfig(1),
            new LifecycleGeneration(1));
        Assert.Equal(ConstantBearingConfig.SchemaVersion, registration.Configuration.ReadLatest().Snapshot.Value);
    }

    [Fact]
    public void ActionsCannotBeOrRecursivelyRetainTheirServiceFrame()
    {
        using var registry = new ServiceCycleRegistry(1);
        Assert.Throws<InvalidOperationException>(() => registry.Register(
            new TypeSafetyDefinition<SafeFrame, ImmutableConfig, SafeState, SafeFrame>(
                new SafeFrame(), new SafeState()),
            new ImmutableConfig(1),
            new LifecycleGeneration(1)));
        Assert.Throws<InvalidOperationException>(() => registry.Register(
            new TypeSafetyDefinition<SafeFrame, ImmutableConfig, SafeState, ActionWithFrame>(
                new SafeFrame(), new SafeState()),
            new ImmutableConfig(1),
            new LifecycleGeneration(1)));
    }

    private sealed class CyclicConfig
    {
        private readonly CyclicConfig? _next;
        internal CyclicConfig(CyclicConfig? next) => _next = next;
        public CyclicConfig? Next => _next;
    }
    private sealed class MutableConfig { internal int Value { get; set; } }
    private sealed class MutableAction { internal int Value { get; set; } }
    private readonly struct ArrayConfig
    {
        private readonly int[] _values;
        internal ArrayConfig(int[] values) => _values = values;
    }
    private readonly struct DelegateConfig
    {
        private readonly Action _action;
        internal DelegateConfig(Action action) => _action = action;
    }
    private sealed class UnityConfig
    {
        private readonly UnityEngine.Object? _value;
        internal UnityConfig(UnityEngine.Object? value) => _value = value;
    }
    private sealed class StaticUnityCacheConfig
    {
        private static UnityEngine.Object? _cache = null;
        private readonly int _value;
        internal StaticUnityCacheConfig(int value) => _value = value;
        internal int Value => _value;
        private static UnityEngine.Object? Cache => _cache;
    }
    private sealed class ConstantBearingConfig
    {
        public const int SchemaVersion = 1;
        private readonly int _value;
        internal ConstantBearingConfig(int value) => _value = value;
        internal int Value => _value;
    }
    private readonly struct ActionWithFrame
    {
        private readonly SafeFrame _frame;
        internal ActionWithFrame(SafeFrame frame) => _frame = frame;
    }
}
