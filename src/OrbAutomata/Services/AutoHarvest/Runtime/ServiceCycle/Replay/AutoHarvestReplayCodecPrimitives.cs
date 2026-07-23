using System;

namespace OrbAutomata;

internal static class AutoHarvestReplayCodecPrimitives
{
    public static void RequireLength(int actual, int expected)
    {
        if (actual != expected)
            throw new ArgumentException("The Auto Harvest replay record has an invalid encoded length.");
    }

    public static void RequireCapacity(int actual, int required)
    {
        if (actual < required)
            throw new ArgumentException("The Auto Harvest replay destination is too small.");
    }

    public static byte WriteBool(bool value) => value ? (byte)1 : (byte)0;

    public static bool ReadBool(byte value) => value switch
    {
        0 => false,
        1 => true,
        _ => throw new ArgumentException("The Auto Harvest replay boolean is invalid."),
    };

    public static AutoHarvestPair ReadPair(byte value) => value switch
    {
        (byte)AutoHarvestPair.FruitTree => AutoHarvestPair.FruitTree,
        (byte)AutoHarvestPair.TreasureTree => AutoHarvestPair.TreasureTree,
        _ => throw new ArgumentException("The Auto Harvest replay pair is invalid."),
    };
}
