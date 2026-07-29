using System;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbMentor;

namespace OrbAutomata;

/// <summary>
/// Owns the suite's one saved configuration reading and publishes each committed change once.
/// </summary>
/// <remarks>
/// <para>
/// BepInEx may raise <c>SettingChanged</c> from its file watcher, so the configuration object only
/// records that a fresh immutable reading is pending. This main-thread publisher consumes that
/// reading and hands it to the application boundary exactly once.
/// </para>
/// <para>
/// Quick controls call this synchronously after changing their entry. That keeps the service-cycle
/// publication and configured UI intent on the same path; the ordinary frame pump calls the same
/// method for Mods-tab, external-file, and other writers.
/// </para>
/// </remarks>
internal sealed class AutomataConfigurationStore
{
    private readonly BepInExAutomataConfiguration _configuration;
    private readonly Action<SuiteRuntimeConfiguration, ConfigGeneration> _publish;

    internal AutomataConfigurationStore(
        BepInExAutomataConfiguration configuration,
        Action<SuiteRuntimeConfiguration, ConfigGeneration> publish)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _publish = publish ?? throw new ArgumentNullException(nameof(publish));
        Current = configuration.TryTakeUnpublishedChange(out var initial)
            ? initial
            : configuration.Current;
        CurrentGeneration = new ConfigGeneration(1);
    }

    internal SuiteRuntimeConfiguration Current { get; private set; }
    internal ConfigGeneration CurrentGeneration { get; private set; }

    internal void PublishPending() => TryPublishPending();

    internal bool TryPublishPending()
    {
        if (!_configuration.TryTakeUnpublishedChange(out var snapshot)) return false;
        CurrentGeneration = CurrentGeneration.Next();
        Current = snapshot;
        _publish(snapshot, CurrentGeneration);
        return true;
    }

    internal void ToggleAutoBuy()
    {
        _configuration.SetAutoBuyMode(
            Current.AutoBuy.Mode == AutoBuyOperationMode.Active
                ? AutoBuyOperationMode.Disabled
                : AutoBuyOperationMode.Active);
        PublishPending();
    }

    internal bool DisableAutoBuy()
    {
        if (Current.AutoBuy.Mode == AutoBuyOperationMode.Disabled) return false;
        _configuration.SetAutoBuyMode(AutoBuyOperationMode.Disabled);
        PublishPending();
        return true;
    }

    internal void ToggleAutoCast()
    {
        _configuration.SetAutoCastMode(
            Current.AutoCast.Mode == AutoCastOperationMode.Active
                ? AutoCastOperationMode.Disabled
                : AutoCastOperationMode.Active);
        PublishPending();
    }

    internal void ToggleAutoConcept()
    {
        _configuration.SetAutoConceptMode(
            Current.AutoConcept.Mode == AutoConceptOperationMode.Active
                ? AutoConceptOperationMode.Disabled
                : AutoConceptOperationMode.Active);
        PublishPending();
    }

    internal void ToggleAutoHarvest()
    {
        _configuration.SetAutoHarvestMode(
            Current.AutoHarvest.Mode == AutoHarvestOperationMode.Active
                ? AutoHarvestOperationMode.Disabled
                : AutoHarvestOperationMode.Active);
        PublishPending();
    }

    internal void ToggleMentor()
    {
        _configuration.SetMentorMode(
            Current.Mentor.Mode == MentorOperationMode.Active
                ? MentorOperationMode.Disabled
                : MentorOperationMode.Active);
        PublishPending();
    }

    internal void SetEmergencyStop(bool stopped)
    {
        if (Current.Safety.EmergencyDisable == stopped) return;
        _configuration.SetEmergencyStop(stopped);
        PublishPending();
    }
}
