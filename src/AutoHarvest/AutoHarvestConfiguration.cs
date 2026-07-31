namespace OrbAutomata;

internal sealed record AutoHarvestConfiguration
{
    internal AutoHarvestOperationMode Mode { get; init; }
    internal bool CollectFruitTrees { get; init; }
    internal bool CollectTreasureTrees { get; init; }
}
