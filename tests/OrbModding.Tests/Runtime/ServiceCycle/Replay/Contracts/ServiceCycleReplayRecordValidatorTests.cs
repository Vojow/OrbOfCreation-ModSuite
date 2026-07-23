using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Replay.Contracts;

public sealed class ServiceCycleReplayRecordValidatorTests
{
    [Fact]
    public void NestedReadonlyValueRecordWithReviewedScalarsAndEnumIsAccepted()
    {
        var result = ServiceCycleReplayRecordValidator.Validate<AcceptedCycleInput>();

        Assert.True(result.IsValid);
        Assert.Equal(7, result.FlattenedScalarCount);
        Assert.Equal(41, result.InlineBytes);
        Assert.True(result.LayoutBytes >= result.InlineBytes);
        ServiceCycleReplayRecordValidator.EnsureValid<AcceptedCycleInput>();
    }

    [Fact]
    public void EmptyReadonlyValueRecordIsRejectedRatherThanBecomingAnUnreviewedMarker()
    {
        var result = ServiceCycleReplayRecordValidator.Validate<EmptyRecord>();

        Assert.False(result.IsValid);
        Assert.Equal(ServiceCycleReplayRecordViolationCode.EmptyValueRecord, result.Code);
    }

    [Theory]
    [InlineData(typeof(string), ServiceCycleReplayRecordViolationCode.String)]
    [InlineData(typeof(object), ServiceCycleReplayRecordViolationCode.Object)]
    [InlineData(typeof(IComparable), ServiceCycleReplayRecordViolationCode.Interface)]
    [InlineData(typeof(Action), ServiceCycleReplayRecordViolationCode.Delegate)]
    [InlineData(typeof(int[]), ServiceCycleReplayRecordViolationCode.ArrayOrCollection)]
    [InlineData(typeof(List<int>), ServiceCycleReplayRecordViolationCode.ArrayOrCollection)]
    [InlineData(typeof(IntPtr), ServiceCycleReplayRecordViolationCode.HandleOrPointer)]
    [InlineData(typeof(RuntimeTypeHandle), ServiceCycleReplayRecordViolationCode.HandleOrPointer)]
    [InlineData(typeof(int?), ServiceCycleReplayRecordViolationCode.Nullable)]
    [InlineData(typeof(Span<int>), ServiceCycleReplayRecordViolationCode.ByRefLike)]
    [InlineData(typeof(DateTime), ServiceCycleReplayRecordViolationCode.AmbientSource)]
    [InlineData(typeof(Random), ServiceCycleReplayRecordViolationCode.AmbientSource)]
    [InlineData(typeof(WakePolicy), ServiceCycleReplayRecordViolationCode.NativeOrRuntimeType)]
    [InlineData(typeof(FeatureRuntimeDto), ServiceCycleReplayRecordViolationCode.MissingReplayRecordMarker)]
    [InlineData(typeof(Guid), ServiceCycleReplayRecordViolationCode.UnreviewedFrameworkValueType)]
    public void ForbiddenRootShapesFailClosedWithStableCode(
        Type type,
        ServiceCycleReplayRecordViolationCode expected)
    {
        var result = ServiceCycleReplayRecordValidator.Validate(type);

        Assert.False(result.IsValid);
        Assert.Equal(expected, result.Code);
        Assert.Equal(type, result.RejectedType);
    }

