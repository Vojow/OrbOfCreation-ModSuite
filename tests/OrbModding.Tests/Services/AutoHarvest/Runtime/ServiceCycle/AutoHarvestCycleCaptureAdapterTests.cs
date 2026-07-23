using System;
using System.Reflection;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using Xunit;

namespace OrbAutomata.Tests;

public sealed class AutoHarvestCycleCaptureAdapterTests
{
    [Fact]
    public void UnselectedPairsProduceNoNativeCapture()
    {
        var ownershipReads = 0;
        var contractCircuit = new AutoHarvestContractCircuit();
        var adapter = new AutoHarvestCycleCaptureAdapter(
            new AutoHarvestBindingResolver(
                TypedRegistryResolver.Shared,
                new AutoHarvestStaticContractAuditor(),
                contractCircuit),
            new AutoHarvestNativeStateReader(),
            new AutoHarvestNativeGateSet(),
            contractCircuit,
            () =>
            {
                ownershipReads++;
                return false;
            });
        var config = AutoHarvestConfigurationFactory.Create(
            masterEnabled: true,
            emergencyDisabled: false,
            activeMode: true,
            fruitSelected: false,
            treasureSelected: false,
            MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(1)));

        Assert.Equal(
            AutoHarvestCycleCaptureDisposition.Captured,
            adapter.Capture(config, new LifecycleGeneration(1), out var frame));

        Assert.Equal(AutoHarvestPairCaptureKind.NotSelected, frame.Fruit.Kind);
        Assert.Equal(AutoHarvestPairCaptureKind.NotSelected, frame.Treasure.Kind);
        Assert.False(frame.OwnsActionFamily);
        Assert.Equal(1, ownershipReads);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void CaptureFailuresPreserveKindAndScope(
        bool sharedActiveState,
        bool invocationFailure)
    {
        var expectedReason = ExpectedReason(invocationFailure);
        var expectedScope = sharedActiveState
            ? AutoHarvestCaptureFailureScope.Feature
            : AutoHarvestCaptureFailureScope.Pair;
        var reader = new FailingCaptureStatePort(
            sharedActiveState,
            ExpectedFailure(invocationFailure));
        var contractCircuit = new AutoHarvestContractCircuit();
        var adapter = new AutoHarvestCycleCaptureAdapter(
            new BindingPort(),
            reader,
            new AutoHarvestNativeGateSet(),
            contractCircuit,
            () => true);

        var disposition = adapter.Capture(
            Configuration(fruit: true, treasure: true),
            new LifecycleGeneration(1),
            out var frame);

        Assert.Equal(AutoHarvestCycleCaptureDisposition.Captured, disposition);
        AssertUnavailable(frame.Fruit, expectedReason, expectedScope);
        AssertUnavailable(frame.Treasure, expectedReason, expectedScope);

        if (!invocationFailure)
        {
            Assert.Equal(
                AutoHarvestCycleCaptureDisposition.Captured,
                adapter.Capture(
                    Configuration(fruit: true, treasure: true),
                    new LifecycleGeneration(1),
                    out var retained));
            AssertUnavailable(retained.Fruit, expectedReason, expectedScope);
            AssertUnavailable(retained.Treasure, expectedReason, expectedScope);
            Assert.Equal(sharedActiveState ? 1 : 3, reader.InvocationCount);
        }
    }

    [Fact]
    public void UnexpectedCaptureFailuresEscape()
    {
        var contractCircuit = new AutoHarvestContractCircuit();
        var adapter = new AutoHarvestCycleCaptureAdapter(
            new BindingPort(),
            new FailingCaptureStatePort(failActiveState: true, new NotSupportedException()),
            new AutoHarvestNativeGateSet(),
            contractCircuit,
            () => true);

        Assert.Throws<NotSupportedException>(() => adapter.Capture(
            Configuration(fruit: true, treasure: true),
            new LifecycleGeneration(1),
            out _));
    }

