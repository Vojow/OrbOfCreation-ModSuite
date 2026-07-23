#if SERVICE_CYCLE_PROFILE
namespace OrbAutomata.Runtime.ServiceCycle.Profile;

internal static class AutoHarvestServiceCycleProfileStageCodes
{
    internal const int BindingAndCoherence = 1001;
    internal const int ActiveActionTraversal = 1002;
    internal const int FruitFactCapture = 1003;
    internal const int TreasureFactCapture = 1004;
    internal const int FrameAssemblyAndOwnershipProjection = 1005;
    internal const int ActionFactRevalidation = 1006;
    internal const int ActionBeforeSnapshot = 1007;
    internal const int ActionNativeSubmission = 1008;
    internal const int ActionAfterSnapshot = 1009;
    internal const int ActionPostconditionVerification = 1010;
}
#endif
