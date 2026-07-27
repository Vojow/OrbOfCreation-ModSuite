using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Tracing;

internal static partial class ServiceCycleSemanticPayloadValidation
{
    private static void ValidateLifecycleAndContext(
        ServiceCycleSemanticEventKind kind,
        in ServiceCycleSemanticPayload payload)
    {
        switch (kind)
        {
            case ServiceCycleSemanticEventKind.ConfigurationPublished:
                Require(payload.Configuration != 0 && payload.Strategy == 0, nameof(payload));
                break;
            case ServiceCycleSemanticEventKind.StrategyPublished:
                Require(payload.Strategy != 0 && payload.Configuration == 0, nameof(payload));
                break;
            case ServiceCycleSemanticEventKind.EmergencyEntered:
            case ServiceCycleSemanticEventKind.EmergencyCleared:
                Require(payload.Code is >= (int)EmergencyStopReason.UserRequested and <= (int)EmergencyStopReason.SuiteShutdown &&
                    payload.OccurrenceCount > 0, nameof(payload));
                break;
            case ServiceCycleSemanticEventKind.LifecycleRequested:
            case ServiceCycleSemanticEventKind.LifecycleActivated:
                Require(payload.Code == 0, nameof(payload));
                break;
            case ServiceCycleSemanticEventKind.LifecycleRetired:
                Require(IsActionCode(payload.Code, CommonActionResultCodes.LifecycleReplaced.Value), nameof(payload));
                break;
            case ServiceCycleSemanticEventKind.LifecycleConstructionDeferred:
                Require(payload.Code == CommonServiceDecisionCodes.TransientContention.Value &&
                    payload.DeadlineTicks >= payload.TimestampTicks, nameof(payload));
                break;
            case ServiceCycleSemanticEventKind.RetryScheduled:
                Require(payload.Disposition is >= (int)ServiceFaultCategory.Capture and <= (int)ServiceFaultCategory.Start &&
                    IsActionCode(payload.Code, CommonActionResultCodes.AdapterFault.Value) &&
                    payload.DeadlineTicks >= payload.TimestampTicks,
                    nameof(payload));
                break;
            case ServiceCycleSemanticEventKind.FaultObserved:
            case ServiceCycleSemanticEventKind.FaultRecovered:
                Require(payload.Disposition is >= (int)ServiceFaultCategory.Capture and <= (int)ServiceFaultCategory.Start &&
                    IsActionCode(payload.Code, CommonActionResultCodes.AdapterFault.Value) &&
                    payload.DeadlineTicks == 0, nameof(payload));
                break;
            case ServiceCycleSemanticEventKind.PumpCompleted:
                Require(payload.Code is 0 or 1 && payload.ActionIndex >= 0 && payload.FrameIdentity >= 0 &&
                    payload.ResponsesAcquired >= 0 && payload.ActionsAttempted >= 0 &&
                    payload.CapturesAttempted >= 0 && payload.CyclesStarted >= 0 &&
                    payload.WorldGateDeferrals >= 0 && payload.EmergencyBatchesRejected >= 0 &&
                    payload.LifecycleTransitions >= 0 && payload.TotalDurationTicks >= payload.ResponseDurationTicks &&
                    payload.TotalDurationTicks >= payload.ActionDurationTicks &&
                    payload.TotalDurationTicks >= payload.CaptureDurationTicks, nameof(payload));
                break;
            case ServiceCycleSemanticEventKind.StartAttempted:
                break;
            case ServiceCycleSemanticEventKind.StartDeferred:
                Require(IsDecisionCode(payload.Code, CommonServiceDecisionCodes.NotReady.Value) &&
                    payload.TryGetReturnedWake(out var startWake) &&
                    startWake.Kind is WakePolicyKind.AfterDecision or WakePolicyKind.At, nameof(payload));
                break;
            case ServiceCycleSemanticEventKind.StartFaulted:
                Require(payload.Disposition == (int)ServiceFaultCategory.Start &&
                    IsActionCode(payload.Code, CommonActionResultCodes.AdapterFault.Value) &&
                    payload.OccurrenceCount > 0 && payload.DeadlineTicks >= payload.TimestampTicks,
                    nameof(payload));
                break;
            case ServiceCycleSemanticEventKind.StartReady:
                Require(IsDecisionCode(payload.Code, CommonServiceDecisionCodes.Ready.Value), nameof(payload));
                break;
        }
    }

    private static bool IsDecisionCode(int code, int expectedCommon) =>
        code == expectedCommon || code >= ServiceDecisionCode.FirstFeatureCode;

    private static bool IsActionCode(int code, int expectedCommon) =>
        code == expectedCommon || code >= ServiceActionResultCode.FirstFeatureCode;
}
