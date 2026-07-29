using System;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Services.AutoAgromancy;

public sealed class AutoAgromancyCycleActionAdapterTests
{
    [Theory]
    [InlineData(RejectionScenario.Disabled)]
    [InlineData(RejectionScenario.ConfigurationChanged)]
    [InlineData(RejectionScenario.LifecycleChanged)]
    [InlineData(RejectionScenario.OwnershipLost)]
    [InlineData(RejectionScenario.PermitLost)]
    [InlineData(RejectionScenario.WorldUnavailable)]
    [InlineData(RejectionScenario.FactsChanged)]
    public void PreflightRejectionsNeverReachNativeMutation(RejectionScenario scenario)
    {
        var fixture = new Fixture();
        var expected = fixture.Configure(scenario);

        var result = fixture.Execute();

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(expected, result.Code);
        Assert.Equal(0, fixture.Native.CallCount);
    }

    [Fact]
    public void VerifiedMutationCommitsWithExactNativeEvidence()
    {
        var fixture = new Fixture();

        var result = fixture.Execute();

        Assert.Equal(ServiceActionDisposition.Committed, result.Disposition);
        Assert.Equal(CommonActionResultCodes.Committed, result.Code);
        Assert.Equal(NativeMutationOutcome.Verified, result.NativeEvidence.Outcome);
        AssertCallOutcome(result, nativeCalls: 1, attempts: 1, committed: 1);
        var call = Assert.Single(fixture.Native.Calls);
        Assert.Equal(fixture.Action.ActionId, call.ActionId);
        Assert.Equal(fixture.Action.ElementId, call.ElementId);
        Assert.Equal(fixture.Action.ObservedLevel, call.ExpectedLevel);
        Assert.Equal(fixture.Action.TargetLevel, call.TargetLevel);
    }

    [Fact]
    public void UnsafePostconditionRollsBackAndReportsBothAttempts()
    {
        var fixture = new Fixture();
        fixture.Worlds.Replace(
            fixture.Before,
            fixture.World(level: 2),
            fixture.World(level: 1));
        fixture.Native.Replace(
            Mutation(AutoAgromancyExactMutationDisposition.Committed, 1, 3),
            Mutation(AutoAgromancyExactMutationDisposition.Committed, 3, 1));

        var result = fixture.Execute();

        Assert.Equal(ServiceActionDisposition.Faulted, result.Disposition);
        Assert.Equal(AutoAgromancyActionResultCodes.SafetyRollback, result.Code);
        Assert.Equal(NativeMutationOutcome.PostconditionFailed, result.NativeEvidence.Outcome);
        AssertCallOutcome(result, nativeCalls: 2, attempts: 2, committed: 0);
        Assert.Equal(2, fixture.Native.CallCount);
    }

    [Fact]
    public void FailedRollbackQuarantinesFurtherMutationUntilLifecycleInvalidation()
    {
        var fixture = new Fixture();
        fixture.Worlds.Replace(fixture.Before, fixture.World(level: 2));
        fixture.Native.Replace(
            Mutation(AutoAgromancyExactMutationDisposition.Committed, 1, 3),
            Mutation(AutoAgromancyExactMutationDisposition.Rejected, 3, 3));

        var failed = fixture.Execute();
        var quarantined = fixture.Execute();

        Assert.Equal(ServiceActionDisposition.Faulted, failed.Disposition);
        AssertCallOutcome(failed, nativeCalls: 2, attempts: 2, committed: 0);
        Assert.Equal(ServiceActionDisposition.Rejected, quarantined.Disposition);
        Assert.Equal(
            AutoAgromancyActionResultCodes.MutationQuarantined,
            quarantined.Code);
        Assert.Equal(2, fixture.Native.CallCount);

        fixture.Adapter.InvalidateLifecycle();
        fixture.Worlds.Replace(fixture.Before, fixture.After);
        fixture.Native.Replace(
            Mutation(AutoAgromancyExactMutationDisposition.Committed, 1, 3));

        var recovered = fixture.Execute();

        Assert.Equal(ServiceActionDisposition.Committed, recovered.Disposition);
        Assert.Equal(3, fixture.Native.CallCount);
    }

    [Fact]
    public void AttemptedUnverifiedMutationQuarantinesWithOneAttempt()
    {
        var fixture = new Fixture();
        fixture.Worlds.Replace(fixture.Before);
        fixture.Native.Replace(
            Mutation(
                AutoAgromancyExactMutationDisposition.AttemptedUnverified,
                1,
                -1));

        var failed = fixture.Execute();
        var quarantined = fixture.Execute();

        Assert.Equal(ServiceActionDisposition.Faulted, failed.Disposition);
        Assert.Equal(CommonActionResultCodes.AdapterFault, failed.Code);
        AssertCallOutcome(failed, nativeCalls: 1, attempts: 1, committed: 0);
        Assert.Equal(ServiceActionDisposition.Rejected, quarantined.Disposition);
        Assert.Equal(
            AutoAgromancyActionResultCodes.MutationQuarantined,
            quarantined.Code);
        Assert.Equal(1, fixture.Native.CallCount);
    }