    [Fact]
    public void FeatureContractFailureDominatesSiblingResolutionFailure()
    {
        var contractCircuit = new AutoHarvestContractCircuit();
        var adapter = new AutoHarvestCycleCaptureAdapter(
            new BindingPort(treasureUnavailable: true),
            new FailingCaptureStatePort(
                failActiveState: true,
                new InvalidOperationException("contract drift")),
            new AutoHarvestNativeGateSet(),
            contractCircuit,
            () => true);

        Assert.Equal(
            AutoHarvestCycleCaptureDisposition.Captured,
            adapter.Capture(
                Configuration(fruit: true, treasure: true),
                new LifecycleGeneration(1),
                out var frame));
        AssertUnavailable(
            frame.Fruit,
            AutoHarvestCaptureUnavailableReason.ContractUnavailable,
            AutoHarvestCaptureFailureScope.Feature);
        AssertUnavailable(
            frame.Treasure,
            AutoHarvestCaptureUnavailableReason.ContractUnavailable,
            AutoHarvestCaptureFailureScope.Feature);
    }

    private static AutomataConfiguration Configuration(bool fruit, bool treasure) => AutoHarvestConfigurationFactory.Create(
        masterEnabled: true,
        emergencyDisabled: false,
        activeMode: true,
        fruitSelected: fruit,
        treasureSelected: treasure,
        MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(1)));

    private static Exception ExpectedFailure(bool invocationFailure) => invocationFailure
        ? new TargetInvocationException("transient native failure", new InvalidOperationException())
        : new InvalidOperationException("contract drift");

    private static AutoHarvestCaptureUnavailableReason ExpectedReason(bool invocationFailure) =>
        invocationFailure
            ? AutoHarvestCaptureUnavailableReason.RegistryNotReady
            : AutoHarvestCaptureUnavailableReason.ContractUnavailable;

    private static void AssertUnavailable(
        in AutoHarvestPairCapture capture,
        AutoHarvestCaptureUnavailableReason reason,
        AutoHarvestCaptureFailureScope scope)
    {
        Assert.Equal(AutoHarvestPairCaptureKind.Unavailable, capture.Kind);
        Assert.Equal(reason, capture.UnavailableReason);
        Assert.Equal(scope, capture.FailureScope);
    }

    private sealed class BindingPort : IAutoHarvestBindingPort
    {
        private readonly bool _treasureUnavailable;

        internal BindingPort(bool treasureUnavailable = false)
        {
            _treasureUnavailable = treasureUnavailable;
        }

        public AutoHarvestResolvedPairSet ResolvePairSet()
        {
            var shared = new AutoHarvestSharedBinding(
                new object(),
                new object(),
                null!,
                null!,
                lifecycleGeneration: 1);
            var fruit = PairBinding(AutoHarvestPair.FruitTree);
            var treasure = _treasureUnavailable
                ? null
                : PairBinding(AutoHarvestPair.TreasureTree);
            var treasureFailure = _treasureUnavailable
                ? AutoHarvestNativeFailure.Create(
                    AutoHarvestRuntimeFailureKind.Retryable,
                    AutoHarvestRuntimeFailureScope.Pair)
                : default;
            return AutoHarvestResolvedPairSet.Create(
                null!, shared, fruit, default, treasure, treasureFailure);
        }


        private static AutoHarvestPairBinding PairBinding(AutoHarvestPair pair) => new(
            pair,
            new object(),
            new object(),
            string.Empty,
            string.Empty,
            new object(),
            null!,
            null!,
            null!,
            growthSeconds: 1,
            restSeconds: 1,
            actionSeconds: 1);
    }

    private sealed class FailingCaptureStatePort : IAutoHarvestCaptureStatePort
    {
        private readonly bool _failActiveState;
        private readonly Exception _failure;

        internal FailingCaptureStatePort(bool failActiveState, Exception failure)
        {
            _failActiveState = failActiveState;
            _failure = failure;
        }

        internal int InvocationCount { get; private set; }

        public AutoHarvestActiveActionSnapshot CaptureActiveActions(
            in ResolvedAutoHarvestPair resolved)
        {
            InvocationCount++;
            if (_failActiveState) throw _failure;
            return default;
        }

        public void ReadFacts(
            in ResolvedAutoHarvestPair resolved,
            in AutoHarvestSubmissionState activeState,
            out AutoHarvestPairFacts facts,
            out object? prototype)
        {
            InvocationCount++;
            if (!_failActiveState) throw _failure;
            facts = default;
            prototype = null;
        }
    }
}
