using System.Collections.Generic;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

/// <summary>Immutable cycle-completeness projection shared by manifest and fence calculations.</summary>
internal sealed class ServiceCycleReplayCompletenessIndex
{
    private readonly Dictionary<ServiceCycleReplayCycleKey, bool> _completeByCycle;

    private ServiceCycleReplayCompletenessIndex(
        Dictionary<ServiceCycleReplayCycleKey, bool> completeByCycle)
    {
        _completeByCycle = completeByCycle;
    }

    internal static ServiceCycleReplayCompletenessIndex Build(
        ServiceCycleReplayArtifactFooter[] footers,
        ServiceCycleReplayFormatWorkCounter? work = null)
    {
        var result = new Dictionary<ServiceCycleReplayCycleKey, bool>(footers.Length);
        for (var index = 0; index < footers.Length; index++)
        {
            work?.Add();
            result.TryAdd(footers[index].Context.Cycle, footers[index].IsComplete);
        }
        return new ServiceCycleReplayCompletenessIndex(result);
    }

    internal bool IsComplete(
        ServiceCycleReplayCycleKey cycle,
        ServiceCycleReplayFormatWorkCounter? work = null)
    {
        work?.Add();
        return _completeByCycle.TryGetValue(cycle, out var complete) && complete;
    }
}
