using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;
using static OrbModding.Tests.Runtime.ServiceCycle.TestSupport.ServiceCycleTypeSafetyFixtures;

namespace OrbModding.Tests.Runtime.ServiceCycle.Registration;

public sealed class ServiceCycleMethodSurfaceTypeSafetyTests
{
    [Fact]
    public void PublicAndProtectedMethodsCannotExposeUnityOrHandleTypes()
    {
        using var registry = new ServiceCycleRegistry(1);
        AssertConfigurationRejected(registry, new ConfigWithPublicUnityReturn());
        AssertConfigurationRejected(registry, new ConfigWithProtectedUnityParameter());
        AssertConfigurationRejected(registry, new ConfigWithWrappedUnityReturn());
        AssertConfigurationRejected(registry, new ConfigWithWrappedHandleParameter());
    }

    [Fact]
    public void OpenEndedMethodAndConstructorSurfacesAreRejected()
    {
        using var registry = new ServiceCycleRegistry(1);
        AssertConfigurationRejected(registry, new ConfigWithObjectReturn());
        AssertConfigurationRejected(registry, new ConfigWithProviderReturn());
        AssertConfigurationRejected(registry, new ConfigWithObjectConstructor(new object()));
        AssertConfigurationRejected(registry, new ConfigWithArrayParameter());
        AssertConfigurationRejected(registry, new ConfigWithCollectionReturn());
        AssertConfigurationRejected(registry, new ConfigWithMemoryReturn());
        AssertConfigurationRejected(registry, new ConfigWithByRefParameter());
        AssertConfigurationRejected(registry, new ConfigWithOpenGenericMethod());
        AssertConfigurationRejected(registry, new ConfigWithDelegateReturn());
        AssertConfigurationRejected(registry, new ConfigWithDowncastableReturn());
    }

    [Fact]
    public void StronglyTypedNeutralDomainMethodsAndConstructorsAreAccepted()
    {
        using var registry = new ServiceCycleRegistry(1);
        using var registration = registry.Register(
            new TypeSafetyDefinition<SafeFrame, SafeNeutralMethodConfig, SafeState, ImmutableAction>(
                new SafeFrame(), new SafeState()),
            new SafeNeutralMethodConfig(new NeutralMethodValue(7)),
            new LifecycleGeneration(1));

        var snapshot = registration.Configuration.ReadLatest().Snapshot;
        Assert.Equal(7, snapshot.Select(new NeutralMethodValue(9)).Value);
        Assert.Equal(7, snapshot.ReadValue());
    }

    public interface IGameObjectProvider
    {
        UnityEngine.Object? ReadGameObject();
    }

    private sealed class ConfigWithPublicUnityReturn
    {
        public UnityEngine.Object? ReadNative() => null;
    }
    private abstract class ProtectedUnityParameterSurface
    {
        protected void AcceptNative(UnityEngine.Object? value) { }
    }
    private sealed class ConfigWithProtectedUnityParameter : ProtectedUnityParameterSurface { }
    private readonly struct GenericWrapper<T>
    {
        private readonly T _value;
        internal GenericWrapper(T value) => _value = value;
    }
    private sealed class ConfigWithWrappedUnityReturn
    {
        public GenericWrapper<UnityEngine.Object?> ReadNative() => new(null);
    }
    private sealed class ConfigWithWrappedHandleParameter
    {
        public void AcceptHandle(GenericWrapper<IntPtr> handle) { }
    }
    private sealed class ConfigWithObjectReturn
    {
        public object ReadNative() => new object();
    }
    private sealed class ConfigWithProviderReturn
    {
        public IGameObjectProvider? ReadProvider() => null;
    }
    private sealed class ConfigWithObjectConstructor
    {
        private readonly int _value = 1;
        public ConfigWithObjectConstructor(object source) { }
        internal int Value => _value;
    }
    private sealed class ConfigWithArrayParameter
    {
        public void Accept(int[] values) { }
    }
    private sealed class ConfigWithCollectionReturn
    {
        public IReadOnlyList<int> ReadValues() => Array.Empty<int>();
    }
    private sealed class ConfigWithMemoryReturn
    {
        public ReadOnlyMemory<int> ReadValues() => default;
    }
    private sealed class ConfigWithByRefParameter
    {
        public void Read(ref int value) { }
    }
    private sealed class ConfigWithOpenGenericMethod
    {
        public int Read<T>() => 0;
    }
    private sealed class ConfigWithDelegateReturn
    {
        public Action ReadCallback() => static () => { };
    }
    private class DowncastableMethodValue { }
    private sealed class ConfigWithDowncastableReturn
    {
        public DowncastableMethodValue ReadValue() => new();
    }
    private readonly struct NeutralMethodValue
    {
        public NeutralMethodValue(int value) => Value = value;
        public int Value { get; }
    }
    private sealed class SafeNeutralMethodConfig
    {
        private readonly NeutralMethodValue _value;
        public SafeNeutralMethodConfig(NeutralMethodValue value) => _value = value;
        public NeutralMethodValue Select(NeutralMethodValue fallback) => _value;
        public int ReadValue() => _value.Value;
    }
}
