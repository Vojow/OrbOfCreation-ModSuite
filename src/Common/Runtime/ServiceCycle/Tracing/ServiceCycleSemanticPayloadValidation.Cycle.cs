using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Tracing;

internal static partial class ServiceCycleSemanticPayloadValidation
{
    private static void ValidateCycleAndEvaluation(
        ServiceCycleSemanticEventKind kind,
        in ServiceCycleSemanticPayload payload)
    {
        switch (kind)
        {
            case ServiceCycleSemanticEventKind.CycleQueued:
                Require(IsDecisionCode(payload.Code, CommonServiceDecisionCodes.Ready.Value), nameof(payload));
                break;
            case ServiceCycleSemanticEventKind.CycleStarted:
            case ServiceCycleSemanticEventKind.CycleCompleted:
                Require(payload.Code == 0, nameof(payload));
                break;
            case ServiceCycleSemanticEventKind.CycleOrphaned:
                Require(payload.Code == CommonActionResultCodes.LifecycleReplaced.Value, nameof(payload));
                break;
            case ServiceCycleSemanticEventKind.CycleFaulted:
            case ServiceCycleSemanticEventKind.EvaluationFaulted:
                Require(IsActionCode(payload.Code, CommonActionResultCodes.AdapterFault.Value), nameof(payload));
                break;
            case ServiceCycleSemanticEventKind.CaptureStarted:
                Require(payload.Code == 0 && payload.ActionCount == 0 && payload.Strategy == 0, nameof(payload));
                break;
            case ServiceCycleSemanticEventKind.CaptureCompleted:
                Require(IsDecisionCode(payload.Code, CommonServiceDecisionCodes.Captured.Value) &&
                    payload.ActionCount == 0 && payload.Strategy != 0,
                    nameof(payload));
                break;
            case ServiceCycleSemanticEventKind.CaptureUnavailable:
                Require(IsDecisionCode(payload.Code, CommonServiceDecisionCodes.CaptureUnavailable.Value) &&
                    payload.ActionCount == 0 && payload.Strategy == 0 && payload.TryGetReturnedWake(out var captureWake) &&
                    captureWake.Kind is WakePolicyKind.AfterDecision or WakePolicyKind.At or
                        WakePolicyKind.OnPublication, nameof(payload));
                break;
            case ServiceCycleSemanticEventKind.CaptureFaulted:
                Require(IsActionCode(payload.Code, CommonActionResultCodes.AdapterFault.Value) &&
                    payload.ActionCount == 0 && payload.Strategy == 0, nameof(payload));
                break;
            case ServiceCycleSemanticEventKind.EvaluationStarted:
                Require(payload.Code == 0 && payload.ActionCount == 0, nameof(payload));
                break;
            case ServiceCycleSemanticEventKind.EvaluationCompleted:
                Require(payload.Code == 0 && payload.TryGetReturnedWake(out _), nameof(payload));
                break;
            case ServiceCycleSemanticEventKind.ProjectionFaulted:
                Require(IsActionCode(payload.Code, CommonActionResultCodes.AdapterFault.Value) &&
                    payload.TryGetReturnedWake(out _), nameof(payload));
                break;
            case ServiceCycleSemanticEventKind.EvaluationDeferred:
                Require(payload.Code == CommonServiceDecisionCodes.TransientContention.Value &&
                    payload.ActionCount == 0 && payload.DeadlineTicks >= payload.TimestampTicks,
                    nameof(payload));
                break;
        }
    }
}
