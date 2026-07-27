using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.ServiceCycleTrace.Journal;

internal static class DecisionJournalValueNames
{
    internal static string Decision(int code) => code switch
    {
        0 => "Unavailable",
        1 => nameof(CommonServiceDecisionCodes.Ready),
        2 => nameof(CommonServiceDecisionCodes.Captured),
        3 => nameof(CommonServiceDecisionCodes.NotReady),
        4 => nameof(CommonServiceDecisionCodes.CaptureUnavailable),
        5 => nameof(CommonServiceDecisionCodes.TransientContention),
        _ => "Feature",
    };

    internal static string Action(int code) => code switch
    {
        0 => "Unavailable",
        1 => nameof(CommonActionResultCodes.Committed),
        2 => nameof(CommonActionResultCodes.EmergencyStop),
        3 => nameof(CommonActionResultCodes.LifecycleReplaced),
        4 => nameof(CommonActionResultCodes.ServiceDisabled),
        5 => nameof(CommonActionResultCodes.NativeRejected),
        6 => nameof(CommonActionResultCodes.PolicyRejected),
        7 => nameof(CommonActionResultCodes.AdapterFault),
        _ => "Feature",
    };

    internal static string Lifecycle(int code) => code switch
    {
        1 => "Requested",
        2 => "Activated",
        _ => "Unknown",
    };

    internal static string WorldGate(int code) => code switch
    {
        1 => "WorldBehindLastAction",
        2 => "WorldUnanswered",
        _ => "Unknown",
    };

    internal static string Emergency(int code) => code switch
    {
        (int)EmergencyStopReason.UserRequested => nameof(EmergencyStopReason.UserRequested),
        (int)EmergencyStopReason.SafetyInterlock => nameof(EmergencyStopReason.SafetyInterlock),
        (int)EmergencyStopReason.SuiteShutdown => nameof(EmergencyStopReason.SuiteShutdown),
        _ => "Unknown",
    };
}