    [Fact]
    public void ContractUnavailableIsAnAdapterFaultWithoutMutationEvidence()
    {
        var fixture = new Fixture();
        fixture.Worlds.Replace(fixture.Before);
        fixture.Native.Replace(
            Mutation(
                AutoAgromancyExactMutationDisposition.ContractUnavailable,
                1,
                1));

        var result = fixture.Execute();

        Assert.Equal(ServiceActionDisposition.Faulted, result.Disposition);
        Assert.Equal(CommonActionResultCodes.AdapterFault, result.Code);
        Assert.False(result.HasNativeEvidence);
        Assert.Equal(1, fixture.Native.CallCount);
    }

    private static AutoAgromancyExactMutationResult Mutation(
        AutoAgromancyExactMutationDisposition disposition,
        int previous,
        int observed) =>
        new(disposition, previous, observed, disposition.ToString());

    private static void AssertCallOutcome(
        ServiceActionResult result,
        int nativeCalls,
        int attempts,
        int committed)
    {
        Assert.True(result.HasNativeEvidence);
        Assert.Equal(nativeCalls, result.NativeCallOutcome.NativeCallsAttempted);
        Assert.Equal(attempts, result.NativeCallOutcome.MutationAttempts);
        Assert.Equal(committed, result.NativeCallOutcome.MutationsCommitted);
    }

    public enum RejectionScenario
    {
        Disabled,
        ConfigurationChanged,
        LifecycleChanged,
        OwnershipLost,
        PermitLost,
        WorldUnavailable,
        FactsChanged,
    }

    private sealed class Fixture
    {
        private const long Lifecycle = 1;
        private bool _owns = true;
        private bool _permit = true;
        private long _lifecycle = Lifecycle;
        private ConfigGeneration _generation = new(1);
        private SuiteRuntimeConfiguration _configuration;

        internal Fixture()
        {
            ActionId = Guid.NewGuid();
            ElementId = Guid.NewGuid();
            ResourceId = Guid.NewGuid();
            _configuration = ActiveConfiguration();
            Before = World(level: 1);
            After = World(level: 3);
            var pair = Before.HarvestActions[0];
            Assert.True(
                AutoAgromancyPlanningProjection.TryBuildFingerprint(
                    Before,
                    in pair,
                    out var fingerprint));
            Action = new AutoAgromancyCycleAction(
                ActionId,
                ElementId,
                observedLevel: 1,
                targetLevel: 3,
                maximumLevel: 5,
                collectedAtEpoch: Lifecycle,
                fingerprint);
            Native = new NativeMutator();
            Native.Replace(
                Mutation(AutoAgromancyExactMutationDisposition.Committed, 1, 3));
            Worlds = new WorldReader(Before, After);
            Adapter = new AutoAgromancyCycleActionAdapter(
                Native,
                Worlds,
                () => _lifecycle,
                () => _owns,
                () => _permit,
                () => _configuration,
                () => _generation);
        }

        internal Guid ActionId { get; }
        internal Guid ElementId { get; }
        internal Guid ResourceId { get; }
        internal GameWorldState Before { get; }
        internal GameWorldState After { get; }
        internal AutoAgromancyCycleAction Action { get; }
        internal NativeMutator Native { get; }
        internal WorldReader Worlds { get; }
        internal AutoAgromancyCycleActionAdapter Adapter { get; }

