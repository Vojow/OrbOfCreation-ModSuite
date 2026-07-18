namespace OrbAutomata;

internal static class AutoBuyResourceThresholdCrossing
{
    public static bool ShouldWake(
        BigAmount previousQuantity,
        BigAmount currentQuantity,
        BigAmount requiredQuantity)
    {
        return previousQuantity.CompareTo(requiredQuantity) < 0 &&
               currentQuantity.CompareTo(requiredQuantity) >= 0;
    }
}
