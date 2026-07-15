using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using BepInEx.Logging;
using OrbAutomata;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutoCastTests
{
    [Theory]
    [InlineData(0, "AC OFF")]
    [InlineData(1, "AC ON")]
    [InlineData(2, "AC !")]
    public void CompactToggleUsesConsistentAutoCastLabels(int state, string expected)
    {
        Assert.Equal(expected, AutoCastToggleButton.FormatLabel((AutoCastToggleVisualState)state));
    }

    [Fact]
    public void ToggleSwitchesBetweenDisabledAndActive()
    {
        var config = AutomataConfig.Bind(new ConfigFile());
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
    public void EmergencyDisableRendersActiveModeAsBlocked()
    {
        var config = AutomataConfig.Bind(new ConfigFile());
        config.AutoCastMode.Value = AutoCastOperationMode.Active;
        var toggle = new AutoCastToggleControl(config);

        Assert.Equal(AutoCastToggleVisualState.On, toggle.State);
        config.EmergencyDisable.Value = true;
        Assert.Equal(AutoCastToggleVisualState.Blocked, toggle.State);
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
    public void ActiveModeUsesRoundRobinOrderAndSkipsChargedAndActiveAura()
    {
        var charged = Spell("charged", charged: true);
        var aura = Spell("active aura", kind: AutoCastSpellKind.Aura, casting: true);
        var first = Spell("first");
        var second = Spell("second");
        using var fixture = Create(charged, aura, first, second);
        fixture.Config.AutoCastMode.Value = AutoCastOperationMode.Active;
        fixture.Config.EnableOperationalLogging.Value = true;

        fixture.Engine.Tick(1.0f);
        fixture.Engine.Tick(1.0f);

        Assert.Contains(fixture.Log.Entries, entry => entry.ToString()!.Contains("first", StringComparison.Ordinal));
        Assert.Contains(fixture.Log.Entries, entry => entry.ToString()!.Contains("second", StringComparison.Ordinal));
        Assert.Equal(1, first.FireCalls);
        Assert.Equal(1, second.FireCalls);
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
        fixture.Config.DecisionLogLevel.Value = AutomataDecisionLogLevel.Verbose;

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

        fixture.Engine.Tick(1.0f);

        Assert.Equal(0, belowImmediate.FireCalls);
        Assert.Equal(0, belowDrain.FireCalls);
        Assert.Equal(1, admitted.FireCalls);
    }

    [Fact]
    public void VerboseThresholdRejectionLogsCurrentCapacityFullnessAndRequiredPercent()
    {
        var below = Spell("mana hungry", immediate: Costs(79, 100, 1));
        using var fixture = Create(below);
        fixture.Config.AutoCastMode.Value = AutoCastOperationMode.Active;
        fixture.Config.EnableOperationalLogging.Value = true;
        fixture.Config.DecisionLogLevel.Value = AutomataDecisionLogLevel.Verbose;

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

    private static Fixture Create(FakeCatalog catalog)
    {
        var config = AutomataConfig.Bind(new ConfigFile());
        config.AbsoluteReserve.Value = "0";
        config.RelativeReserveMultiplier.Value = 0.0f;
        var log = new ManualLogSource();
        var engine = new AutoCastEngine(config, catalog, new ReservePolicy(config), new ResourceFullnessPolicy(), log, () => true);
        return new Fixture(config, log, engine);
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture(AutomataConfig config, ManualLogSource log, AutoCastEngine engine)
        {
            Config = config;
            Log = log;
            Engine = engine;
        }

        public AutomataConfig Config { get; }

        public ManualLogSource Log { get; }

        public AutoCastEngine Engine { get; }

        public void Dispose() => Engine.Dispose();
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

    private sealed class FakeSpell : IAutoCastCandidate
    {
        private readonly IReadOnlyList<ResourceAdmissionCost> _immediate;
        private readonly IReadOnlyList<ResourceAdmissionCost> _drain;

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

        public int FireCalls { get; private set; }

        public bool TargetsValid { get; set; } = true;

        public bool CanCastResult { get; set; } = true;

        public string CanCastReason { get; set; } = "ready";

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
            reason = TargetsValid ? "valid" : "no valid target";
            return TargetsValid;
        }

        public bool TryFireAndResolveTargets(out string reason)
        {
            FireCalls++;
            reason = "fired";
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
