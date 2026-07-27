using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using BepInEx.Logging;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutoCastTests
{
    [Fact]
    public void FreshConfigUsesZeroResourceThreshold()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());

        Assert.Equal(0.0f, config.AutoCastStartResourcePercent.Value);
    }

    [Fact]
    public void FreshConfigFullChargesChargedSpellsByDefault()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());

        Assert.True(config.AutoCastFullCharge.Value);
    }

    [Fact]
    public void ExistingResourceThresholdIsPreserved()
    {
        var file = new ConfigFile();
        file.Bind("AutoCast", "StartResourcePercent", 80.0f, "existing").Value = 37.0f;

        var config = BepInExAutomataConfiguration.Bind(file);

        Assert.Equal(37.0f, config.AutoCastStartResourcePercent.Value);
    }

    [Theory]
    [InlineData(0, "AC OFF")]
    [InlineData(1, "AC ON")]
    public void CompactToggleUsesConsistentAutoCastLabels(int state, string expected)
    {
        Assert.Equal(expected, AutoCastToggleButton.FormatLabel((AutoCastToggleVisualState)state));
    }

    [Fact]
    public void ToggleSwitchesBetweenDisabledAndActive()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        var toggle = new AutoCastToggleControl(config);

        Assert.Equal(AutoCastToggleVisualState.Off, toggle.State);
        toggle.Toggle();
        Assert.Equal(AutoCastOperationMode.Active, config.AutoCastMode.Value);
        Assert.Equal(AutoCastToggleVisualState.On, toggle.State);

        toggle.Toggle();
        Assert.Equal(AutoCastToggleVisualState.Off, toggle.State);
        toggle.Toggle();
        Assert.Equal(AutoCastToggleVisualState.On, toggle.State);
    }

    [Fact]
    public void EmergencyDisableKeepsConfiguredIntentVisuallyOn()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.AutoCastMode.Value = AutoCastOperationMode.Active;
        var toggle = new AutoCastToggleControl(config);

        Assert.Equal(AutoCastToggleVisualState.On, toggle.State);
        config.EmergencyDisable.Value = true;
        Assert.Equal(AutoCastToggleVisualState.On, toggle.State);
    }

    [Fact]
    public void DefaultsToDisabledAndDoesNotFire()
    {
        var spell = Spell("idle");
        using var fixture = Create(spell);

        fixture.Engine.Tick(1.0f);

        Assert.Equal(AutoCastOperationMode.Disabled, fixture.Config.AutoCastMode.Value);
        Assert.Equal(0, spell.FireCalls);
    }

    [Fact]
    public void ActiveEmptyLoadoutRemainsOperational()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.AutoCastMode.Value = AutoCastOperationMode.Active;
        var registry = new FeatureStatusRegistry();
        using var statuses = new AutomataFeatureStatuses(config.Current, 1, registry);
        using var engine = new AutoCastEngine(
            config,
            new FakeCatalog(),
            new ReservePolicy(config),
            new ResourceFullnessPolicy(),
            new ManualLogSource(),
            () => true,
            featureStatus: statuses.AutoCast);

        engine.Tick(1.0f);

        Assert.Equal(FeatureStatusState.Operational, statuses.AutoCast.Current.State);
        Assert.Equal(AutoCastToggleVisualState.On, AutomataFeatureStatusVisuals.ToVisualState(statuses.AutoCast.Current));
    }

    [Fact]
    public void TypedContractFailureDegradesWithoutParsingDiagnosticText()
    {
        var spell = Spell("typed contract failure");
        spell.CanCastResult = false;
        spell.CanCastReason = "adapter check 17 failed";
        spell.AdmissionFailureKind = AutoCastAdmissionFailureKind.ContractUnavailable;
        using var fixture = CreateWithCoordinator(spell);
        fixture.Config.AutoCastMode.Value = AutoCastOperationMode.Active;

        fixture.Engine.Tick(1.0f);

        Assert.Equal(FeatureStatusState.Degraded, fixture.FeatureStatuses.AutoCast.Current.State);
        Assert.Equal(
            FeatureStatusReasonCode.PartialCapabilityUnavailable,
            fixture.FeatureStatuses.AutoCast.Current.Reason.Code);
    }

    [Fact]
    public void OrdinaryRejectionRemainsOperationalRegardlessOfDiagnosticWording()
    {
        var spell = Spell("ordinary wait");
        spell.CanCastResult = false;
        spell.CanCastReason = "contractually unavailable until recharge";
        spell.AdmissionFailureKind = AutoCastAdmissionFailureKind.OrdinaryRejection;
        using var fixture = CreateWithCoordinator(spell);
        fixture.Config.AutoCastMode.Value = AutoCastOperationMode.Active;

        fixture.Engine.Tick(1.0f);

        Assert.Equal(FeatureStatusState.Operational, fixture.FeatureStatuses.AutoCast.Current.State);
    }

    [Fact]
    public void ActiveModeFullChargesChargedSpellBeforeContinuingRotation()
    {
        var charged = Spell("charged", charged: true);
        charged.IsReadyingCast = true;
        var aura = Spell("active aura", kind: AutoCastSpellKind.Aura, casting: true);
        var first = Spell("first");
        var second = Spell("second");
        using var fixture = Create(charged, aura, first, second);
        fixture.Config.AutoCastMode.Value = AutoCastOperationMode.Active;
        fixture.Config.EnableOperationalLogging.Value = true;

        fixture.Engine.Tick(1.0f);
        fixture.Engine.Tick(1.0f);

        Assert.Equal(new[] { true }, charged.ChargeHoldChanges);
        Assert.Equal(1, charged.FireCalls);
        Assert.Equal(0, first.FireCalls);
        charged.IsReadyingCast = false;
        fixture.Engine.Tick(1.0f);

        Assert.Equal(new[] { true, false }, charged.ChargeHoldChanges);
        Assert.Equal(1, first.FireCalls);
    }

    [Fact]
    public void DisabledFullChargeSettingFiresChargedSpellImmediately()
    {
        var charged = Spell("charged", charged: true);
        using var fixture = Create(charged);
        fixture.Config.AutoCastMode.Value = AutoCastOperationMode.Active;
        fixture.Config.AutoCastFullCharge.Value = false;

        fixture.Engine.Tick(1.0f);

        Assert.Equal(1, charged.FireCalls);
        Assert.Empty(charged.ChargeHoldChanges);
    }

    [Fact]
    public void DecisionLogLevelOffSuppressesOperationalCastLogs()
    {
        var spell = Spell("quiet");
        using var fixture = Create(spell);
        fixture.Config.AutoCastMode.Value = AutoCastOperationMode.Active;
        fixture.Config.EnableOperationalLogging.Value = true;
        fixture.Config.DecisionLogLevel.Value = SuiteDecisionLogLevel.Off;

        fixture.Engine.Tick(1.0f);

        Assert.Equal(1, spell.FireCalls);
        Assert.Empty(fixture.Log.Entries);
    }

    [Fact]
    public void DisablingAutoCastReleasesOwnedFullChargeHold()
    {
        var charged = Spell("charged", charged: true);
        charged.IsReadyingCast = true;
        using var fixture = Create(charged);
        fixture.Config.AutoCastMode.Value = AutoCastOperationMode.Active;
        fixture.Engine.Tick(1.0f);

        fixture.Config.AutoCastMode.Value = AutoCastOperationMode.Disabled;
        fixture.Engine.Tick(0.1f);

        Assert.Equal(new[] { true, false }, charged.ChargeHoldChanges);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void ManualSpellInputReleasesOwnedFullChargeHoldAndPausesRotation()
    {
        var charged = Spell("charged", charged: true);
        charged.IsReadyingCast = true;
        using var fixture = Create(charged);
        fixture.Config.AutoCastMode.Value = AutoCastOperationMode.Active;
        fixture.Engine.Tick(1.0f);

        AutoCastManualSignal.NotifySpellFire();
        fixture.Engine.Tick(0.1f);

        Assert.Equal(new[] { true, false }, charged.ChargeHoldChanges);
        Assert.Equal(1, charged.FireCalls);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void LeavingGameplayReleasesOwnedFullChargeHold()
    {
        var charged = Spell("charged", charged: true);
        charged.IsReadyingCast = true;
        var inGameplay = true;
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.AbsoluteReserve.Value = "0";
        config.RelativeReserveMultiplier.Value = 0.0f;
        config.AutoCastMode.Value = AutoCastOperationMode.Active;
        var catalog = new FakeCatalog(charged);
        using var engine = new AutoCastEngine(
            config,
            catalog,
            new ReservePolicy(config),
            new ResourceFullnessPolicy(),
            new ManualLogSource(),
            () => inGameplay);
        engine.Tick(1.0f);

        inGameplay = false;
        engine.Tick(0.1f);

        Assert.Equal(new[] { true, false }, charged.ChargeHoldChanges);
    }

    [Fact]
    public void TurningOffFullChargeDuringChargeReleasesOwnedHold()
    {
        var charged = Spell("charged", charged: true);
        charged.IsReadyingCast = true;
        using var fixture = Create(charged);
        fixture.Config.AutoCastMode.Value = AutoCastOperationMode.Active;
        fixture.Engine.Tick(1.0f);

        fixture.Config.AutoCastFullCharge.Value = false;
        fixture.Engine.Tick(0.1f);

        Assert.Equal(new[] { true, false }, charged.ChargeHoldChanges);
    }

    [Fact]
    public void EmergencyDisableDuringChargeReleasesOwnedHold()
    {
        var charged = Spell("charged", charged: true);
        charged.IsReadyingCast = true;
        using var fixture = Create(charged);
        fixture.Config.AutoCastMode.Value = AutoCastOperationMode.Active;
        fixture.Engine.Tick(1.0f);

        fixture.Config.EmergencyDisable.Value = true;
        fixture.Engine.Tick(0.1f);

        Assert.Equal(new[] { true, false }, charged.ChargeHoldChanges);
    }

    [Fact]
    public void DisposingEngineDuringChargeReleasesOwnedHold()
    {
        var charged = Spell("charged", charged: true);
        charged.IsReadyingCast = true;
        var fixture = Create(charged);
        fixture.Config.AutoCastMode.Value = AutoCastOperationMode.Active;
        fixture.Engine.Tick(1.0f);

        fixture.Dispose();

        Assert.Equal(new[] { true, false }, charged.ChargeHoldChanges);
    }

    [Fact]
    public void NativeFireFailureAfterAcquiringHoldReleasesIt()
    {
        var charged = Spell("charged", charged: true);
        charged.FireResult = false;
        using var fixture = Create(charged);
        fixture.Config.AutoCastMode.Value = AutoCastOperationMode.Active;

        fixture.Engine.Tick(1.0f);

        Assert.Equal(1, charged.FireCalls);
        Assert.Equal(new[] { true, false }, charged.ChargeHoldChanges);
    }

    [Fact]
    public void NativeHoldFailurePreventsChargedSpellFromFiring()
    {
        var charged = Spell("charged", charged: true);
        charged.HoldStartResult = false;
        using var fixture = Create(charged);
        fixture.Config.AutoCastMode.Value = AutoCastOperationMode.Active;

        fixture.Engine.Tick(1.0f);

        Assert.Equal(0, charged.FireCalls);
        Assert.Equal(new[] { true }, charged.ChargeHoldChanges);
    }

    [Fact]
    public void NativeFireAndHoldReleaseFailuresAreBothReported()
    {
        var charged = Spell("charged", charged: true);
        charged.FireResult = false;
        charged.HoldReleaseResult = false;
        using var fixture = Create(charged);
        fixture.Config.AutoCastMode.Value = AutoCastOperationMode.Active;

        fixture.Engine.Tick(1.0f);

        Assert.Contains(fixture.Log.Entries, entry => entry.ToString()!.Contains("could not release full-charge hold", StringComparison.Ordinal));
        Assert.Contains(fixture.Log.Entries, entry => entry.ToString()!.Contains("could not fire", StringComparison.Ordinal));
    }

    [Fact]
    public void VerboseLoggingSuppressesEmptySlotsAndRepeatedRejectionsUntilStateChanges()
    {
        var rejected = Spell("cooling down");
        rejected.CanCastResult = false;
        rejected.CanCastReason = "recharging: charges=0/1, cooldownRemaining=3e0";
        var empty = Spell("Empty");
        empty.IsEmpty = true;
        using var fixture = Create(rejected, empty);
        fixture.Config.AutoCastMode.Value = AutoCastOperationMode.Active;
        fixture.Config.EnableOperationalLogging.Value = true;
        fixture.Config.DecisionLogLevel.Value = SuiteDecisionLogLevel.Verbose;

        fixture.Engine.Tick(1.0f);
        fixture.Engine.Tick(1.0f);

        Assert.Single(
            fixture.Log.Entries,
            entry => entry.ToString()!.Contains("Auto Cast skipped", StringComparison.Ordinal) &&
                     entry.ToString()!.Contains("cooling down", StringComparison.Ordinal));
        Assert.DoesNotContain(fixture.Log.Entries, entry => entry.ToString()!.Contains("Empty", StringComparison.Ordinal));

        rejected.CanCastResult = true;
        fixture.Engine.Tick(1.0f);
        rejected.CanCastResult = false;
        fixture.Engine.Tick(1.0f);

        Assert.Equal(
            2,
            fixture.Log.Entries.Count(entry =>
                entry.ToString()!.Contains("Auto Cast skipped", StringComparison.Ordinal) &&
                entry.ToString()!.Contains("cooling down", StringComparison.Ordinal)));
    }

    [Fact]
    public void EmergencyDisableStopsActiveCasting()
    {
        var spell = Spell("guarded");
        using var fixture = Create(spell);
        fixture.Config.AutoCastMode.Value = AutoCastOperationMode.Active;
        fixture.Config.EmergencyDisable.Value = true;

        fixture.Engine.Tick(1.0f);

        Assert.Equal(0, spell.FireCalls);
        Assert.Empty(fixture.Log.Entries);
    }

    [Fact]
    public void ActiveModeFiresOneSpellAndAdvancesToNextSlot()
    {
        var first = Spell("first");
        var second = Spell("second");
        using var fixture = Create(first, second);
        fixture.Config.AutoCastMode.Value = AutoCastOperationMode.Active;

        fixture.Engine.Tick(1.0f);
        fixture.Engine.Tick(1.0f);

        Assert.Equal(1, first.FireCalls);
        Assert.Equal(1, second.FireCalls);
    }

    [Fact]
    public void ResourceThresholdAppliesToImmediateAndDrainResources()
    {
        var belowImmediate = Spell("below immediate", immediate: Costs(79, 100, 1));
        var belowDrain = Spell("below drain", drain: Costs(79, 100, 1));
        var admitted = Spell("admitted", immediate: Costs(80, 100, 1), drain: Costs(90, 100, 1));
        using var fixture = Create(belowImmediate, belowDrain, admitted);
        fixture.Config.AutoCastMode.Value = AutoCastOperationMode.Active;
        fixture.Config.AutoCastStartResourcePercent.Value = 80.0f;

        fixture.Engine.Tick(1.0f);

        Assert.Equal(0, belowImmediate.FireCalls);
        Assert.Equal(0, belowDrain.FireCalls);
        Assert.Equal(1, admitted.FireCalls);
    }

    [Fact]
    public void ResourceRejectionDoesNotTraverseTargetGraph()
    {
        var blocked = Spell("resource blocked", immediate: Costs(79, 100, 1));
        using var fixture = Create(blocked);
        fixture.Config.AutoCastMode.Value = AutoCastOperationMode.Active;
        fixture.Config.AutoCastStartResourcePercent.Value = 80.0f;

        fixture.Engine.Tick(1.0f);

        Assert.Equal(0, blocked.TargetValidationCalls);
        Assert.Equal(0, blocked.FireCalls);
    }

    [Fact]
    public void VerboseThresholdRejectionLogsCurrentCapacityFullnessAndRequiredPercent()
    {
        var below = Spell("mana hungry", immediate: Costs(79, 100, 1));
        using var fixture = Create(below);
        fixture.Config.AutoCastMode.Value = AutoCastOperationMode.Active;
        fixture.Config.AutoCastStartResourcePercent.Value = 80.0f;
        fixture.Config.EnableOperationalLogging.Value = true;
        fixture.Config.DecisionLogLevel.Value = SuiteDecisionLogLevel.Verbose;

        fixture.Engine.Tick(1.0f);

        Assert.Contains(
            fixture.Log.Entries,
            entry => entry.ToString()!.Contains(
                "current=7.9e1, capacity=1e2, fullness=79.0 %, required=80.0 %",
                StringComparison.Ordinal));
    }

    [Fact]
    public void RecommendationLogsImmediateAndDrainResourceSnapshots()
    {
        var spell = Spell(
            "resourceful",
            immediate: Costs(80, 100, 2),
            drain: Costs(90, 100, 3));
        using var fixture = Create(spell);
        fixture.Config.AutoCastMode.Value = AutoCastOperationMode.Active;
        fixture.Config.AutoCastStartResourcePercent.Value = 80.0f;
        fixture.Config.EnableOperationalLogging.Value = true;

        fixture.Engine.Tick(1.0f);

        Assert.Contains(
            fixture.Log.Entries,
            entry => entry.ToString()!.Contains(
                "Resource immediate cost=2e0 current=8e1 capacity=1e2 fullness=80.0 %",
                StringComparison.Ordinal));
        Assert.Contains(
            fixture.Log.Entries,
            entry => entry.ToString()!.Contains(
                "Resource drain cost=3e0 current=9e1 capacity=1e2 fullness=90.0 %; StartThreshold=80.0 %",
                StringComparison.Ordinal));
    }

    [Fact]
    public void ZeroCostSpellIsAdmittedWithoutReserveCost()
    {
        var zeroCost = Spell("free", immediate: Array.Empty<ResourceAdmissionCost>());
        using var fixture = Create(zeroCost);
        fixture.Config.AutoCastMode.Value = AutoCastOperationMode.Active;

        fixture.Engine.Tick(1.0f);

        Assert.Equal(1, zeroCost.FireCalls);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void ActiveChannelPausesEntireRotation()
    {
        var channel = Spell("channel", kind: AutoCastSpellKind.Channel, casting: true);
        var instant = Spell("instant");
        using var fixture = Create(channel, instant);
        fixture.Config.AutoCastMode.Value = AutoCastOperationMode.Active;

        fixture.Engine.Tick(1.0f);

        Assert.Equal(0, instant.FireCalls);
    }

    [Fact]
    public void ChannelLifecycleLogsOnlyObservedPauseAndResumeTransitions()
    {
        var channel = Spell("channel", kind: AutoCastSpellKind.Channel);
        using var fixture = Create(channel);
        fixture.Config.AutoCastMode.Value = AutoCastOperationMode.Active;
        fixture.Config.EnableOperationalLogging.Value = true;

        fixture.Engine.Tick(1.0f);
        channel.IsCasting = true;
        fixture.Engine.Tick(1.0f);
        fixture.Engine.Tick(1.0f);
        channel.IsCasting = false;
        channel.CanCastResult = false;
        fixture.Engine.Tick(1.0f);

        Assert.Single(fixture.Log.Entries, entry => entry.ToString()!.Contains("channel active", StringComparison.Ordinal));
        Assert.Single(fixture.Log.Entries, entry => entry.ToString()!.Contains("channel ended", StringComparison.Ordinal));
    }

    [Fact]
    public void ReflectedReadinessExplainsAttuningRechargeAndNativeResources()
    {
        var catalog = new ReflectionAutoCastCatalog();

        var attuning = new ReflectionAutoCastCandidate(catalog, new ReadinessSpell { Attuning = true }, 0);
        Assert.False(attuning.CanCast(out var attuningReason));
        Assert.Equal("attuning after a previous cast", attuningReason);

        var recharging = new ReflectionAutoCastCandidate(
            catalog,
            new ReadinessSpell { ChargeAvailable = false, CurrentCharges = 0, MaximumCharges = 2 },
            0);
        Assert.False(recharging.CanCast(out var rechargeReason));
        Assert.Contains("charges=0/2", rechargeReason, StringComparison.Ordinal);
        Assert.Contains("cooldownRemaining=3e0", rechargeReason, StringComparison.Ordinal);

        var resources = new ReflectionAutoCastCandidate(catalog, new ReadinessSpell { EnoughResources = false }, 0);
        Assert.False(resources.CanCast(out var resourceReason));
        Assert.Equal("native resource availability rejected", resourceReason);
    }

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void ReflectionAutoCastCatalog_TranslatesLoadoutCostsKindAndStableIdentity()
    {
        var manager = new SpellManager();
        const string spellUuid = "11111111-1111-1111-1111-111111111111";
        var spell = new global::Spell(new SpellRecipeSO { uuid = spellUuid })
        {
            DisplayName = "Adapter spell",
            Channeled = true,
        };
        spell.Cost.costs.Add(new global::ResourceTuple(
            new global::ResourceSO
            {
                uuid = "adapter-resource",
                quantity = new BigDouble(10.0, 0),
            },
            new BigDouble(2.0, 0)));
        manager.activeSpells.Add(spell);
        SpellManager.instance = manager;
        SpellManager.NativeCanCast = true;
        global::Spell.FireSignal = AutoCastManualSignal.NotifySpellFire;
        TargetingManager.Targeting = false;
        using var catalog = new ReflectionAutoCastCatalog();
        try
        {
            var candidate = Assert.Single(catalog.DiscoverActiveLoadout());

            Assert.Equal(0, candidate.SlotIndex);
            Assert.Equal("Adapter spell", candidate.DisplayName);
            Assert.Equal(AutoCastSpellKind.Channel, candidate.Kind);
            Assert.True(candidate.TryGetImmediateCosts(out var costs));
            var cost = Assert.Single(costs);
            Assert.Equal("adapter-resource", cost.ResourceId);
            Assert.Equal(0, cost.Cost.CompareTo(new BigAmount(2.0, 0)));
            Assert.Equal(0, cost.CurrentQuantity.CompareTo(new BigAmount(10.0, 0)));
            Assert.Equal(0, spell.Cost.CostPrintReads);
            Assert.True(candidate.TryGetIdentity(out var identity, out var identityReason), identityReason);
            Assert.Equal(spellUuid, identity.Uuid);
            Assert.Same(spell, identity.NativeReference);
            Assert.Equal(typeof(global::Spell), identity.NativeType);
            Assert.True(candidate.TrySetChargeHold(true, out var holdReason), holdReason);
            var mutationOutcome = Assert.IsAssignableFrom<INativeMutationOutcomeSource>(candidate);
            Assert.Equal(1, mutationOutcome.LastNativeMutationOutcome.NativeCallsAttempted);
            Assert.Equal(0, mutationOutcome.LastNativeMutationOutcome.MutationsCommitted);
            Assert.True(spell.HoldingCharge);
            Assert.True(candidate.TryFireAndResolveTargets(out var fireReason), fireReason);
            Assert.Equal(1, mutationOutcome.LastNativeMutationOutcome.NativeCallsAttempted);
            Assert.Equal(1, mutationOutcome.LastNativeMutationOutcome.MutationsCommitted);
            Assert.Equal(1, spell.FireCalls);
        }
        finally
        {
            SpellManager.instance = null;
            SpellManager.NativeCanCast = true;
            global::Spell.FireSignal = null;
            TargetingManager.Targeting = false;
        }
    }

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void ReflectionAutoCastCandidate_MissingFireEvidenceBlocksUntilLifecycleRecovery()
    {
        var manager = new SpellManager();
        var spell = new global::Spell(new SpellRecipeSO { uuid = "22222222-2222-2222-2222-222222222222" }) { EmitFireSignal = false };
        manager.activeSpells.Add(spell);
        SpellManager.instance = manager;
        global::Spell.FireSignal = AutoCastManualSignal.NotifySpellFire;
        TargetingManager.Targeting = false;
        using var catalog = new ReflectionAutoCastCatalog();
        try
        {
            var candidate = Assert.Single(catalog.DiscoverActiveLoadout());

            Assert.False(candidate.TryFireAndResolveTargets(out var failedReason));
            var mutationOutcome = Assert.IsAssignableFrom<INativeMutationOutcomeSource>(candidate);
            Assert.Contains("PostconditionFailed", failedReason);
            Assert.Equal(1, mutationOutcome.LastNativeMutationOutcome.NativeCallsAttempted);
            Assert.Equal(0, mutationOutcome.LastNativeMutationOutcome.MutationsCommitted);
            Assert.Equal(1, spell.FireCalls);
            Assert.False(candidate.TryFireAndResolveTargets(out var blockedReason));
            Assert.Contains("blocked until the next lifecycle", blockedReason);
            Assert.Equal(0, mutationOutcome.LastNativeMutationOutcome.NativeCallsAttempted);
            Assert.Equal(1, spell.FireCalls);

            spell.EmitFireSignal = true;
            catalog.RecoverMutationBlocks();

            Assert.True(candidate.TryFireAndResolveTargets(out var recoveredReason), recoveredReason);
            Assert.Equal(2, spell.FireCalls);
        }
        finally
        {
            SpellManager.instance = null;
            global::Spell.FireSignal = null;
            TargetingManager.Targeting = false;
        }
    }

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void ReflectionAutoCastCandidate_NativeExecutionThrowIsAttemptedButUncommitted()
    {
        var manager = new SpellManager();
        var spell = new global::Spell(new SpellRecipeSO { uuid = "33333333-3333-3333-3333-333333333333" });
        manager.activeSpells.Add(spell);
        SpellManager.instance = manager;
        global::Spell.FireSignal = () => throw new InvalidOperationException("simulated native fire failure");
        TargetingManager.Targeting = false;
        using var catalog = new ReflectionAutoCastCatalog();
        try
        {
            var candidate = Assert.Single(catalog.DiscoverActiveLoadout());
            var mutationOutcome = Assert.IsAssignableFrom<INativeMutationOutcomeSource>(candidate);

            Assert.False(candidate.TryFireAndResolveTargets(out var reason));
            Assert.Contains("ExecutionThrew", reason);
            Assert.Equal(1, mutationOutcome.LastNativeMutationOutcome.NativeCallsAttempted);
            Assert.Equal(1, mutationOutcome.LastNativeMutationOutcome.MutationAttempts);
            Assert.Equal(0, mutationOutcome.LastNativeMutationOutcome.MutationsCommitted);
        }
        finally
        {
            SpellManager.instance = null;
            global::Spell.FireSignal = null;
            TargetingManager.Targeting = false;
        }
    }

    [Fact]
    public void ManualFireSignalPausesForConfiguredUnscaledTime()
    {
        var spell = Spell("paused");
        using var fixture = Create(spell);
        fixture.Config.AutoCastMode.Value = AutoCastOperationMode.Active;
        fixture.Config.AutoCastManualPauseSeconds.Value = 2.0f;

        AutoCastManualSignal.NotifySpellFire();
        fixture.Engine.Tick(1.0f);
        Assert.Equal(0, spell.FireCalls);

        fixture.Engine.Tick(1.0f);
        Assert.Equal(1, spell.FireCalls);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void ExistingTargetingRequestPausesWithoutFiring()
    {
        var spell = Spell("targeted");
        var catalog = new FakeCatalog(spell) { Targeting = true };
        using var fixture = Create(catalog);
        fixture.Config.AutoCastMode.Value = AutoCastOperationMode.Active;

        fixture.Engine.Tick(1.0f);

        Assert.Equal(0, spell.FireCalls);
    }

    [Fact]
    public void NativeCastBusyPausesWithoutQueueingAnotherSpell()
    {
        var spell = Spell("busy");
        var catalog = new FakeCatalog(spell) { Busy = true };
        using var fixture = Create(catalog);
        fixture.Config.AutoCastMode.Value = AutoCastOperationMode.Active;

        fixture.Engine.Tick(1.0f);

        Assert.Equal(0, spell.FireCalls);
    }

    [Fact]
    public void TargetPreflightRejectsSpellAndContinuesRotation()
    {
        var invalid = Spell("invalid target");
        invalid.TargetsValid = false;
        var valid = Spell("valid target");
        using var fixture = Create(invalid, valid);
        fixture.Config.AutoCastMode.Value = AutoCastOperationMode.Active;

        fixture.Engine.Tick(1.0f);

        Assert.Equal(0, invalid.FireCalls);
        Assert.Equal(1, valid.FireCalls);
    }

    [Fact]
    public void AutomatedFireScopeDoesNotTriggerManualPause()
    {
        var spell = Spell("automated");
        using var fixture = Create(spell);
        fixture.Config.AutoCastMode.Value = AutoCastOperationMode.Active;

        using (AutoCastManualSignal.EnterAutomatedFire())
        {
            AutoCastManualSignal.NotifySpellFire();
        }

        fixture.Engine.Tick(1.0f);

        Assert.Equal(1, spell.FireCalls);
    }

    private static ResourceAdmissionCost[] Costs(double quantity, double capacity, double cost)
    {
        return new[]
        {
            new ResourceAdmissionCost(
                "resource",
                "Resource",
                new BigAmount(cost, 0),
                new BigAmount(quantity, 0),
                new BigAmount(capacity, 0)),
        };
    }

    private static FakeSpell Spell(
        string name,
        AutoCastSpellKind kind = AutoCastSpellKind.Instant,
        bool charged = false,
        bool casting = false,
        IReadOnlyList<ResourceAdmissionCost>? immediate = null,
        IReadOnlyList<ResourceAdmissionCost>? drain = null)
    {
        return new FakeSpell(name, kind, charged, casting, immediate, drain);
    }

    private static Fixture Create(params FakeSpell[] spells) => Create(new FakeCatalog(spells));

    private static Fixture CreateWithCoordinator(params FakeSpell[] spells)
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.AbsoluteReserve.Value = "0";
        config.RelativeReserveMultiplier.Value = 0.0f;
        var log = new ManualLogSource();
        var statuses = new AutomataFeatureStatuses(config.Current, 1, new FeatureStatusRegistry());
        var coordinator = new SuitePerformanceCoordinator(StopwatchPerformanceClock.Instance, 1000.0, 1000.0);
        var engine = new AutoCastEngine(
            config,
            new FakeCatalog(spells),
            new ReservePolicy(config),
            new ResourceFullnessPolicy(),
            log,
            () => true,
            coordinator,
            () => 1,
            statuses.AutoCast);
        return new Fixture(config, log, engine, statuses);
    }

    private static Fixture Create(FakeCatalog catalog)
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.AbsoluteReserve.Value = "0";
        config.RelativeReserveMultiplier.Value = 0.0f;
        var log = new ManualLogSource();
        var statuses = new AutomataFeatureStatuses(config.Current, 1, new FeatureStatusRegistry());
        var engine = new AutoCastEngine(
            config,
            catalog,
            new ReservePolicy(config),
            new ResourceFullnessPolicy(),
            log,
            () => true,
            featureStatus: statuses.AutoCast);
        return new Fixture(config, log, engine, statuses);
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture(
            BepInExAutomataConfiguration config,
            ManualLogSource log,
            AutoCastEngine engine,
            AutomataFeatureStatuses featureStatuses)
        {
            Config = config;
            Log = log;
            Engine = engine;
            FeatureStatuses = featureStatuses;
        }

        public BepInExAutomataConfiguration Config { get; }

        public ManualLogSource Log { get; }

        public AutoCastEngine Engine { get; }

        public AutomataFeatureStatuses FeatureStatuses { get; }

        public void Dispose()
        {
            Engine.Dispose();
            FeatureStatuses.Dispose();
        }
    }

    private sealed class FakeCatalog : IAutoCastCatalog
    {
        private readonly IReadOnlyList<IAutoCastCandidate> _spells;

        public FakeCatalog(params FakeSpell[] spells)
        {
            for (var index = 0; index < spells.Length; index++)
            {
                spells[index].SlotIndexValue = index;
            }

            _spells = spells;
        }

        public bool Busy { get; set; }

        public bool Targeting { get; set; }

        public IReadOnlyList<IAutoCastCandidate> DiscoverActiveLoadout() => _spells;

        public bool IsNativeCastBusy() => Busy;

        public bool IsTargeting() => Targeting;

        public void Dispose()
        {
        }
    }

    private sealed class FakeSpell : IAutoCastCandidate, IAutoCastAdmissionFailureEvidence
    {
        private readonly IReadOnlyList<ResourceAdmissionCost> _immediate;
        private readonly IReadOnlyList<ResourceAdmissionCost> _drain;
        private readonly object _nativeIdentity = new object();

        public FakeSpell(
            string name,
            AutoCastSpellKind kind,
            bool charged,
            bool casting,
            IReadOnlyList<ResourceAdmissionCost>? immediate,
            IReadOnlyList<ResourceAdmissionCost>? drain)
        {
            DisplayName = name;
            Kind = kind;
            IsCharged = charged;
            IsCasting = casting;
            _immediate = immediate ?? Costs(100, 100, 1);
            _drain = drain ?? Array.Empty<ResourceAdmissionCost>();
        }

        public int SlotIndexValue { get; set; }

        public int SlotIndex => SlotIndexValue;

        public string DisplayName { get; }

        public AutoCastSpellKind Kind { get; }

        public bool IsEmpty { get; set; }

        public bool IsCharged { get; }

        public bool IsCasting { get; set; }

        public bool IsReadyingCast { get; set; }

        public int FireCalls { get; private set; }

        public List<bool> ChargeHoldChanges { get; } = new List<bool>();

        public bool FireResult { get; set; } = true;

        public bool HoldStartResult { get; set; } = true;

        public bool HoldReleaseResult { get; set; } = true;

        public bool TargetsValid { get; set; } = true;

        public int TargetValidationCalls { get; private set; }

        public bool CanCastResult { get; set; } = true;

        public string CanCastReason { get; set; } = "ready";

        public AutoCastAdmissionFailureKind AdmissionFailureKind { get; set; }

        public AutoCastAdmissionFailureKind LastAdmissionFailure => AdmissionFailureKind;

        public bool CanCast(out string reason)
        {
            reason = CanCastReason;
            return CanCastResult;
        }

        public bool TryGetImmediateCosts(out IReadOnlyList<ResourceAdmissionCost> costs)
        {
            costs = _immediate;
            return true;
        }

        public bool TryGetDrainCosts(out IReadOnlyList<ResourceAdmissionCost> costs)
        {
            costs = _drain;
            return true;
        }

        public bool HasValidTargets(out string reason)
        {
            TargetValidationCalls++;
            reason = TargetsValid ? "valid" : "no valid target";
            return TargetsValid;
        }

        public bool TryFireAndResolveTargets(out string reason)
        {
            FireCalls++;
            reason = FireResult ? "fired" : "native fire failed";
            return FireResult;
        }

        public bool TrySetChargeHold(bool isHolding, out string reason)
        {
            ChargeHoldChanges.Add(isHolding);
            var succeeded = isHolding ? HoldStartResult : HoldReleaseResult;
            reason = succeeded ? (isHolding ? "held" : "released") : "native hold failed";
            return succeeded;
        }

        public bool TryGetIdentity(out AutoCastCandidateIdentity identity, out string reason)
        {
            identity = new AutoCastCandidateIdentity(DisplayName, _nativeIdentity, GetType(), SlotIndex);
            reason = string.Empty;
            return true;
        }
    }

    private sealed class ReadinessSpell
    {
        public bool Attuning { get; set; }

        public bool ChargeAvailable { get; set; } = true;

        public bool EnoughResources { get; set; } = true;

        public int CurrentCharges { get; set; } = 1;

        public int MaximumCharges { get; set; } = 1;

        public bool CanCast() => false;

        public bool IsAttuning() => Attuning;

        public bool IsChargeAvailable() => ChargeAvailable;

        public bool HasEnoughResources() => EnoughResources;

        public int GetCurrSpellCharges() => CurrentCharges;

        public int GetMaxSpellCharges() => MaximumCharges;

        public TestBigDouble GetCooldownTimeRemaining() => new TestBigDouble(3.0, 0);
    }

    private sealed class NativeLoadoutSpell
    {
        private readonly NativeLoadoutCostList _cost = new();

        public NativeLoadoutSpell(string uuid)
        {
            reference = new NativeLoadoutRecipe { uuid = uuid };
            _cost.costs.Add(new NativeLoadoutCostEntry(
                new NativeLoadoutResourceSO
                {
                    uuid = "adapter-resource",
                    quantity = new BigDouble(10.0, 0),
                },
                new BigDouble(2.0, 0)));
        }

        public NativeLoadoutRecipe reference;
        public bool Channeled { get; set; }
        public bool EmitFireSignal { get; set; } = true;
        public bool HoldingCharge { get; private set; }
        public int FireCalls { get; private set; }

        public string GetName() => "Adapter spell";
        public bool IsChanneled() => Channeled;
        public bool IsToggledSpell() => false;
        public bool IsEmpty() => false;
        public bool CanCharge() => true;
        public bool IsCasting() => false;
        public bool IsReadyingCast() => false;
        public bool CanCast() => true;
        public bool IsAttuning() => false;
        public bool IsChargeAvailable() => true;
        public bool HasEnoughResources() => true;
        public NativeLoadoutCostList GetCost() => _cost;
        public NativeLoadoutCostList GetDrainCost() => new();
        public object GetScalingInfo() => new object();
        public void SetChargeInput(string source, bool holding) => HoldingCharge = holding;
        public void Fire()
        {
            if (EmitFireSignal) AutoCastManualSignal.NotifySpellFire();
            FireCalls++;
        }
    }

    private sealed class NativeLoadoutRecipe
    {
        public string uuid = string.Empty;
    }

    private sealed class NativeLoadoutCostList
    {
        public List<NativeLoadoutCostEntry> costs = new();
    }

    private sealed class NativeLoadoutCostEntry
    {
        public NativeLoadoutCostEntry(NativeLoadoutResourceSO resource, BigDouble value)
        {
            this.resource = resource;
            Value = value;
        }

        public NativeLoadoutResourceSO resource;
        public BigDouble Value { get; }
        public BigDouble GetValue() => Value;
    }

    private sealed class NativeLoadoutResourceSO
    {
        public string uuid = string.Empty;
        public BigDouble quantity;
        public string GetName() => "Adapter resource";
        public BigDouble GetQuantity() => quantity;
    }

    private sealed class TestBigDouble
    {
        public TestBigDouble(double mantissa, long exponent)
        {
            this.mantissa = mantissa;
            this.exponent = exponent;
        }

        public readonly double mantissa;

        public readonly long exponent;
    }
}
