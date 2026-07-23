using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;

namespace OrbAutomata;

internal sealed class AutoHarvestReplayExecutionFactory : IServiceCycleReplayExecutionFactory<
    AutoHarvestCycleFrame,
    AutomataConfiguration,
    AutoHarvestCycleState,
    AutoHarvestCycleAction,
    AutoHarvestCycleInputRecord,
    AutoHarvestStateRecord,
    AutoHarvestActionRecord>
{
    public ServiceId ServiceId => AutoHarvestServicePolicies.ServiceId;
    public WakePolicy DefaultWakePolicy => AutoHarvestServicePolicies.DefaultWakePolicy;
    public ServiceFaultRecoveryPolicy FaultRecoveryPolicy =>
        AutoHarvestServicePolicies.FaultRecoveryPolicy;
    public AutoHarvestCycleFrame CreateFrame() => default;
    public IServiceCycleReplayCodec<AutoHarvestCycleInputRecord> CreateCycleInputCodec() =>
        new AutoHarvestCycleInputCodec();
    public IServiceCycleReplayCodec<AutoHarvestStateRecord> CreateStateCodec() =>
        new AutoHarvestStateCodec();
    public IServiceCycleReplayCodec<AutoHarvestActionRecord> CreateActionCodec() =>
        new AutoHarvestActionCodec();
    public IServiceCycleReplayComparer<AutoHarvestCycleInputRecord> CreateCycleInputComparer() =>
        new AutoHarvestCycleInputComparer();
    public IServiceCycleReplayComparer<AutoHarvestStateRecord> CreateStateComparer() =>
        new AutoHarvestStateComparer();
    public IServiceCycleReplayComparer<AutoHarvestActionRecord> CreateActionComparer() =>
        new AutoHarvestActionComparer();
    public IServiceCycleReplayHydrator<
        AutoHarvestCycleFrame,
        AutomataConfiguration,
        AutoHarvestCycleState,
        AutoHarvestCycleInputRecord,
        AutoHarvestStateRecord> CreateHydrator() => new AutoHarvestReplayHydrator();
    public IServiceCycleReplayEvaluatorPort<
        AutoHarvestCycleFrame,
        AutomataConfiguration,
        AutoHarvestCycleState,
        AutoHarvestCycleAction,
        AutoHarvestStateRecord,
        AutoHarvestActionRecord> CreateEvaluatorPort() => new AutoHarvestReplayEvaluatorPort();
    public ServiceCycleReplayWorker<
        AutoHarvestCycleFrame,
        AutomataConfiguration,
        AutoHarvestCycleState,
        AutoHarvestCycleAction,
        AutoHarvestCycleInputRecord,
        AutoHarvestStateRecord,
        AutoHarvestActionRecord> CreateProductionWorkerDefinition() =>
        new AutomataReplayWorker<
            AutoHarvestCycleFrame,
            AutoHarvestCycleState,
            AutoHarvestCycleAction,
            AutoHarvestCycleInputRecord,
            AutoHarvestStateRecord,
            AutoHarvestActionRecord>(
                CreateEvaluatorPort(),
                CreateCycleInputCodec(),
                CreateStateCodec(),
                CreateActionCodec());
}
