using BepInEx.Configuration;
using OrbModding.Common;

namespace OrbAutomata;

internal sealed class BepInExAutomataReplayConfiguration
{
    private BepInExAutomataReplayConfiguration(ConfigEntry<bool> enableAutoHarvestCapture) =>
        EnableAutoHarvestCapture = enableAutoHarvestCapture;

    public ConfigEntry<bool> EnableAutoHarvestCapture { get; }

    public static BepInExAutomataReplayConfiguration Bind(ConfigFile config) => new(
        config.Bind(
            "Diagnostics",
            "EnableAutoHarvestReplayCapture",
            false,
            new ConfigDescription(
                "Write one finite Auto Harvest ServiceCycle replay artifact at the first action, lifecycle boundary, or capture-window limit. The BepInEx log reports durable completion. Requires restart.",
                null,
                new ModConfigMetadata(
                    20,
                    30,
                    hidden: false,
                    displaySection: "Advanced",
                    displayName: "Auto Harvest replay capture",
                    restartRequired: true))));
}
