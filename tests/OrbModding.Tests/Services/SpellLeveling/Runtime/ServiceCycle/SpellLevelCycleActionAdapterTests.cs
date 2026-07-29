using System;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using Xunit;

namespace OrbModding.Tests.Services.SpellLeveling.Runtime.ServiceCycle;

/// <summary>
/// End-to-end tests for the Spell Leveling boundary: the real action adapter over the real native
/// adapter over the game stubs. What the worker cannot see — the leveling prerequisite and the level's
/// affordability — is decided here, so this is where those rules are pinned.
/// </summary>
public sealed class SpellLevelCycleActionAdapterTests : IDisposable
{
    private const long PlannedEpoch = 7;

    public SpellLevelCycleActionAdapterTests() => ResetNativeState();

    public void Dispose() => ResetNativeState();

    [Fact]
    public void ASingleLevelCommitsAsAnExactPlusOneDelta()
    {
        var spell = Spell(masteryLevel: 3, ready: true, unlocked: true);

        var result = Execute(SpellLevelActionKind.Single, Id(spell));

        Assert.Equal(ServiceActionDisposition.Committed, result.Disposition);
        Assert.Equal(CommonActionResultCodes.Committed, result.Code);
        Assert.True(result.HasNativeEvidence);
        Assert.Equal(NativeMutationOutcome.Verified, result.NativeEvidence.Outcome);
        Assert.Equal(4, spell.masteryLevel);
        Assert.Equal(1, spell.GetLevelCost().PerformCalls);
    }

    [Fact]
    public void ALockedProgressionRejectsWithItsOwnCodeAndSpendsNothing()
    {
        // The one refusal the planner cannot avoid: prerequisites are not published, so the worker
        // plans optimistically and learns the answer here. Its own code is what lets the feature
        // status say Locked rather than a generic native rejection.
        var spell = Spell(masteryLevel: 1, ready: true, unlocked: false);

        var result = Execute(SpellLevelActionKind.Single, Id(spell));

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(SpellLevelActionResultCodes.ProgressionLocked, result.Code);
        Assert.Equal(1, spell.masteryLevel);
        Assert.Equal(0, spell.GetLevelCost().PerformCalls);
    }

    [Fact]
    public void AnUnaffordableLevelRejectsWithoutSpending()
    {
        var spell = Spell(masteryLevel: 1, ready: true, unlocked: true);
        spell.GetLevelCost().affordable = false;

        var result = Execute(SpellLevelActionKind.Single, Id(spell));

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(SpellLevelActionResultCodes.LevelNotAffordable, result.Code);
        Assert.Equal(1, spell.masteryLevel);
        Assert.Equal(0, spell.GetLevelCost().PerformCalls);
    }

    [Fact]
    public void ASpellThatStoppedBeingReadySinceThePlanRejectsWithoutSpending()
    {
        // The snapshot said ready and the game now says otherwise. That is the staleness the boundary
        // exists to absorb, and absorbing it costs one penalty-free rejection.
        var spell = Spell(masteryLevel: 1, ready: false, unlocked: true);

        var result = Execute(SpellLevelActionKind.Single, Id(spell));

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(SpellLevelActionResultCodes.LevelNotAffordable, result.Code);
        Assert.Equal(0, spell.GetLevelCost().PerformCalls);
    }

    [Fact]
    public void AnAllLevelsEveryReadySpellOnce()
    {
        var lowest = Spell(masteryLevel: 1, ready: true, unlocked: true);
        var other = Spell(masteryLevel: 5, ready: true, unlocked: true);
        var notReady = Spell(masteryLevel: 2, ready: false, unlocked: true);
        UnlockLevelAll(level: 1);

        var result = Execute(SpellLevelActionKind.All, Id(lowest));

        Assert.Equal(ServiceActionDisposition.Committed, result.Disposition);
        Assert.Equal(2, lowest.masteryLevel);
        Assert.Equal(6, other.masteryLevel);
        Assert.Equal(2, notReady.masteryLevel);
    }

