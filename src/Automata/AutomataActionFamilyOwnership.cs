using System;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common;

namespace OrbAutomata;

internal sealed class AutomataActionFamilyOwnership : IDisposable
{
    internal const string KnownAutoBuyPluginGuid = "IngoH.OrbOfCreation.AutoBuyOrb";

    private static readonly AutomationActionFamily[] StructureFamilies =
        { AutomationActionFamily.StructurePurchase };
    private static readonly AutomationActionFamily[] UpgradeFamilies =
        { AutomationActionFamily.UpgradePurchase };
    private static readonly AutomationActionFamily[] MultiBuyFamilies =
        { AutomationActionFamily.NativeMultiBuyOverride };
    private static readonly AutomationActionFamily[] CastFamilies =
        { AutomationActionFamily.SpellCast };
    private static readonly AutomationActionFamily[] ConceptFamilies =
        { AutomationActionFamily.ConceptAssignment };
    private static readonly AutomationActionFamily[] SpellLevelFamilies =
        { AutomationActionFamily.SpellLevelPurchase };
    private static readonly AutomationActionFamily[] HarvestFamilies =
        { AutomationActionFamily.HarvestAction };
    private static readonly AutomationActionFamily[] ItemFamilies =
        { AutomationActionFamily.ConsumableUse };
    private static readonly AutomationActionFamily[] KnownExternalFamilies =
        { AutomationActionFamily.StructurePurchase, AutomationActionFamily.NativeMultiBuyOverride };

    private readonly ActionFamilyOwnershipRegistry _registry;
    private ActionFamilyLeaseSet? _structures;
    private ActionFamilyLeaseSet? _upgrades;
    private ActionFamilyLeaseSet? _multiBuy;
    private ActionFamilyLeaseSet? _cast;
    private ActionFamilyLeaseSet? _concept;
    private ActionFamilyLeaseSet? _spellLevel;
    private ActionFamilyLeaseSet? _harvest;
    private ActionFamilyLeaseSet? _items;
    private IDisposable? _knownExternal;
    private int _pluginInventoryCount = -1;
    private long _structuresRetryFrame;
    private long _upgradesRetryFrame;
    private long _multiBuyRetryFrame;
    private long _castRetryFrame;
    private long _conceptRetryFrame;
    private long _spellLevelRetryFrame;
    private long _harvestRetryFrame;
    private long _itemsRetryFrame;

    internal int ClaimAttempts { get; private set; }
    public bool KnownAutoBuyLoaded { get; private set; }

    public AutomataActionFamilyOwnership(ActionFamilyOwnershipRegistry? registry = null) =>
        _registry = registry ?? ActionFamilyOwnershipRegistry.Shared;

    public bool OwnsAutoBuy(AutoBuyCandidateKind kind) => kind switch
    {
        AutoBuyCandidateKind.Structure => _structures?.IsHeld == true,
        AutoBuyCandidateKind.Upgrade =>
            _upgrades?.IsHeld == true && _multiBuy?.IsHeld == true,
        _ => false,
    };

    public AutoBuyCandidateKinds EffectiveAutoBuyOwnership(AutoBuyConfiguration config)
    {
        var owned = AutoBuyCandidateKinds.None;
        if (!config.IncludeStructures || OwnsAutoBuy(AutoBuyCandidateKind.Structure))
            owned |= AutoBuyCandidateKinds.Structures;
        if (!config.IncludeUpgrades || OwnsAutoBuy(AutoBuyCandidateKind.Upgrade))
            owned |= AutoBuyCandidateKinds.Upgrades;
        return owned;
    }

    public bool OwnsCast => _cast?.IsHeld == true;
    public bool OwnsConcept => _concept?.IsHeld == true;
    public bool OwnsSpellLevel => _spellLevel?.IsHeld == true;
    public bool OwnsHarvest => _harvest?.IsHeld == true;
    public bool OwnsItems => _items?.IsHeld == true && _multiBuy?.IsHeld == true;
    public bool TryCaptureHarvestMutationPermit() => _harvest?.TryCaptureMutationPermit() == true;
    public bool TryCaptureItemMutationPermit() =>
        _items?.TryCaptureMutationPermit() == true &&
        _multiBuy?.TryCaptureMutationPermit() == true;

    public void RefreshLoadedPluginInventory(int pluginCount, Func<string, bool> isLoaded)
    {
        if (pluginCount == _pluginInventoryCount) return;
        _pluginInventoryCount = pluginCount;
        var loaded = isLoaded(KnownAutoBuyPluginGuid);
        KnownAutoBuyLoaded = loaded;
        if (loaded && _knownExternal is null)
        {
            _knownExternal = _registry.RegisterKnownExternal(
                new ActionFamilyOwner(
                    new FeatureStatusKey(KnownAutoBuyPluginGuid, "AutoBuy"),
                    "AutobuyOrb"),
                KnownExternalFamilies);
        }
        else if (!loaded && _knownExternal is not null)
        {
            _knownExternal.Dispose();
            _knownExternal = null;
            ResetRetryFrames();
        }
    }

