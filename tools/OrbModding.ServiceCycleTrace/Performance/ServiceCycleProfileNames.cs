#if SERVICE_CYCLE_PROFILE
using System.Globalization;
using OrbAutomata.Runtime.ServiceCycle.Profile;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;

namespace OrbModding.ServiceCycleTrace.Performance;

internal static class ServiceCycleProfileNames
{
    internal static string Stage(int stage) => stage switch
    {
        ServiceCycleProfileCommonStageCodes.DetachedInputConstruction => "Detached input construction",
        ServiceCycleProfileCommonStageCodes.DetachedInputBridgePublication => "Detached input publication",
        ServiceCycleProfileCommonStageCodes.SemanticStart => "Semantic start emission",
        ServiceCycleProfileCommonStageCodes.SemanticTerminal => "Semantic terminal emission",
        ServiceCycleProfileCommonStageCodes.SemanticPumpSummary => "Semantic pump summary",
        ServiceCycleProfileCommonStageCodes.OverallPump => "Overall pump",
        AutoHarvestServiceCycleProfileStageCodes.BindingAndCoherence => "Auto Harvest binding/coherence",
        AutoHarvestServiceCycleProfileStageCodes.ActiveActionTraversal => "Auto Harvest active-action traversal",
        AutoHarvestServiceCycleProfileStageCodes.FruitFactCapture => "Auto Harvest fruit facts",
        AutoHarvestServiceCycleProfileStageCodes.TreasureFactCapture => "Auto Harvest treasure facts",
        AutoHarvestServiceCycleProfileStageCodes.FrameAssemblyAndOwnershipProjection =>
            "Auto Harvest frame/ownership assembly",
        AutoHarvestServiceCycleProfileStageCodes.ActionFactRevalidation =>
            "Auto Harvest action fact revalidation",
        AutoHarvestServiceCycleProfileStageCodes.ActionBeforeSnapshot =>
            "Auto Harvest action before snapshot",
        AutoHarvestServiceCycleProfileStageCodes.ActionNativeSubmission =>
            "Auto Harvest native submission",
        AutoHarvestServiceCycleProfileStageCodes.ActionAfterSnapshot =>
            "Auto Harvest after snapshot",
        AutoHarvestServiceCycleProfileStageCodes.ActionPostconditionVerification =>
            "Auto Harvest postcondition verification",
        _ => "Stage " + stage.ToString(CultureInfo.InvariantCulture),
    };
}
#endif