    [Fact]
    public void AnAllPlannedAgainstAnUpgradeThatIsNoLongerCommittedRejects()
    {
        var spell = Spell(masteryLevel: 1, ready: true, unlocked: true);
        UnlockLevelAll(level: 0);

        var result = Execute(SpellLevelActionKind.All, Id(spell));

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(SpellLevelActionResultCodes.LevelNotAffordable, result.Code);
        Assert.Equal(1, spell.masteryLevel);
    }

    [Fact]
    public void ALifecycleEpochDriftRejectsWithoutTouchingTheGame()
    {
        var spell = Spell(masteryLevel: 1, ready: true, unlocked: true);

        var result = Execute(SpellLevelActionKind.Single, Id(spell), nativeEpoch: PlannedEpoch + 1);

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(CommonActionResultCodes.LifecycleReplaced, result.Code);
        Assert.Equal(1, spell.masteryLevel);
        Assert.Equal(0, spell.GetLevelCost().PerformCalls);
    }

    [Fact]
    public void LosingTheActionFamilyRejectsWithoutTouchingTheGame()
    {
        var spell = Spell(masteryLevel: 1, ready: true, unlocked: true);

        var result = Execute(SpellLevelActionKind.Single, Id(spell), ownsActionFamily: false);

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(SpellLevelActionResultCodes.ActionFamilyUnavailable, result.Code);
        Assert.Equal(1, spell.masteryLevel);
    }

    [Fact]
    public void ADisabledConfigurationRejectsWithoutTouchingTheGame()
    {
        var spell = Spell(masteryLevel: 1, ready: true, unlocked: true);

        var result = Execute(SpellLevelActionKind.Single, Id(spell), autoLevelSpells: false);

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(CommonActionResultCodes.ServiceDisabled, result.Code);
        Assert.Equal(1, spell.masteryLevel);
    }

    [Fact]
    public void AMutationThatDoesNotMoveTheLevelFaultsAndBlocksFurtherSpending()
    {
        // The audited postcondition. A native call that spends and does not deliver means the suite no
        // longer understands the contract, so it faults and the port refuses to call again until the
        // next lifecycle rather than spending on every cycle forever.
        var spell = Spell(masteryLevel: 1, ready: true, unlocked: true);
        spell.SuppressLevelMutation = true;
        var levels = new SpellLevelNativeAdapter();

        var first = Execute(SpellLevelActionKind.Single, Id(spell), levels: levels);
        Assert.Equal(ServiceActionDisposition.Faulted, first.Disposition);
        Assert.Equal(CommonActionResultCodes.AdapterFault, first.Code);

        spell.SuppressLevelMutation = false;
        var second = Execute(SpellLevelActionKind.Single, Id(spell), levels: levels);
        Assert.Equal(ServiceActionDisposition.Faulted, second.Disposition);
        Assert.Equal(1, spell.masteryLevel);

        // A lifecycle boundary is the one thing that clears it, which is the whole retained-state rule.
        levels.InvalidateLifecycle();
        var third = Execute(SpellLevelActionKind.Single, Id(spell), levels: levels);
        Assert.Equal(ServiceActionDisposition.Committed, third.Disposition);
        Assert.Equal(2, spell.masteryLevel);
    }

    [Fact]
    public void TheBoundaryIsTheOnlyPlaceLockedCanBeObserved()
    {
        var spell = Spell(masteryLevel: 1, ready: true, unlocked: false);
        var observed = new List<AutoSpellLevelCapability>();

        Execute(SpellLevelActionKind.Single, Id(spell), observeCapability: observed.Add);
        Assert.Equal(AutoSpellLevelCapability.Locked, Assert.Single(observed));

        observed.Clear();
        spell.levelingPrerequisites.available = true;
        Execute(SpellLevelActionKind.Single, Id(spell), observeCapability: observed.Add);
        Assert.Equal(AutoSpellLevelCapability.Single, Assert.Single(observed));
    }