        internal ServiceActionResultCode Configure(RejectionScenario scenario)
        {
            switch (scenario)
            {
                case RejectionScenario.Disabled:
                    _configuration = new SuiteRuntimeConfiguration
                    {
                        General = new SuiteGeneralConfiguration { Enabled = true },
                        AutoAgromancy = new AutoAgromancyConfiguration(),
                    };
                    return CommonActionResultCodes.ServiceDisabled;
                case RejectionScenario.ConfigurationChanged:
                    _generation = new ConfigGeneration(2);
                    return AutoAgromancyActionResultCodes.LiveConfigurationChanged;
                case RejectionScenario.LifecycleChanged:
                    _lifecycle = 2;
                    return CommonActionResultCodes.LifecycleReplaced;
                case RejectionScenario.OwnershipLost:
                    _owns = false;
                    return AutoAgromancyActionResultCodes.ActionFamilyUnavailable;
                case RejectionScenario.PermitLost:
                    _permit = false;
                    return AutoAgromancyActionResultCodes.ActionFamilyUnavailable;
                case RejectionScenario.WorldUnavailable:
                    Worlds.Fail = true;
                    return AutoAgromancyActionResultCodes.PairUnavailable;
                case RejectionScenario.FactsChanged:
                    Worlds.Replace(World(level: 1, trueRate: new BigDouble(9)));
                    return AutoAgromancyActionResultCodes.LiveFactsChanged;
                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario));
            }
        }

        internal ServiceActionResult Execute()
        {
            var cycle = new ServiceCycleIdentity(
                new ServiceId("AutoAgromancy"),
                new LifecycleGeneration(Lifecycle),
                new ConfigGeneration(1),
                new StrategyGeneration(1),
                new WorldGeneration(1),
                new CycleId(1));
            var context = new ServiceActionContext(
                cycle,
                new BatchId(1),
                new ActionId(1),
                actionIndex: 0,
                new MonotonicTimestamp(1));
            return Adapter.TryExecute(Action, _configuration, context);
        }

        internal GameWorldState World(
            int level,
            BigDouble? trueRate = null)
        {
            var action = new WorldHarvestAction(
                ActionId,
                ElementId,
                level,
                maximumLevel: 5,
                visible: true,
                actionCostModifier: new BigDouble(100),
                actionSpeed: new BigDouble(100),
                hasInstanceScaling: false);
            var cost = new WorldHarvestActionCost(
                ActionId,
                ElementId,
                WorldHarvestActionCostKind.Base,
                position: 0,
                ResourceId,
                new BigDouble(1));
            return new GameWorldState
            {
                HarvestActions =
                    PublicationTable<WorldHarvestAction>.Create(new[] { action }),
                HarvestActionCosts =
                    PublicationTable<WorldHarvestActionCost>.Create(new[] { cost }),
                HarvestElements = WorldTable.Create(new[] { Element(ElementId) }),
                Resources = WorldTable.Create(
                    new[] { Resource(ResourceId, trueRate ?? new BigDouble(10)) }),
                HarvestActionCaptureState = WorldHarvestActionCaptureState.Complete,
                CollectedAtEpoch = Lifecycle,
            };
        }

        private static SuiteRuntimeConfiguration ActiveConfiguration() => new()
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
            AutoAgromancy = new AutoAgromancyConfiguration
            {
                Mode = AutoAgromancyOperationMode.Active,
            },
        };

        private static WorldHarvestElement Element(Guid id) => new(
            id,
            BigDouble.Zero,
            masteryLevel: 4,
            0,
            0,
            0,
            0,
            0,
            BigDouble.Zero,
            BigDouble.Zero,
            BigDouble.Zero,
            BigDouble.Zero,
            BigDouble.Zero,
            BigDouble.Zero,
            BigDouble.Zero,
            BigDouble.Zero,
            new BigDouble(100),
            new BigDouble(100),
            BigDouble.Zero,
            BigDouble.Zero);

        private static WorldResource Resource(Guid id, BigDouble trueRate)
        {
            var rateInputs = default(RawResourceRateInputs);
            var traits = default(RawResourceTraits);
            var modifiers = default(RawResourceModifiers);
            var sample = new RawResourceSample(
                id,
                new BigDouble(100),
                new BigDouble(-1),
                trueRate,
                true,
                BigDouble.Zero,
                BigDouble.Zero,
                new BigDouble(100),
                new BigDouble(100),
                BigDouble.Zero,
                BigDouble.Zero,
                BigDouble.Zero,
                false,
                false,
                false,
                0,
                Guid.Empty,
                in rateInputs,
                in traits,
                in modifiers);
            return new WorldResource(
                in sample,
                false,
                BigDouble.Zero,
                0,
                false,
                new BigDouble(100),
                trueRate);
        }
    }

    private sealed class NativeMutator : IAutoAgromancyExactNativeMutator
    {
        private readonly Queue<AutoAgromancyExactMutationResult> _results = new();

        internal List<MutationCall> Calls { get; } = new();
        internal int CallCount => Calls.Count;

        internal void Replace(params AutoAgromancyExactMutationResult[] results)
        {
            _results.Clear();
            foreach (var result in results) _results.Enqueue(result);
        }

        public AutoAgromancyExactMutationResult ApplyExactTarget(
            Guid actionId,
            Guid elementId,
            int expectedCurrentLevel,
            int targetLevel)
        {
            Calls.Add(
                new MutationCall(
                    actionId,
                    elementId,
                    expectedCurrentLevel,
                    targetLevel));
            return _results.Dequeue();
        }
    }

    private sealed class WorldReader : IAutoAgromancyLiveWorldReader
    {
        private readonly Queue<GameWorldState> _worlds = new();

        internal WorldReader(params GameWorldState[] worlds) => Replace(worlds);

        internal bool Fail { get; set; }

        internal void Replace(params GameWorldState[] worlds)
        {
            _worlds.Clear();
            foreach (var world in worlds) _worlds.Enqueue(world);
            Fail = false;
        }

        public bool TryRead(long lifecycleEpoch, out GameWorldState world)
        {
            if (Fail || _worlds.Count == 0)
            {
                world = GameWorldStateDefaults.Empty;
                return false;
            }
            world = _worlds.Dequeue();
            return true;
        }
    }

    private readonly record struct MutationCall(
        Guid ActionId,
        Guid ElementId,
        int ExpectedLevel,
        int TargetLevel);
}
