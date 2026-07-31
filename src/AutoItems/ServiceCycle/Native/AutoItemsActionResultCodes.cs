using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class AutoItemsActionResultCodes
{
    internal static ServiceActionResultCode ActionFamilyUnavailable => new(4096);
    internal static ServiceActionResultCode ItemUnavailable => new(4097);
    internal static ServiceActionResultCode FamilyChanged => new(4098);
    internal static ServiceActionResultCode NativeBusy => new(4099);
    internal static ServiceActionResultCode NotVisible => new(4100);
    internal static ServiceActionResultCode CanFireRefused => new(4101);
    internal static ServiceActionResultCode RandomizationUnavailable => new(4102);
    internal static ServiceActionResultCode MutationPermitUnavailable => new(4103);
    internal static ServiceActionResultCode TargetUnavailable => new(4104);
    internal static ServiceActionResultCode ContractUnavailable => new(4105);
    internal static ServiceActionResultCode MultiBuyUnavailable => new(4106);
    internal static ServiceActionResultCode Quarantined => new(4107);
    internal static ServiceActionResultCode TemporaryDurationChanged => new(4108);
    internal static ServiceActionResultCode TemporaryCostChanged => new(4109);
    internal static ServiceActionResultCode TemporaryEffectPresent => new(4110);
    internal static ServiceActionResultCode PublicationGap => new(4111);
    internal static ServiceActionResultCode AudioUnavailable => new(4112);
}
