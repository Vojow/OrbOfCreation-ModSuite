using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class AutoHarvestService
{
    internal static IAutomataReplayServiceDefinition<
        AutoHarvestCycleFrame,
        AutoHarvestCycleState,
        AutoHarvestCycleAction,
        AutoHarvestCycleInputRecord,
        AutoHarvestStateRecord,
        AutoHarvestActionRecord> Define(
        IAutoHarvestCycleCapturePort capture,
        IAutoHarvestCycleActionPort actions)
    {
        if (capture is null) throw new ArgumentNullException(nameof(capture));
        if (actions is null) throw new ArgumentNullException(nameof(actions));

        var metadata = new AutomataServiceMetadata(
            AutoHarvestServicePolicies.ServiceId,
            AutoHarvestServicePolicies.DefaultWakePolicy,
            AutoHarvestServicePolicies.FaultRecoveryPolicy);
        var service = AutomataService.Define<
            AutoHarvestCycleFrame,
            AutoHarvestCycleState,
            AutoHarvestCycleAction>(
                in metadata,
                createFrame: static () => default,
                createWorker: static () => new AutoHarvestWorkerDefinition(),
                shouldStart: ShouldStart,
                capture: Capture,
                execute: actions.TryExecute);
        var replay = new AutoHarvestReplayExecutionFactory();
        return AutomataReplayService.Decorate<
            AutoHarvestCycleFrame,
            AutoHarvestCycleState,
            AutoHarvestCycleAction,
            AutoHarvestCycleInputRecord,
            AutoHarvestStateRecord,
            AutoHarvestActionRecord>(
                service,
                createCycleInputRecord: static (
                    in AutoHarvestCycleFrame frame,
                    in AutomataConfiguration config,
                    in ServiceCaptureContext _,
                    in ServiceCaptureResult _) => new(frame, config),
                createWorker: replay.CreateProductionWorkerDefinition);

        ServiceCaptureResult Capture(
            ref AutoHarvestCycleFrame frame,
            in AutomataConfiguration config,
            in ServiceCaptureContext context)
        {
            var disposition = capture.Capture(
                config,
                context.Lifecycle,
#if SERVICE_CYCLE_PROFILE
                context,
#endif
                out var captured);
            if (disposition == AutoHarvestCycleCaptureDisposition.Unavailable)
            {
                return ServiceCaptureResult.Unavailable(
                    CommonServiceDecisionCodes.CaptureUnavailable,
                    WakePolicy.AfterDecision(config.AutoHarvest.EvaluationInterval));
            }
            if (disposition != AutoHarvestCycleCaptureDisposition.Captured)
                throw new InvalidOperationException("Auto Harvest capture returned an unknown disposition.");
            frame = captured;
            return ServiceCaptureResult.Captured(
                new StrategyGeneration(1),
                CommonServiceDecisionCodes.Captured);
        }
    }

    private static ServiceStartDecision ShouldStart(
        in AutomataConfiguration config,
        in ServiceCycleStartContext context) =>
        AutoHarvestConfigurationPolicy.IsOperational(config)
            ? ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready)
            : ServiceStartDecision.Wait(
                CommonServiceDecisionCodes.NotReady,
                WakePolicy.AfterDecision(config.AutoHarvest.EvaluationInterval));
}