    [Fact]
    public void TheCapabilityProbeReadsTheGameRatherThanTheSnapshot()
    {
        var levels = new SpellLevelNativeAdapter();
        var spell = Spell(masteryLevel: 1, ready: false, unlocked: false);

        Assert.True(levels.TryReadCapability(out var locked));
        Assert.Equal(AutoSpellLevelCapability.Locked, locked);

        // Readiness is not what unlocks the feature — the prerequisite is.
        spell.levelingPrerequisites.available = true;
        Assert.True(levels.TryReadCapability(out var single));
        Assert.Equal(AutoSpellLevelCapability.Single, single);

        UnlockLevelAll(level: 1);
        Assert.True(levels.TryReadCapability(out var all));
        Assert.Equal(AutoSpellLevelCapability.All, all);
    }

    // ---- Helpers -----------------------------------------------------------------------------

    private static Guid Id(global::SpellRecipeSO spell) => Guid.Parse(spell.uuid);

    private static global::SpellRecipeSO Spell(int masteryLevel, bool ready, bool unlocked)
    {
        var spell = new global::SpellRecipeSO
        {
            uuid = Guid.NewGuid().ToString(),
            masteryLevel = masteryLevel,
            discovered = true,
            readyToLevel = ready,
        };
        spell.levelingPrerequisites.available = unlocked;
        global::SpellRecipeSO.All.Add(spell);
        global::SpellManager.instance!.availableSpellRecipes.value.Add(spell);
        return spell;
    }

    /// <summary>
    /// Sets the committed level of the level-all upgrade. The upgrade itself always exists: the native
    /// port resolves it during binding, so a build without it has no spell-level contract at all —
    /// which is a different failure from "the player has not bought it yet".
    /// </summary>
    private static void UnlockLevelAll(int level) => LevelAllUpgrade().level = level;

    private static global::UpgradeSO LevelAllUpgrade() =>
        (global::UpgradeSO)global::IdScriptableObject.RuntimeLookup[KnownEntities.UnlockLevelAllSpells.Uuid];

    private static ServiceActionResult Execute(
        SpellLevelActionKind kind,
        Guid uuid,
        long nativeEpoch = PlannedEpoch,
        bool ownsActionFamily = true,
        bool autoLevelSpells = true,
        SpellLevelNativeAdapter? levels = null,
        Action<AutoSpellLevelCapability>? observeCapability = null)
    {
        var adapter = new SpellLevelCycleActionAdapter(
            levels ?? new SpellLevelNativeAdapter(),
            () => nativeEpoch,
            () => ownsActionFamily,
            observeCapability);
        var action = new SpellLevelCycleAction(kind, uuid, PlannedEpoch);
        var config = new SuiteRuntimeConfiguration
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
            Safety = new SuiteSafetyConfiguration { EmergencyDisable = false },
            AutoBuy = new AutoBuyConfiguration
            {
                Mode = AutoBuyOperationMode.Active,
                AutoLevelSpells = autoLevelSpells,
            },
        };
        return adapter.TryExecute(in action, in config, default);
    }

    private static void ResetNativeState()
    {
        global::IdScriptableObject.RuntimeLookup.Clear();
        global::SpellRecipeSO.All.Clear();
        global::UpgradeSO.All.Clear();
        global::SpellManager.instance = new global::SpellManager();

        // The upgrade has to be reachable the way production reaches it, which is the typed registry
        // rather than the category's All list. Seeding only the list leaves the port unable to bind.
        var levelAll = new global::UpgradeSO
        {
            uuid = KnownEntities.UnlockLevelAllSpells.Uuid.ToString("D"),
            level = 0,
            available = true,
        };
        global::UpgradeSO.All.Add(levelAll);
        global::IdScriptableObject.RuntimeLookup.Add(KnownEntities.UnlockLevelAllSpells.Uuid, levelAll);
    }
}