    [Theory]
    [InlineData(typeof(WithString), ServiceCycleReplayRecordViolationCode.String)]
    [InlineData(typeof(WithObject), ServiceCycleReplayRecordViolationCode.Object)]
    [InlineData(typeof(WithInterface), ServiceCycleReplayRecordViolationCode.Interface)]
    [InlineData(typeof(WithDelegate), ServiceCycleReplayRecordViolationCode.Delegate)]
    [InlineData(typeof(WithArray), ServiceCycleReplayRecordViolationCode.ArrayOrCollection)]
    [InlineData(typeof(WithCollection), ServiceCycleReplayRecordViolationCode.ArrayOrCollection)]
    [InlineData(typeof(WithNullable), ServiceCycleReplayRecordViolationCode.Nullable)]
    [InlineData(typeof(WithHandle), ServiceCycleReplayRecordViolationCode.HandleOrPointer)]
    [InlineData(typeof(WithAmbientClock), ServiceCycleReplayRecordViolationCode.AmbientSource)]
    [InlineData(typeof(WithRuntimeType), ServiceCycleReplayRecordViolationCode.NativeOrRuntimeType)]
    [InlineData(typeof(WithReferenceCycle), ServiceCycleReplayRecordViolationCode.ReferenceType)]
    [InlineData(typeof(WithFrameworkValue), ServiceCycleReplayRecordViolationCode.UnreviewedFrameworkValueType)]
    [InlineData(typeof(WithStaticStorage), ServiceCycleReplayRecordViolationCode.StaticStorage)]
    [InlineData(typeof(WithUnmarkedNestedRecord), ServiceCycleReplayRecordViolationCode.MissingReplayRecordMarker)]
    public void ForbiddenNestedGraphsFailClosedAtAnyVisibility(
        Type type,
        ServiceCycleReplayRecordViolationCode expected)
    {
        var result = ServiceCycleReplayRecordValidator.Validate(type);

        Assert.False(result.IsValid);
        Assert.Equal(expected, result.Code);
        Assert.True(result.FieldOrdinal > 0);
    }

    [Fact]
    public void ConstructedGenericRecordIsRejectedBeforeItsApparentlySafeFieldIsTrusted()
    {
        var result = ServiceCycleReplayRecordValidator.Validate<GenericRecord<int>>();

        Assert.False(result.IsValid);
        Assert.Equal(ServiceCycleReplayRecordViolationCode.OpenOrConstructedGeneric, result.Code);
    }

    [Fact]
    public void FeatureValueRecordRequiresExplicitReplayOptInWhileOptedInEquivalentIsAccepted()
    {
        var unmarked = ServiceCycleReplayRecordValidator.Validate(typeof(FeatureRuntimeDto));
        var optedIn = ServiceCycleReplayRecordValidator.Validate<OptedInFeatureDto>();

        Assert.False(unmarked.IsValid);
        Assert.Equal(ServiceCycleReplayRecordViolationCode.MissingReplayRecordMarker, unmarked.Code);
        Assert.True(optedIn.IsValid);
    }

    [Fact]
    public void MutableAndExplicitLayoutValueTypesAreRejected()
    {
        Assert.Equal(
            ServiceCycleReplayRecordViolationCode.RootMustBeReadonlyRecord,
            ServiceCycleReplayRecordValidator.Validate<MutableRecord>().Code);
        Assert.Equal(
            ServiceCycleReplayRecordViolationCode.ExplicitOrUnmanagedLayout,
            ServiceCycleReplayRecordValidator.Validate<ExplicitLayoutRecord>().Code);
        Assert.Equal(
            ServiceCycleReplayRecordViolationCode.ExplicitOrUnmanagedLayout,
            ServiceCycleReplayRecordValidator.Validate<HugeSequentialLayoutRecord>().Code);
    }

    [Fact]
    public void SequentialPackingCannotBypassTheConservativeLayoutBound()
    {
        var compact = ServiceCycleReplayRecordValidator.Validate<CompactPackRecord>();
        var runtimeDefault = ServiceCycleReplayRecordValidator.Validate<RuntimeDefaultPackRecord>();

        Assert.True(compact.IsValid);
        Assert.True(runtimeDefault.IsValid);
        Assert.True(compact.LayoutBytes >= Unsafe.SizeOf<CompactPackRecord>());
        Assert.True(runtimeDefault.LayoutBytes >= Unsafe.SizeOf<RuntimeDefaultPackRecord>());
    }

    [Fact]
    public void ExcessivelyDeepOtherwiseReadonlyGraphIsRejectedAtTheFixedBoundary()
    {
        var result = ServiceCycleReplayRecordValidator.Validate<Depth17>();

        Assert.False(result.IsValid);
        Assert.Equal(ServiceCycleReplayRecordViolationCode.MaximumDepthExceeded, result.Code);
        Assert.Equal(ServiceCycleReplayRecordValidator.MaximumDepth + 1, result.Depth);
    }

