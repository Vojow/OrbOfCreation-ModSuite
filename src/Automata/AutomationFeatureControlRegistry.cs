using System;
using System.Collections.Generic;
using System.Linq;
using OrbMentor;
using OrbModding.Common;

namespace OrbAutomata;

internal sealed class AutomationFeatureControlRegistration
{
    private readonly Func<FeatureStatusSnapshot> _readStatus;
    private readonly Action _toggle;

    internal AutomationFeatureControlRegistration(
        string featureId,
        string pageLabel,
        string displayName,
        ITooltipable tooltip,
        Func<FeatureStatusSnapshot> readStatus,
        Action toggle)
    {
        FeatureId = RequireText(featureId, nameof(featureId));
        PageLabel = RequireText(pageLabel, nameof(pageLabel));
        DisplayName = RequireText(displayName, nameof(displayName));
        Tooltip = tooltip ?? throw new ArgumentNullException(nameof(tooltip));
        _readStatus = readStatus ?? throw new ArgumentNullException(nameof(readStatus));
        _toggle = toggle ?? throw new ArgumentNullException(nameof(toggle));
    }

    internal string FeatureId { get; }
    internal string PageLabel { get; }
    internal string DisplayName { get; }
    internal ITooltipable Tooltip { get; }
    internal FeatureStatusSnapshot Status => _readStatus();
    internal void Toggle() => _toggle();

    private static string RequireText(string value, string parameterName)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0
            ? throw new ArgumentException("A non-empty value is required.", parameterName)
            : normalized;
    }
}

/// <summary>
/// The one registered automation-feature control roster shared by the Mods headers and the
/// gameplay quick-controls column. Construction consumers enumerate this roster; they do not
/// maintain their own feature lists.
/// </summary>
internal sealed class AutomationFeatureControlRegistry
{
    private readonly IReadOnlyList<AutomationFeatureControlRegistration> _features;
    private readonly IReadOnlyDictionary<string, AutomationFeatureControlRegistration> _byPage;

    internal AutomationFeatureControlRegistry(
        IEnumerable<AutomationFeatureControlRegistration> features)
    {
        if (features is null) throw new ArgumentNullException(nameof(features));
        var materialized = features.ToArray();
        if (materialized.Length == 0)
            throw new ArgumentException("At least one automation feature is required.", nameof(features));
        if (materialized.Select(feature => feature.FeatureId).Distinct(StringComparer.Ordinal).Count() !=
            materialized.Length)
            throw new ArgumentException("Automation feature IDs must be unique.", nameof(features));
        if (materialized.Select(feature => feature.PageLabel).Distinct(StringComparer.Ordinal).Count() !=
            materialized.Length)
            throw new ArgumentException("Automation feature page labels must be unique.", nameof(features));
        _features = materialized;
        _byPage = materialized.ToDictionary(feature => feature.PageLabel, StringComparer.Ordinal);
    }

    internal IReadOnlyList<AutomationFeatureControlRegistration> Features => _features;

    internal bool TryGet(
        string pageLabel,
        out AutomationFeatureControlRegistration registration) =>
        _byPage.TryGetValue(pageLabel, out registration!);

    internal static AutomationFeatureControlRegistry Create(
        AutomataConfigurationStore configuration,
        AutomataFeatureStatuses statuses,
        SpellLevelCapabilityState spellLevelCapability,
        MentorConfig mentor)
    {
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));
        if (statuses is null) throw new ArgumentNullException(nameof(statuses));
        if (spellLevelCapability is null) throw new ArgumentNullException(nameof(spellLevelCapability));
        if (mentor is null) throw new ArgumentNullException(nameof(mentor));

        var autoBuy = new AutoBuyToggleControl(
            configuration,
            () => spellLevelCapability.Current,
            () => statuses.AutoBuy.Current,
            () => statuses.SpellLevel.Current);
        var autoCast = new AutoCastToggleControl(
            configuration,
            () => statuses.AutoCast.Current);
        var autoConcept = new AutoConceptToggleControl(
            configuration,
            () => statuses.AutoConcept.Current);
        var autoHarvest = new AutoHarvestToggleControl(
            configuration,
            () => statuses.AutoHarvest.Current);
        var autoItems = new AutoItemsToggleControl(
            configuration,
            () => statuses.AutoItems.Current);
        var autoScribe = new AutoScribeToggleControl(
            configuration,
            () => statuses.AutoScribe.Current);

        return new AutomationFeatureControlRegistry(new[]
        {
            new AutomationFeatureControlRegistration(
                AutomataFeatureStatuses.AutoBuyFeatureId,
                "Auto Buy",
                "Auto Buy",
                new AutoBuyTooltip(autoBuy),
                () => statuses.AutoBuy.Current,
                autoBuy.Toggle),
            new AutomationFeatureControlRegistration(
                AutomataFeatureStatuses.AutoCastFeatureId,
                "Auto Cast",
                "Auto Cast",
                new AutoCastTooltip(autoCast),
                () => statuses.AutoCast.Current,
                autoCast.Toggle),
            new AutomationFeatureControlRegistration(
                AutomataFeatureStatuses.AutoConceptFeatureId,
                "Auto Concept",
                "Auto Concept",
                new AutoConceptTooltip(autoConcept),
                () => statuses.AutoConcept.Current,
                autoConcept.Toggle),
            new AutomationFeatureControlRegistration(
                AutomataFeatureStatuses.AutoHarvestFeatureId,
                "Auto Harvest",
                "Auto Harvest",
                new AutoHarvestTooltip(autoHarvest),
                () => statuses.AutoHarvest.Current,
                autoHarvest.Toggle),
            new AutomationFeatureControlRegistration(
                AutomataFeatureStatuses.MentorFeatureId,
                "Mentor",
                "Orb Mentor",
                new MentorTooltip(mentor, () => statuses.Mentor.Current),
                () => statuses.Mentor.Current,
                configuration.ToggleMentor),
            new AutomationFeatureControlRegistration(
                AutomataFeatureStatuses.AutoItemsFeatureId,
                "Auto Items",
                "Auto Items",
                new AutoItemsTooltip(autoItems),
                () => statuses.AutoItems.Current,
                autoItems.Toggle),
            new AutomationFeatureControlRegistration(
                AutomataFeatureStatuses.AutoScribeFeatureId,
                "Auto Scribe",
                "Auto Scribe",
                new AutoScribeTooltip(autoScribe),
                () => statuses.AutoScribe.Current,
                autoScribe.Toggle),
        });
    }
}
