using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;

internal static class DecisionJournalProjection
{
    internal static bool Equals(
        in ServiceStateProjectionSnapshot left,
        in ServiceStateProjectionSnapshot right)
    {
        if (left.Count != right.Count) return false;
        for (var index = 0; index < left.Count; index++)
        {
            var leftEntry = left.GetEntry(index);
            var rightEntry = right.GetEntry(index);
            if (leftEntry.Key != rightEntry.Key ||
                leftEntry.Value.Kind != rightEntry.Value.Kind ||
                leftEntry.Value.Integer != rightEntry.Value.Integer ||
                !leftEntry.Value.FloatingPoint.Equals(rightEntry.Value.FloatingPoint))
            {
                return false;
            }
        }
        return true;
    }
}
