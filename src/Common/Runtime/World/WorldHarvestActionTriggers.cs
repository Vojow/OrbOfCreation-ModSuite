using System.Threading;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// Process-local monotonic evidence that a native event capable of changing
/// Druidry balance completed. World collection is the only consumer; workers
/// see the copied epoch values on <see cref="GameWorldState"/>.
/// </summary>
internal static class WorldHarvestActionTriggerSource
{
    private static long _plotActionEpoch;
    private static long _verifiedHarvestSubmissionEpoch;

    internal static long PlotActionEpoch => Volatile.Read(ref _plotActionEpoch);
    internal static long VerifiedHarvestSubmissionEpoch =>
        Volatile.Read(ref _verifiedHarvestSubmissionEpoch);

    internal static long AdvancePlotAction() =>
        Interlocked.Increment(ref _plotActionEpoch);

    internal static long AdvanceVerifiedHarvestSubmission() =>
        Interlocked.Increment(ref _verifiedHarvestSubmissionEpoch);
}
