using System;
using System.Collections.Generic;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using Xunit;

namespace OrbAutomata.Tests;

public sealed class AutoHarvestCycleActionAdapterTests
{
    [Theory]
    [InlineData(RejectionScenario.Disabled)]
    [InlineData(RejectionScenario.Quarantined)]
    [InlineData(RejectionScenario.Ownership)]
    [InlineData(RejectionScenario.Permit)]
    [InlineData(RejectionScenario.Lifecycle)]
    public void RejectionsBeforeFinalReadDoNotReachNativeMutation(
        RejectionScenario scenario)
    {
        using var fixture = new Fixture();
        var expectedCode = fixture.ConfigureScenario(scenario);

        var result = fixture.Execute();

        Assert.Equal(expectedCode, result.Code);
        Assert.Equal(0, fixture.Mutation.SubmitCount);
    }

    [Fact]
    public void NativeSubmissionRunsAfterOwnershipAndMutationPermit()
    {
        using var fixture = new Fixture();

        var result = fixture.Execute();

        Assert.Equal(ServiceActionDisposition.Committed, result.Disposition);
        AssertOrdered(fixture.Events, "ownership", "permit", "resolve", "mutate");
        Assert.Equal(1, fixture.Gates.QuarantineReadCount);
    }

    [Fact]
    public void AttemptedUnverifiedMutationQuarantinesThePair()
    {
        using var fixture = new Fixture();
        fixture.Mutation.Result = new AutoHarvestSubmissionResult(
            NativeMutationOutcome.PostconditionFailed,
            new NativeMutationCallOutcome(1, 1, 0));

        var result = fixture.Execute();

        Assert.Equal(ServiceActionDisposition.Faulted, result.Disposition);
        Assert.True(result.HasNativeEvidence);
        Assert.Equal(1, fixture.Gates.QuarantineCount);
    }

    [Fact]
    public void NonAttemptedFinalPreflightRejectionDoesNotQuarantine()
    {
        using var fixture = new Fixture();
        fixture.Mutation.Result = new AutoHarvestSubmissionResult(
            AutoHarvestSubmissionFailureCode.PolicyRevalidationRejected);

        var result = fixture.Execute();

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.False(result.HasNativeEvidence);
        Assert.Equal(0, fixture.Gates.QuarantineCount);
    }

    private static void AssertOrdered(IReadOnlyList<string> events, params string[] expected)
    {
        var previous = -1;
        foreach (var item in expected)
        {
            var current = -1;
            for (var index = previous + 1; index < events.Count; index++)
            {
                if (events[index] != item) continue;
                current = index;
                break;
            }
            Assert.True(current > previous, $"'{item}' was not observed after index {previous}.");
            previous = current;
        }
    }

    private sealed class Fixture : IDisposable
    {
        private bool _owns = true;
        private bool _capturePermit = true;
        private bool _operational = true;
        private ulong _nativeLifecycle = 7;
        private readonly AutoHarvestCycleActionAdapter _adapter;

        public Fixture()
        {
            Events = new List<string>();
            Bindings = new BindingPort(Events, () => _nativeLifecycle);
            Mutation = new MutationPort(Events);
            Gates = new GatePort();
            _adapter = new AutoHarvestCycleActionAdapter(
                Bindings,
                Mutation,
                Gates,
                () =>
                {
                    Events.Add("ownership");
                    return _owns;
                },
                () =>
                {
                    Events.Add("permit");
                    return _capturePermit;
                });
        }

        public List<string> Events { get; }
        public BindingPort Bindings { get; }
        public MutationPort Mutation { get; }
        public GatePort Gates { get; }

        public ServiceActionResultCode ConfigureScenario(RejectionScenario scenario)
        {
            return scenario switch
            {
                RejectionScenario.Disabled => SetDisabled(),
                RejectionScenario.Quarantined => SetQuarantined(),
                RejectionScenario.Ownership => SetOwnershipUnavailable(),
                RejectionScenario.Permit => SetPermitUnavailable(),
                RejectionScenario.Lifecycle => SetLifecycleReplaced(),
                _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
            };
        }

        private ServiceActionResultCode SetDisabled()
        {
            _operational = false;
            return CommonActionResultCodes.ServiceDisabled;
        }

