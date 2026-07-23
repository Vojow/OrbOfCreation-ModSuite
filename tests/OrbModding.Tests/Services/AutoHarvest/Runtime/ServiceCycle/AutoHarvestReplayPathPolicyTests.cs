using Xunit;

namespace OrbAutomata.Tests;

public sealed class AutoHarvestReplayPathPolicyTests
{
    [Fact]
    public void ReportedArtifactPathIsStableAndMachineRelative()
    {
        Assert.Equal(
            "BepInEx/config/OrbOfCreation-ModSuite/replay/auto-harvest/auto-harvest-000042.oscr",
            AutoHarvestReplayPathPolicy.FormatRelativeArtifactPath(42));
    }
}