    [Fact]
    public void PrimitiveAndEnumRootsAreNotMistakenForDetachedRecords()
    {
        Assert.Equal(
            ServiceCycleReplayRecordViolationCode.RootMustBeReadonlyRecord,
            ServiceCycleReplayRecordValidator.Validate(typeof(int)).Code);
        Assert.Equal(
            ServiceCycleReplayRecordViolationCode.RootMustBeReadonlyRecord,
            ServiceCycleReplayRecordValidator.Validate(typeof(SampleMode)).Code);
    }

    [Fact]
    public void RepeatedNestedOccurrencesCountEveryFlattenedScalar()
    {
        var result = ServiceCycleReplayRecordValidator.Validate<IntBranch9>();

        Assert.False(result.IsValid);
        Assert.Equal(ServiceCycleReplayRecordViolationCode.MaximumFlattenedScalarCountExceeded, result.Code);
        Assert.Equal(ServiceCycleReplayRecordValidator.MaximumFlattenedScalarCount, result.FlattenedScalarCount);
    }

    [Fact]
    public void RepeatedWideScalarsRespectTheIndependentInlineByteBound()
    {
        var result = ServiceCycleReplayRecordValidator.Validate<DecimalBranch8>();

        Assert.False(result.IsValid);
        Assert.Equal(ServiceCycleReplayRecordViolationCode.MaximumInlineBytesExceeded, result.Code);
        Assert.Equal(ServiceCycleReplayRecordValidator.MaximumInlineBytes, result.InlineBytes);
        Assert.True(result.FlattenedScalarCount <= ServiceCycleReplayRecordValidator.MaximumFlattenedScalarCount);
    }

    [Fact]
    public void DefaultLayoutPaddingCannotBypassTheInlineMemoryBound()
    {
        var actualLayoutBytes = Unsafe.SizeOf<PaddedOverflow>();
        Assert.True(actualLayoutBytes > ServiceCycleReplayRecordValidator.MaximumInlineBytes);

        var result = ServiceCycleReplayRecordValidator.Validate<PaddedOverflow>();

        Assert.False(result.IsValid);
        Assert.Equal(ServiceCycleReplayRecordViolationCode.MaximumInlineBytesExceeded, result.Code);
        Assert.True(result.LayoutBytes >= actualLayoutBytes);
        Assert.True(result.LayoutBytes > ServiceCycleReplayRecordValidator.MaximumInlineBytes);
        Assert.True(result.InlineBytes < ServiceCycleReplayRecordValidator.MaximumInlineBytes);
        Assert.True(result.FlattenedScalarCount <= ServiceCycleReplayRecordValidator.MaximumFlattenedScalarCount);
    }

    [Fact]
    public void MultipleDefectsAlwaysReportTheFirstDeclarationOrdinal()
    {
        for (var iteration = 0; iteration < 20; iteration++)
        {
            var result = ServiceCycleReplayRecordValidator.Validate(typeof(MultipleDefects));
            Assert.False(result.IsValid);
            Assert.Equal(ServiceCycleReplayRecordViolationCode.String, result.Code);
            Assert.Equal(1, result.FieldOrdinal);
        }
    }

