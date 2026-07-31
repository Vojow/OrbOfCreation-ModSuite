using System;
using System.Collections.Generic;
using System.Linq;
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

    public ModConfigFeatureCommands(AutomationFeatureControlRegistry registry)
    {
        if (registry is null) throw new ArgumentNullException(nameof(registry));
        _bySection = registry.Features.ToDictionary(
            registration => registration.PageLabel,
            registration => new ModConfigFeatureCommand(
                registration.DisplayName,
                () => registration.Status,
                registration.Toggle),
            StringComparer.Ordinal);
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