        private ServiceActionResultCode SetQuarantined()
        {
            Gates.Quarantined = true;
            return CommonActionResultCodes.AdapterFault;
        }

        private ServiceActionResultCode SetOwnershipUnavailable()
        {
            _owns = false;
            return AutoHarvestActionResultCodes.ActionFamilyUnavailable;
        }

        private ServiceActionResultCode SetPermitUnavailable()
        {
            _capturePermit = false;
            return AutoHarvestActionResultCodes.ActionFamilyUnavailable;
        }

        private ServiceActionResultCode SetLifecycleReplaced()
        {
            _nativeLifecycle = 8;
            return CommonActionResultCodes.LifecycleReplaced;
        }

        public ServiceActionResult Execute()
        {
            var configuration = AutoHarvestConfigurationFactory.Create(
                masterEnabled: _operational,
                emergencyDisabled: false,
                activeMode: true,
                fruitSelected: true,
                treasureSelected: false,
                MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(1)));
            var cycle = new ServiceCycleIdentity(
                new ServiceId("AutoHarvest"),
                new LifecycleGeneration(7),
                new ConfigGeneration(1),
                new StrategyGeneration(1),
                new CaptureSequence(1),
                new CycleId(1));
            var context = new ServiceActionContext(
                cycle,
                new BatchId(1),
                new ActionId(1),
                actionIndex: 0,
                new MonotonicTimestamp(1));
            var action = new AutoHarvestCycleAction(AutoHarvestPair.FruitTree);
            return _adapter.TryExecute(action, configuration, context);
        }

        public void Dispose() { }
    }

    private sealed class BindingPort : IAutoHarvestBindingPort
    {
        private readonly List<string> _events;
        private readonly Func<ulong> _readLifecycle;

        public BindingPort(List<string> events, Func<ulong> readLifecycle)
        {
            _events = events;
            _readLifecycle = readLifecycle;
        }

        public AutoHarvestResolvedPairSet ResolvePairSet()
        {
            _events.Add("resolve");
            var shared = new AutoHarvestSharedBinding(
                new object(),
                new object(),
                null!,
                null!,
                checked((long)_readLifecycle()));
            var fruit = new AutoHarvestPairBinding(
                AutoHarvestPair.FruitTree,
                new object(),
                new object(),
                Guid.NewGuid().ToString("D"),
                Guid.NewGuid().ToString("D"),
                new object(),
                null!,
                null!,
                null!,
                growthSeconds: 1,
                restSeconds: 1,
                actionSeconds: 1);
            return AutoHarvestResolvedPairSet.Create(
                null!,
                shared,
                fruit,
                default,
                treasure: null,
                AutoHarvestNativeFailure.Create(
                    AutoHarvestRuntimeFailureKind.Retryable,
                    AutoHarvestRuntimeFailureScope.Pair));
        }

    }

    private sealed class MutationPort : IAutoHarvestMutationPort
    {
        private readonly List<string> _events;

        public MutationPort(List<string> events)
        {
            _events = events;
            Result = new AutoHarvestSubmissionResult(
                NativeMutationOutcome.Verified,
                new NativeMutationCallOutcome(1, 1, 1));
        }

        public AutoHarvestSubmissionResult Result { get; set; }
        public int SubmitCount { get; private set; }

        public AutoHarvestSubmissionResult Submit(
            in ResolvedAutoHarvestPair resolved)
        {
            _events.Add("mutate");
            SubmitCount++;
            return Result;
        }
    }

    private sealed class GatePort : IAutoHarvestGatePort
    {
        public bool Quarantined { get; set; }
        public int QuarantineReadCount { get; private set; }
        public int QuarantineCount { get; private set; }
        public void ObserveResolvedPairs(in AutoHarvestResolvedPairSet pairs) { }
        public bool IsQuarantined(AutoHarvestPair pair)
        {
            QuarantineReadCount++;
            return Quarantined;
        }
        public void Quarantine(AutoHarvestPair pair)
        {
            Quarantined = true;
            QuarantineCount++;
        }
    }

    public enum RejectionScenario
    {
        Disabled,
        Quarantined,
        Ownership,
        Permit,
        Lifecycle,
    }
}
