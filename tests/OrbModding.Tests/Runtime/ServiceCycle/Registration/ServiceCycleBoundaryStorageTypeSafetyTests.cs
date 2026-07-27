using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;
using static OrbModding.Tests.Runtime.ServiceCycle.TestSupport.ServiceCycleTypeSafetyFixtures;

namespace OrbModding.Tests.Runtime.ServiceCycle.Registration;

public sealed class ServiceCycleBoundaryStorageTypeSafetyTests
{
    [Fact]
    public void PointerSizedHandlesSafeHandlesAndWeakReferencesAreRejected()
    {
        using var registry = new ServiceCycleRegistry(1);
        AssertConfigurationRejected(new IntPtrConfig(IntPtr.Zero));
        AssertConfigurationRejected(new UIntPtrConfig(UIntPtr.Zero));
        AssertConfigurationRejected(new GcHandleConfig(default));
        AssertConfigurationRejected(new SafeHandleConfig(null));
        AssertConfigurationRejected(new CriticalHandleConfig(null));
        AssertConfigurationRejected(new WeakReferenceConfig(new WeakReference(new object())));
        AssertConfigurationRejected(new GenericWeakReferenceConfig(
            new WeakReference<object>(new object())));
    }

    [Fact]
    public void ClosedGenericArgumentsCannotHideUnityTypes()
    {
        using var registry = new ServiceCycleRegistry(1);
        AssertConfigurationRejected(new WrappedUnityConfig(new GenericWrapper<UnityEngine.Object?>(null)));
    }

    private readonly struct IntPtrConfig
    {
        private readonly IntPtr _value;
        internal IntPtrConfig(IntPtr value) => _value = value;
    }
    private readonly struct UIntPtrConfig
    {
        private readonly UIntPtr _value;
        internal UIntPtrConfig(UIntPtr value) => _value = value;
    }
    private readonly struct GcHandleConfig
    {
        private readonly GCHandle _value;
        internal GcHandleConfig(GCHandle value) => _value = value;
    }
    private sealed class SafeHandleConfig
    {
        private readonly SafeFileHandle? _value;
        internal SafeHandleConfig(SafeFileHandle? value) => _value = value;
    }
    private sealed class CriticalHandleConfig
    {
        private readonly SyntheticCriticalHandle? _value;
        internal CriticalHandleConfig(SyntheticCriticalHandle? value) => _value = value;
    }
    private sealed class SyntheticCriticalHandle : CriticalHandle
    {
        internal SyntheticCriticalHandle() : base(IntPtr.Zero) { }
        public override bool IsInvalid => true;
        protected override bool ReleaseHandle() => true;
    }
    private sealed class WeakReferenceConfig
    {
        private readonly WeakReference _value;
        internal WeakReferenceConfig(WeakReference value) => _value = value;
    }
    private sealed class GenericWeakReferenceConfig
    {
        private readonly WeakReference<object> _value;
        internal GenericWeakReferenceConfig(WeakReference<object> value) => _value = value;
    }
    private readonly struct GenericWrapper<T>
    {
        private readonly T _value;
        internal GenericWrapper(T value) => _value = value;
    }
    private readonly struct WrappedUnityConfig
    {
        private readonly GenericWrapper<UnityEngine.Object?> _value;
        internal WrappedUnityConfig(GenericWrapper<UnityEngine.Object?> value) => _value = value;
    }
}