    [Fact]
    public void EnsureValidReportsTheStableFailureCode()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            ServiceCycleReplayRecordValidator.EnsureValid<WithString>);

        Assert.Contains(((int)ServiceCycleReplayRecordViolationCode.String).ToString(), exception.Message);
        Assert.Contains(nameof(ServiceCycleReplayRecordViolationCode.String), exception.Message);
    }

    private enum SampleMode : short { First = 1, Second = 2 }
    private readonly record struct NestedValues(long Count, double Weight, decimal Limit) : IServiceCycleReplayRecord;
    private readonly record struct AcceptedCycleInput(
        bool Enabled,
        uint Generation,
        char Marker,
        SampleMode Mode,
        NestedValues Values) : IServiceCycleReplayRecord;
    private readonly record struct EmptyRecord : IServiceCycleReplayRecord;
    private readonly record struct WithString(string Value) : IServiceCycleReplayRecord;
    private readonly record struct WithObject(object Value) : IServiceCycleReplayRecord;
    private readonly record struct WithInterface(IComparable Value) : IServiceCycleReplayRecord;
    private readonly record struct WithDelegate(Action Value) : IServiceCycleReplayRecord;
    private readonly record struct WithArray(int[] Value) : IServiceCycleReplayRecord;
    private readonly record struct WithCollection(List<int> Value) : IServiceCycleReplayRecord;
    private readonly record struct WithNullable(int? Value) : IServiceCycleReplayRecord;
    private readonly record struct WithHandle(IntPtr Value) : IServiceCycleReplayRecord;
    private readonly record struct WithAmbientClock(DateTime Value) : IServiceCycleReplayRecord;
    private readonly record struct WithRuntimeType(WakePolicy Value) : IServiceCycleReplayRecord;
    private readonly record struct WithReferenceCycle(ReferenceCycleNode Value) : IServiceCycleReplayRecord;
    private readonly record struct WithFrameworkValue(Guid Value) : IServiceCycleReplayRecord;
    private readonly record struct WithStaticStorage(int Value) : IServiceCycleReplayRecord
    {
        private static readonly object Storage = new();
        public static object CurrentStorage => Storage;
    }
    private readonly record struct UnmarkedNestedRecord(int Value);
    private readonly record struct WithUnmarkedNestedRecord(UnmarkedNestedRecord Value) : IServiceCycleReplayRecord;
    private readonly record struct FeatureRuntimeDto(int Value);
    private readonly record struct OptedInFeatureDto(int Value) : IServiceCycleReplayRecord;
    private readonly record struct MultipleDefects(string First, object Second) : IServiceCycleReplayRecord;
    private sealed class ReferenceCycleNode { public ReferenceCycleNode? Next { get; set; } }
    private readonly record struct GenericRecord<T>(T Value) : IServiceCycleReplayRecord where T : struct;
    private struct MutableRecord : IServiceCycleReplayRecord { public int Value { get; set; } }

    [StructLayout(LayoutKind.Explicit)]
    private readonly struct ExplicitLayoutRecord : IServiceCycleReplayRecord
    {
        [FieldOffset(0)] public readonly int Value;
    }

    [StructLayout(LayoutKind.Sequential, Size = 1_000_000)]
    private readonly struct HugeSequentialLayoutRecord : IServiceCycleReplayRecord
    {
        public readonly int Value;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private readonly struct CompactPackRecord : IServiceCycleReplayRecord
    {
        public readonly byte Marker;
        public readonly long Value;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private readonly struct RuntimeDefaultPackRecord : IServiceCycleReplayRecord
    {
        public readonly byte Marker;
        public readonly long Value;
    }

    private readonly record struct Depth0(int Value) : IServiceCycleReplayRecord;
    private readonly record struct Depth1(Depth0 Value) : IServiceCycleReplayRecord;
    private readonly record struct Depth2(Depth1 Value) : IServiceCycleReplayRecord;
    private readonly record struct Depth3(Depth2 Value) : IServiceCycleReplayRecord;
    private readonly record struct Depth4(Depth3 Value) : IServiceCycleReplayRecord;
    private readonly record struct Depth5(Depth4 Value) : IServiceCycleReplayRecord;
    private readonly record struct Depth6(Depth5 Value) : IServiceCycleReplayRecord;
    private readonly record struct Depth7(Depth6 Value) : IServiceCycleReplayRecord;
    private readonly record struct Depth8(Depth7 Value) : IServiceCycleReplayRecord;
    private readonly record struct Depth9(Depth8 Value) : IServiceCycleReplayRecord;
    private readonly record struct Depth10(Depth9 Value) : IServiceCycleReplayRecord;
    private readonly record struct Depth11(Depth10 Value) : IServiceCycleReplayRecord;
    private readonly record struct Depth12(Depth11 Value) : IServiceCycleReplayRecord;
    private readonly record struct Depth13(Depth12 Value) : IServiceCycleReplayRecord;
    private readonly record struct Depth14(Depth13 Value) : IServiceCycleReplayRecord;
    private readonly record struct Depth15(Depth14 Value) : IServiceCycleReplayRecord;
    private readonly record struct Depth16(Depth15 Value) : IServiceCycleReplayRecord;
    private readonly record struct Depth17(Depth16 Value) : IServiceCycleReplayRecord;

    private readonly record struct IntLeaf(int Value) : IServiceCycleReplayRecord;
    private readonly record struct IntBranch1(IntLeaf Left, IntLeaf Right) : IServiceCycleReplayRecord;
    private readonly record struct IntBranch2(IntBranch1 Left, IntBranch1 Right) : IServiceCycleReplayRecord;
    private readonly record struct IntBranch3(IntBranch2 Left, IntBranch2 Right) : IServiceCycleReplayRecord;
    private readonly record struct IntBranch4(IntBranch3 Left, IntBranch3 Right) : IServiceCycleReplayRecord;
    private readonly record struct IntBranch5(IntBranch4 Left, IntBranch4 Right) : IServiceCycleReplayRecord;
    private readonly record struct IntBranch6(IntBranch5 Left, IntBranch5 Right) : IServiceCycleReplayRecord;
    private readonly record struct IntBranch7(IntBranch6 Left, IntBranch6 Right) : IServiceCycleReplayRecord;
    private readonly record struct IntBranch8(IntBranch7 Left, IntBranch7 Right) : IServiceCycleReplayRecord;
    private readonly record struct IntBranch9(IntBranch8 Left, IntBranch8 Right) : IServiceCycleReplayRecord;

    private readonly record struct DecimalLeaf(decimal Value) : IServiceCycleReplayRecord;
    private readonly record struct DecimalBranch1(DecimalLeaf Left, DecimalLeaf Right) : IServiceCycleReplayRecord;
    private readonly record struct DecimalBranch2(DecimalBranch1 Left, DecimalBranch1 Right) : IServiceCycleReplayRecord;
    private readonly record struct DecimalBranch3(DecimalBranch2 Left, DecimalBranch2 Right) : IServiceCycleReplayRecord;
    private readonly record struct DecimalBranch4(DecimalBranch3 Left, DecimalBranch3 Right) : IServiceCycleReplayRecord;
    private readonly record struct DecimalBranch5(DecimalBranch4 Left, DecimalBranch4 Right) : IServiceCycleReplayRecord;
    private readonly record struct DecimalBranch6(DecimalBranch5 Left, DecimalBranch5 Right) : IServiceCycleReplayRecord;
    private readonly record struct DecimalBranch7(DecimalBranch6 Left, DecimalBranch6 Right) : IServiceCycleReplayRecord;
    private readonly record struct DecimalBranch8(DecimalBranch7 Left, DecimalBranch7 Right) : IServiceCycleReplayRecord;

    private readonly record struct PaddedLeaf(byte Head, decimal Value, byte Tail) : IServiceCycleReplayRecord;
    private readonly record struct PaddedBranch1(PaddedLeaf Left, PaddedLeaf Right) : IServiceCycleReplayRecord;
    private readonly record struct PaddedBranch2(PaddedBranch1 Left, PaddedBranch1 Right) : IServiceCycleReplayRecord;
    private readonly record struct PaddedBranch3(PaddedBranch2 Left, PaddedBranch2 Right) : IServiceCycleReplayRecord;
    private readonly record struct PaddedBranch4(PaddedBranch3 Left, PaddedBranch3 Right) : IServiceCycleReplayRecord;
    private readonly record struct PaddedBranch5(PaddedBranch4 Left, PaddedBranch4 Right) : IServiceCycleReplayRecord;
    private readonly record struct PaddedBranch6(PaddedBranch5 Left, PaddedBranch5 Right) : IServiceCycleReplayRecord;
    private readonly record struct PaddedOverflow(PaddedBranch6 Values, byte Tail) : IServiceCycleReplayRecord;
}