    public void Refresh(SuiteRuntimeConfiguration config, bool lifecycleReady, long frame = 0)
    {
        var suiteReady = lifecycleReady && config.General.Enabled;
        RefreshLease(ref _structures, ref _structuresRetryFrame, frame,
            suiteReady && config.CanStartAutoBuyActively && config.AutoBuy.IncludeStructures,
            "AutoBuy.Structures", "Automata Auto Buy Structures", StructureFamilies);
        RefreshLease(ref _upgrades, ref _upgradesRetryFrame, frame,
            suiteReady && config.CanStartAutoBuyActively && config.AutoBuy.IncludeUpgrades,
            "AutoBuy.Upgrades", "Automata Auto Buy Upgrades", UpgradeFamilies);
        RefreshLease(ref _multiBuy, ref _multiBuyRetryFrame, frame,
            suiteReady &&
            (config.CanStartAutoBuyActively && config.AutoBuy.IncludeUpgrades ||
             config.CanStartAutoItemsActively &&
             AutoItemsConfigurationPolicy.HasEnabledFamily(config.AutoItems)),
            "NativeMultiBuy", "Automata Native Multi-Buy Scope", MultiBuyFamilies);
        RefreshLease(ref _cast, ref _castRetryFrame, frame,
            suiteReady && config.CanStartAutoCastActively,
            "AutoCast", "Automata Auto Cast", CastFamilies);
        RefreshLease(ref _concept, ref _conceptRetryFrame, frame,
            suiteReady && config.CanStartAutoConceptActively,
            "AutoConcept", "Automata Auto Concept", ConceptFamilies);
        RefreshLease(ref _spellLevel, ref _spellLevelRetryFrame, frame,
            suiteReady && config.CanStartAutoBuyActively && config.AutoBuy.AutoLevelSpells,
            "SpellLevel", "Automata Spell Leveling", SpellLevelFamilies);
        RefreshLease(ref _harvest, ref _harvestRetryFrame, frame,
            suiteReady && config.CanStartAutoHarvestActively &&
            (config.AutoHarvest.CollectFruitTrees || config.AutoHarvest.CollectTreasureTrees),
            "AutoHarvest", "Automata Auto Harvest", HarvestFamilies);
        RefreshLease(ref _items, ref _itemsRetryFrame, frame,
            suiteReady && config.CanStartAutoItemsActively &&
            AutoItemsConfigurationPolicy.HasEnabledFamily(config.AutoItems),
            "AutoItems", "Automata Auto Items", ItemFamilies);
    }

    public void ReleaseLifecycleClaims()
    {
        Release(ref _items);
        Release(ref _harvest);
        Release(ref _spellLevel);
        Release(ref _concept);
        Release(ref _cast);
        Release(ref _upgrades);
        Release(ref _multiBuy);
        Release(ref _structures);
    }

    private void RefreshLease(
        ref ActionFamilyLeaseSet? lease,
        ref long nextRetryFrame,
        long frame,
        bool shouldOwn,
        string featureId,
        string displayName,
        AutomationActionFamily[] families)
    {
        if (lease is not null && !lease.IsHeld)
        {
            lease.Dispose();
            lease = null;
        }
        if (!shouldOwn)
        {
            Release(ref lease);
            nextRetryFrame = 0;
            return;
        }
        if (lease is not null || frame < nextRetryFrame) return;
        ClaimAttempts++;
        if (!_registry.TryClaimSet(
            new ActionFamilyOwner(new FeatureStatusKey(PluginIds.SuiteGuid, featureId), displayName),
            families,
            out lease,
            out _))
            nextRetryFrame = frame + 60;
        else
            nextRetryFrame = 0;
    }

    private static void Release(ref ActionFamilyLeaseSet? lease)
    {
        lease?.Dispose();
        lease = null;
    }

    public void Dispose()
    {
        ReleaseLifecycleClaims();
        _knownExternal?.Dispose();
        _knownExternal = null;
    }

    private void ResetRetryFrames()
    {
        _structuresRetryFrame = 0;
        _upgradesRetryFrame = 0;
        _multiBuyRetryFrame = 0;
        _castRetryFrame = 0;
        _conceptRetryFrame = 0;
        _spellLevelRetryFrame = 0;
        _harvestRetryFrame = 0;
        _itemsRetryFrame = 0;
    }
}
