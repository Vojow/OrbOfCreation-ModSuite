using System;
using System.Collections.Generic;
using OrbAutomata;
using OrbAutomata.Runtime.ServiceCycle.Profile;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
using Xunit;

namespace OrbModding.ProfileTests;

internal static class AutoHarvestProfileTestSupport
{
    internal static AutoHarvestCycleCaptureAdapter CreateAdapter(
        BindingPort bindings,
        CaptureStatePort reader,
        AutoHarvestProfileOperations operations) => new(
            bindings,
            reader,
            new GatePort(),
            new ContractCircuit(),
            () => true,
            operations,
            bindings);

    internal static AutomataConfiguration Configuration() => AutoHarvestConfigurationFactory.Create(
        masterEnabled: true,
        emergencyDisabled: false,
        activeMode: true,
        fruitSelected: true,
        treasureSelected: true,
        MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(1)));

    internal static ServiceCaptureContext CaptureContext(int serviceOrdinal, long frameIdentity)
    {
        var coordinates = new ServiceCycleProfileCoordinates(serviceOrdinal, frameIdentity);
        return new ServiceCaptureContext(
            AutoHarvestServicePolicies.ServiceId,
            new LifecycleGeneration(1),
            new ConfigGeneration(1),
            new CaptureSequence(1),
            new CycleId(5),
            new MonotonicTimestamp(10),
            in coordinates);
    }

    internal static ServiceCaptureContext CaptureContextWithoutProfileCoordinates()
    {
        var coordinates = default(ServiceCycleProfileCoordinates);
        return new ServiceCaptureContext(
            AutoHarvestServicePolicies.ServiceId,
            new LifecycleGeneration(1),
            new ConfigGeneration(1),
            new CaptureSequence(1),
            new CycleId(5),
            new MonotonicTimestamp(10),
            in coordinates);
    }

    internal static ServiceActionContext ActionContext(int serviceOrdinal, long frameIdentity)
    {
        var coordinates = new ServiceCycleProfileCoordinates(serviceOrdinal, frameIdentity);
        var identity = new ServiceCycleIdentity(
            AutoHarvestServicePolicies.ServiceId,
            new LifecycleGeneration(1),
            new ConfigGeneration(1),
            new StrategyGeneration(1),
            new CaptureSequence(1),
            new CycleId(5));
        return new ServiceActionContext(
            identity,
            new BatchId(1),
            new ActionId(1),
            actionIndex: 0,
            new MonotonicTimestamp(10),
            in coordinates);
    }

    internal static int[] StageCodes(List<CapturedMeasurement> measurements)
    {
        var result = new int[measurements.Count];
        for (var index = 0; index < measurements.Count; index++)
            result[index] = measurements[index].Context.StageCode;
        return result;
    }

    internal sealed class BindingPort :
        IAutoHarvestBindingPort,
        IAutoHarvestProfileBindingObservation
    {
        private readonly AutoHarvestProfileOperations _operations;
        private readonly bool _treasureAvailable;
        private bool _driftDuringNextResolve;
        private ServiceCycleProfileTemperature _temperature =
            ServiceCycleProfileTemperature.ColdProcess;

        internal BindingPort(
            AutoHarvestProfileOperations operations,
            bool treasureAvailable = true,
            bool driftDuringNextResolve = false)
        {
            _operations = operations;
            _treasureAvailable = treasureAvailable;
            _driftDuringNextResolve = driftDuringNextResolve;
        }

        public ServiceCycleProfileTemperature CurrentTemperature => _temperature;

        public ServiceCycleProfileTemperature PrepareTemperature() => _temperature;

        public AutoHarvestResolvedPairSet ResolvePairSet()
        {
            if (_driftDuringNextResolve)
            {
                _driftDuringNextResolve = false;
                _temperature = ServiceCycleProfileTemperature.LifecycleRebind;
            }
            _operations.AddStableIdRead();
            _operations.AddStableIdRead();
            var shared = new AutoHarvestSharedBinding(
                new object(),
                new object(),
                null!,
                null!,
                lifecycleGeneration: 1);
            var fruit = PairBinding(AutoHarvestPair.FruitTree);
            var treasure = _treasureAvailable
                ? PairBinding(AutoHarvestPair.TreasureTree)
                : null;
            var treasureFailure = _treasureAvailable
                ? default
                : AutoHarvestNativeFailure.Create(
                    AutoHarvestRuntimeFailureKind.Retryable,
                    AutoHarvestRuntimeFailureScope.Pair);
            return AutoHarvestResolvedPairSet.Create(
                null!,
                shared,
                fruit,
                default,
                treasure,
                treasureFailure);
        }


        public bool TryComplete(ServiceCycleProfileTemperature observed)
        {
            if (_temperature != observed) return false;
            _temperature = ServiceCycleProfileTemperature.Warm;
            return true;
        }

        private static AutoHarvestPairBinding PairBinding(AutoHarvestPair pair) => new(
            pair,
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
    }

    internal sealed class CaptureStatePort : IAutoHarvestCaptureStatePort
    {
        private readonly AutoHarvestProfileOperations _operations;
        private readonly Exception? _activeFailure;

        internal CaptureStatePort(
            AutoHarvestProfileOperations operations,
            Exception? activeFailure = null)
        {
            _operations = operations;
            _activeFailure = activeFailure;
        }

        public AutoHarvestActiveActionSnapshot CaptureActiveActions(
            in ResolvedAutoHarvestPair resolved)
        {
            _operations.AddReflectedFieldRead();
            _operations.AddReflectedMethodCall();
            _operations.AddListEntry();
            if (_activeFailure is not null) throw _activeFailure;
            return new AutoHarvestActiveActionSnapshot(
                true,
                usedEntryCount: 0,
                emptyEntryCount: 1,
                nativeHasEmptyEntry: true,
                supportedCollectCount: 0,
                default,
                default);
        }

        public void ReadFacts(
            in ResolvedAutoHarvestPair resolved,
            in AutoHarvestSubmissionState activeState,
            out AutoHarvestPairFacts facts,
            out object? prototype)
        {
            _operations.AddReflectedMethodCall();
            _operations.AddStableIdRead();
            _operations.AddStableIdRead();
            facts = new AutoHarvestPairFacts(
                AutoHarvestEvidenceState.Verified,
                AutoHarvestEvidenceState.Verified,
                AutoHarvestEvidenceState.Verified,
                AutoHarvestEvidenceState.Verified,
                AutoHarvestEvidenceState.Verified,
                AutoHarvestActionSafetyState.NativePhaseCyclePreserving,
                AutoHarvestEvidenceState.Verified,
                AutoHarvestEvidenceState.Verified);
            prototype = null;
        }
    }

    internal sealed class CapturingMeasurementPort : IServiceCycleProfileMeasurementPort
    {
        private ulong _sequence;

        internal List<CapturedMeasurement> Completed { get; } = new();
        internal List<CapturedMeasurement> Abandoned { get; } = new();

        public bool TryBegin(
            in ServiceCycleProfileContext context,
            out ServiceCycleProfileMeasurementToken token)
        {
            token = new ServiceCycleProfileMeasurementToken(
                this,
                ++_sequence,
                in context,
                startedAtRawTicks: 0,
                allocatedBytes: 0);
            return true;
        }

        public ServiceCycleProfileMeasurementResult Complete(
            in ServiceCycleProfileMeasurementToken token,
            in ServiceCycleProfileOperationCounters operations)
        {
            Assert.True(operations.TrySnapshot(out var snapshot));
            Completed.Add(new CapturedMeasurement(token.Context, snapshot));
            return ServiceCycleProfileMeasurementResult.Accepted;
        }

        public ServiceCycleProfileMeasurementResult Abandon(
            in ServiceCycleProfileMeasurementToken token)
        {
            Abandoned.Add(new CapturedMeasurement(token.Context, default));
            return ServiceCycleProfileMeasurementResult.Accepted;
        }
    }

    private sealed class GatePort : IAutoHarvestGatePort
    {
        public void ObserveResolvedPairs(in AutoHarvestResolvedPairSet pairs)
        {
        }

        public bool IsQuarantined(AutoHarvestPair pair) => false;
        public void Quarantine(AutoHarvestPair pair)
        {
        }
    }

    private sealed class ContractCircuit : IAutoHarvestContractCircuit
    {
        public AutoHarvestNativeFailure FailureFor(AutoHarvestPair pair) => default;
        public void Block(AutoHarvestPair pair, AutoHarvestRuntimeFailureScope scope)
        {
        }
    }

    private sealed class ZeroClock : IPerformanceClock
    {
        public long GetTimestamp() => 0;
        public double GetElapsedMilliseconds(long startTimestamp, long endTimestamp) => 0;
    }
}

internal readonly record struct CapturedMeasurement(
    ServiceCycleProfileContext Context,
    ServiceCycleProfileOperations Operations);
