using System;
using System.Collections.Generic;
using System.Linq;
using OrbAutomata;
using OrbModding.Common;

namespace OrbModConfig;

internal readonly record struct ModConfigFeatureCommandPresentation(
    string StatusText,
    bool IsActive,
    string ButtonLabel);

internal sealed class ModConfigFeatureCommand
{
    private readonly Func<ModConfigFeatureCommandPresentation> _readPresentation;
    private readonly Action _toggle;

    public ModConfigFeatureCommand(
        string displayName,
        Func<ModConfigFeatureCommandPresentation> readPresentation,
        Action toggle)
    {
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        _readPresentation = readPresentation ??
            throw new ArgumentNullException(nameof(readPresentation));
        _toggle = toggle ?? throw new ArgumentNullException(nameof(toggle));
    }

    public string DisplayName { get; }
    public ModConfigFeatureCommandPresentation Presentation => _readPresentation();
    public void Toggle() => _toggle();

    internal static ModConfigFeatureCommand FromFeature(
        AutomationFeatureControlRegistration registration) =>
        new(
            registration.DisplayName,
            () =>
            {
                var status = registration.Status;
                var presentation = FeatureStatusPresenter.Present(status);
                return new ModConfigFeatureCommandPresentation(
                    FeatureStatusPresenter.Format(status),
                    presentation.IsConfiguredOn,
                    presentation.IsConfiguredOn ? "Turn off" : "Turn on");
            },
            registration.Toggle);

    internal static ModConfigFeatureCommand FromEmergencyStop(
        EmergencyStopControl control) =>
        new(
            "Suite emergency stop",
            () => control.IsStopped
                ? new ModConfigFeatureCommandPresentation(
                    "Emergency stop: Engaged\nAll suite automation is stopped.",
                    true,
                    "Resume all")
                : new ModConfigFeatureCommandPresentation(
                    "Emergency stop: Clear\nConfigured automation may run.",
                    false,
                    "Stop all"),
            control.Activate);
}

internal sealed class ModConfigFeatureCommands
{
    private readonly IReadOnlyDictionary<string, ModConfigFeatureCommand> _bySection;

    public ModConfigFeatureCommands(
        AutomationFeatureControlRegistry registry,
        EmergencyStopControl emergencyStop)
    {
        if (registry is null) throw new ArgumentNullException(nameof(registry));
        if (emergencyStop is null) throw new ArgumentNullException(nameof(emergencyStop));
        var commands = registry.Features.ToDictionary(
            registration => registration.PageLabel,
            ModConfigFeatureCommand.FromFeature,
            StringComparer.Ordinal);
        commands.Add("General", ModConfigFeatureCommand.FromEmergencyStop(emergencyStop));
        _bySection = commands;
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
