using System;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;

namespace OrbModConfig;

internal sealed class ModConfigFeatureCommand
{
    private readonly Func<FeatureStatusSnapshot> _readStatus;
    private readonly Action _toggle;

    public ModConfigFeatureCommand(
        string displayName,
        Func<FeatureStatusSnapshot> readStatus,
        Action toggle)
    {
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        _readStatus = readStatus ?? throw new ArgumentNullException(nameof(readStatus));
        _toggle = toggle ?? throw new ArgumentNullException(nameof(toggle));
    }

    public string DisplayName { get; }
    public FeatureStatusSnapshot Status => _readStatus();
    public void Toggle() => _toggle();
}

internal sealed class ModConfigFeatureCommands
{
    private readonly IReadOnlyDictionary<string, ModConfigFeatureCommand> _bySection;

    public ModConfigFeatureCommands(
        AutomataConfigurationStore store,
        AutomataFeatureStatuses statuses)
    {
        if (store is null) throw new ArgumentNullException(nameof(store));
        if (statuses is null) throw new ArgumentNullException(nameof(statuses));
        _bySection = new Dictionary<string, ModConfigFeatureCommand>(StringComparer.Ordinal)
        {
            ["Auto Buy"] = new("Auto Buy", () => statuses.AutoBuy.Current, store.ToggleAutoBuy),
            ["Auto Cast"] = new("Auto Cast", () => statuses.AutoCast.Current, store.ToggleAutoCast),
            ["Auto Concept"] = new("Auto Concept", () => statuses.AutoConcept.Current, store.ToggleAutoConcept),
            ["Auto Harvest"] = new("Auto Harvest", () => statuses.AutoHarvest.Current, store.ToggleAutoHarvest),
            ["Mentor"] = new("Orb Mentor", () => statuses.Mentor.Current, store.ToggleMentor),
        };
    }

    public bool TryGet(string pluginGuid, string sectionName, out ModConfigFeatureCommand command)
    {
        if (!string.Equals(pluginGuid, PluginIds.SuiteGuid, StringComparison.Ordinal))
        {
            command = null!;
            return false;
        }
        return _bySection.TryGetValue(sectionName, out command!);
    }
}
