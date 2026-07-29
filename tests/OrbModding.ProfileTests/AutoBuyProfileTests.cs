using System;
using OrbAutomata;
using OrbAutomata.Runtime.ServiceCycle.Profile;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
using OrbModding.Common.Runtime.World;
using Xunit;
using static OrbModding.ProfileTests.AutoHarvestProfileTestSupport;

namespace OrbModding.ProfileTests;

public sealed class AutoBuyProfileTests : IDisposable
{
    private const long PlannedEpoch = 7;

    public AutoBuyProfileTests() => ResetNativeState();

    public void Dispose() => ResetNativeState();

    [Fact]
    public void CommittedStructurePurchaseRoutesAllFourActionStages()
    {
        var measurement = new CapturingMeasurementPort();
        var probe = new ServiceCycleProfileProbe();
        probe.Attach(measurement);
        var operations = new AutomataProfileOperations(probe);

        global::ActionManager.RemainingRoom = 64;
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            queuedQuantity = 3,
        };
        global::StructureSO.All.Add(structure);

        var result = Execute(operations, AutoBuyCandidateKind.Structure, Guid.Parse(structure.uuid));

        Assert.Equal(ServiceActionDisposition.Committed, result.Disposition);
        Assert.Equal(4, structure.queuedQuantity);
        Assert.Empty(measurement.Abandoned);
        Assert.Collection(
            measurement.Completed,
            item => AssertStage(item, ServiceCycleProfileSpan.AutoBuyActionQueueRoomRead),
            item => AssertStage(
                item,
                ServiceCycleProfileSpan.AutoBuyActionCandidateResolution,
                stableIdReads: 2,
                listEntries: 1),
            // One call, not two: IsAvailable() is on the snapshot the worker planned from, so only
            // CanPurchase() is asked here.
            item => AssertStage(
                item,
                ServiceCycleProfileSpan.AutoBuyActionAdmissionRevalidation,
                methodCalls: 1),
            item => AssertStage(
                item,
                ServiceCycleProfileSpan.AutoBuyActionNativeSubmission,
                methodCalls: 3,
                invocationArgumentArrays: 1));
        Assert.Same(measurement, probe.Detach());
    }

    [Fact]
    public void PreflightRejectionStopsBeforeTheNativeSubmissionStage()
    {
        var measurement = new CapturingMeasurementPort();
        var probe = new ServiceCycleProfileProbe();
        probe.Attach(measurement);
        var operations = new AutomataProfileOperations(probe);

        global::ActionManager.RemainingRoom = 64;
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = false,
            queuedQuantity = 3,
        };
        global::StructureSO.All.Add(structure);

        var result = Execute(operations, AutoBuyCandidateKind.Structure, Guid.Parse(structure.uuid));

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(3, structure.queuedQuantity);
        Assert.Equal(
            new[]
            {
                ServiceCycleProfileSpan.AutoBuyActionQueueRoomRead,
                ServiceCycleProfileSpan.AutoBuyActionCandidateResolution,
                ServiceCycleProfileSpan.AutoBuyActionAdmissionRevalidation,
            },
            Spans(measurement.Completed));
        Assert.Empty(measurement.Abandoned);
        Assert.Same(measurement, probe.Detach());
    }

    private static ServiceActionResult Execute(
        AutomataProfileOperations operations,
        AutoBuyCandidateKind kind,
        Guid uuid)
    {
        var adapter = new AutoBuyCycleActionAdapter(
            new AutoBuyNativePurchaseAdapter(operations),
            new AutoBuyNativeQueueRoomAdapter(),
            () => PlannedEpoch,
            () => AutoBuyCandidateKinds.All,
            operations,
            IgnoreRefusals.Instance);
        return adapter.TryExecute(
            new AutoBuyCycleAction(kind, uuid, PlannedEpoch),
            Configuration(),
            ActionContext());
    }

    private static ServiceActionContext ActionContext()
    {
        var coordinates = new ServiceCycleProfileCoordinates(serviceOrdinal: 1, frameIdentity: 41);
        var identity = new ServiceCycleIdentity(
            new ServiceId("AutoBuy"),
            new LifecycleGeneration((ulong)PlannedEpoch),
            new ConfigGeneration(1),
            new StrategyGeneration(1),
            new WorldGeneration(1),
            new CycleId(5));
        return new ServiceActionContext(
            identity,
            new BatchId(1),
            new ActionId(1),
            actionIndex: 0,
            new MonotonicTimestamp(10),
            in coordinates);
    }

    private static SuiteRuntimeConfiguration Configuration() => new()
    {
        General = new SuiteGeneralConfiguration { Enabled = true },
        AutoBuy = new AutoBuyConfiguration
        {
            Mode = AutoBuyOperationMode.Active,
            IncludeStructures = true,
            IncludeUpgrades = true,
        },
    };

    private sealed class IgnoreRefusals : IAutoBuyRefusalResponsePort
    {
        internal static IgnoreRefusals Instance { get; } = new();
        public void ObserveRefusal(in AutoBuyRefusalReport report)
        {
        }
    }

    private static void AssertStage(
        in CapturedMeasurement item,
        ServiceCycleProfileSpan span,
        uint methodCalls = 0,
        uint stableIdReads = 0,
        uint listEntries = 0,
        uint invocationArgumentArrays = 0)
    {
        Assert.Equal((int)span, item.Context.StageCode);
        Assert.Equal(1, item.Context.ServiceOrdinal);
        Assert.Equal((ulong)41, item.Context.Frame);
        Assert.Equal(ServiceCycleProfileTemperature.Warm, item.Context.Temperature);
        Assert.Equal((uint)0, item.Operations.ReflectedFieldReads);
        Assert.Equal(methodCalls, item.Operations.ReflectedMethodCalls);
        Assert.Equal(stableIdReads, item.Operations.StableIdReads);
        Assert.Equal(listEntries, item.Operations.ListEntries);
        Assert.Equal(invocationArgumentArrays, item.Operations.InvocationArgumentArrays);
        Assert.Equal((uint)0, item.Operations.RecordCopies);
    }

    private static void ResetNativeState()
    {
        global::StructureSO.All.Clear();
        global::UpgradeSO.All.Clear();
        global::ResourceSO.All.Clear();
        global::ValueModifierVariable.All.Clear();
        global::ActionManager.instance = new global::ActionManager();
        global::ActionManager.RemainingRoom = 0;
        global::GlobalVariables.MultiBuy = new global::IntVariable();
        global::Player.BulkDevelopment = new global::IntVariable();
        NativeMultiBuyScope.ResetQuarantineForTests();
    }
}
