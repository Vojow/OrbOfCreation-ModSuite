using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class AutoItemsActionResultCodes
{
    internal static ServiceActionResultCode ActionFamilyUnavailable => new(1100);
    internal static ServiceActionResultCode ItemUnavailable => new(1101);
    internal static ServiceActionResultCode FamilyChanged => new(1102);
    internal static ServiceActionResultCode NativeBusy => new(1103);
    internal static ServiceActionResultCode NotAdmissible => new(1104);
    internal static ServiceActionResultCode RandomizationUnavailable => new(1105);
    internal static ServiceActionResultCode MutationPermitUnavailable => new(1107);
    internal static ServiceActionResultCode TemporaryEffectPresent => new(1108);
    internal static ServiceActionResultCode TemporaryCostChanged => new(1109);
    internal static ServiceActionResultCode TargetUnavailable => new(1110);
    internal static ServiceActionResultCode ContractUnavailable => new(1111);
    internal static ServiceActionResultCode MultiBuyUnavailable => new(1112);
    internal static ServiceActionResultCode Quarantined => new(1113);
}
