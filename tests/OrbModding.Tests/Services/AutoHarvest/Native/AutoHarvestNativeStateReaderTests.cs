using System;
using System.Collections.Generic;
using System.Reflection;
using OrbAutomata;
using Xunit;

namespace OrbModding.Tests.Services.AutoHarvest.Native;

public sealed class AutoHarvestNativeStateReaderTests
{
    /// <summary>
    /// The reader's whole remaining job at the action boundary: hand back the instance to submit
    /// into. Every fact the submission rests on now rides on the action.
    /// </summary>
    [Fact]
    public void ThePlotsOneInstanceOfThePairIsTheResolvedPrototype()
    {
        var world = PrototypeWorld.Create();
        var instance = world.AddInstance(world.Plot, world.Action);

        Assert.Same(instance, new AutoHarvestNativeStateReader().ReadPrototype(world.Resolved));
    }

    /// <summary>
    /// Instances of other plots and actions are passed over, not counted.
    /// </summary>
    [Fact]
    public void InstancesOfOtherPairsAreNotMistakenForThePrototype()
    {
        var world = PrototypeWorld.Create();
        world.AddInstance(world.OtherPlot, world.OtherAction);
        var instance = world.AddInstance(world.Plot, world.Action);
        world.AddInstance(world.OtherPlot, world.OtherAction);

        Assert.Same(instance, new AutoHarvestNativeStateReader().ReadPrototype(world.Resolved));
    }

    /// <summary>
    /// Two instances of the pair leave nothing to submit into: which one would be a guess.
    /// </summary>
    [Fact]
    public void TwoInstancesOfThePairResolveToNoPrototype()
    {
        var world = PrototypeWorld.Create();
        world.AddInstance(world.Plot, world.Action);
        world.AddInstance(world.Plot, world.Action);

        Assert.Null(new AutoHarvestNativeStateReader().ReadPrototype(world.Resolved));
    }

    [Fact]
    public void APlotHoldingNoInstanceOfTheActionResolvesToNoPrototype()
    {
        var world = PrototypeWorld.Create();
        world.AddInstance(world.OtherPlot, world.OtherAction);

        Assert.Null(new AutoHarvestNativeStateReader().ReadPrototype(world.Resolved));
    }

    /// <summary>
    /// The pair's own action turning up under another plot is a contradiction, not an unrelated
    /// entry: nothing about the list can be trusted after it, so no prototype is resolved.
    /// </summary>
    [Fact]
    public void TheSupportedActionUnderAForeignPlotRefusesTheWholeList()
    {
        var world = PrototypeWorld.Create();
        world.AddInstance(world.Plot, world.Action);
        world.AddInstance(world.OtherPlot, world.Action);

        Assert.Null(new AutoHarvestNativeStateReader().ReadPrototype(world.Resolved));
    }

    [Fact]
    public void AnInstanceOfTheWrongNativeTypeRefusesTheWholeList()
    {
        var world = PrototypeWorld.Create();
        world.AddInstance(world.Plot, world.Action);
        world.Instances.Add("not an instance");

        Assert.Null(new AutoHarvestNativeStateReader().ReadPrototype(world.Resolved));
    }

    [Fact]
    public void FinalFreeActionEntryIsAvailableWhenNativeEntryEvidenceAgrees()
    {
        var state = State(emptyEntries: 1, nativeHasEmptyEntry: true);

        Assert.Equal(
            AutoHarvestEvidenceState.Verified,
            AutoHarvestWorldFacts.ProjectActionSlotAvailability(state));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void MissingEnumeratedOrNativeEmptyEntryRejectsAvailability(
        int emptyEntries,
        bool nativeHasEmptyEntry)
    {
        var state = State(emptyEntries, nativeHasEmptyEntry);

        Assert.Equal(
            AutoHarvestEvidenceState.Rejected,
            AutoHarvestWorldFacts.ProjectActionSlotAvailability(state));
    }

    [Fact]
    public void InvalidNativeStateLeavesAvailabilityUnknown()
    {
        Assert.Equal(
            AutoHarvestEvidenceState.Unknown,
            AutoHarvestWorldFacts.ProjectActionSlotAvailability(
                AutoHarvestSubmissionState.Invalid));
    }

    [Fact]
    public void SharedActiveActionSnapshotProjectsBothPairsWithoutLosingCommonEvidence()
    {
        var fruit = new AutoHarvestActivePairState(matchCount: 1, quantity: 2, engaged: true);
        var treasure = new AutoHarvestActivePairState(matchCount: 2, quantity: 5, engaged: false);
        var snapshot = new AutoHarvestActiveActionSnapshot(
            true,
            usedEntryCount: 3,
            emptyEntryCount: 1,
            nativeHasEmptyEntry: true,
            supportedCollectCount: 3,
            fruit,
            treasure);

        var fruitState = snapshot.Project(AutoHarvestPair.FruitTree);
        var treasureState = snapshot.Project(AutoHarvestPair.TreasureTree);

        Assert.Equal(3, fruitState.SupportedCollectCount);
        Assert.Equal(1, fruitState.PairMatchCount);
        Assert.Equal(2, fruitState.PairQuantity);
        Assert.True(fruitState.PairEngaged);
        Assert.Equal(3, treasureState.SupportedCollectCount);
        Assert.Equal(2, treasureState.PairMatchCount);
        Assert.Equal(5, treasureState.PairQuantity);
        Assert.False(treasureState.PairEngaged);
        Assert.Equal(fruitState.UsedEntryCount, treasureState.UsedEntryCount);
        Assert.Equal(fruitState.EmptyEntryCount, treasureState.EmptyEntryCount);
    }

    private static AutoHarvestSubmissionState State(
        int emptyEntries,
        bool nativeHasEmptyEntry) =>
        new(
            isValid: true,
            usedEntryCount: 2,
            emptyEntryCount: emptyEntries,
            nativeHasEmptyEntry,
            supportedCollectCount: 0,
            pairMatchCount: 0,
            pairQuantity: 0,
            pairEngaged: false);

    /// <summary>
    /// A fruit-tree pair bound to stub natives, with the plot's instance list under test control.
    /// The contract is the production one, filled by reflection the way the binder fills it.
    /// </summary>
    private sealed class PrototypeWorld
    {
        private PrototypeWorld(
            StubPlot plot,
            StubAction action,
            StubPlot otherPlot,
            StubAction otherAction,
            in ResolvedAutoHarvestPair resolved)
        {
            Plot = plot;
            Action = action;
            OtherPlot = otherPlot;
            OtherAction = otherAction;
            Resolved = resolved;
        }

        internal StubPlot Plot { get; }
        internal StubAction Action { get; }
        internal StubPlot OtherPlot { get; }
        internal StubAction OtherAction { get; }
        internal ResolvedAutoHarvestPair Resolved { get; }
        internal List<object> Instances => Plot.instances;

        internal static PrototypeWorld Create()
        {
            var plot = new StubPlot();
            var action = new StubAction();
            var types = Types();
            var binding = new AutoHarvestPairBinding(
                AutoHarvestPair.FruitTree,
                plot,
                action,
                plot.GetGuid().ToString("D"),
                action.GetGuid().ToString("D"),
                new object(),
                null!,
                null!,
                null!);
            var shared = new AutoHarvestSharedBinding(new object(), null!, null!, 1);
            return new PrototypeWorld(
                plot,
                action,
                new StubPlot(),
                new StubAction(),
                new ResolvedAutoHarvestPair(Contract(types), shared, binding, binding, null));
        }

        /// <summary>
        /// Adds one instance to the pair's plot. The instance may name any plot and action — a plot
        /// holding an instance of something it does not offer is a shape the game produces.
        /// </summary>
        internal StubInstance AddInstance(StubPlot plot, StubAction action)
        {
            var instance = new StubInstance(plot, action);
            Plot.instances.Add(instance);
            return instance;
        }

        private static AutoHarvestReflectionTypes Types()
        {
            var types = (AutoHarvestReflectionTypes)Activator.CreateInstance(
                typeof(AutoHarvestReflectionTypes),
                nonPublic: true)!;
            Set(types, nameof(AutoHarvestReflectionTypes.Plot), typeof(StubPlot));
            Set(types, nameof(AutoHarvestReflectionTypes.Action), typeof(StubAction));
            Set(types, nameof(AutoHarvestReflectionTypes.Instance), typeof(StubInstance));
            return types;
        }

        private static AutoHarvestReflectionContract Contract(AutoHarvestReflectionTypes types)
        {
            var constructor = typeof(AutoHarvestReflectionContract).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                new[] { typeof(AutoHarvestReflectionTypes) },
                modifiers: null)!;
            var contract = (AutoHarvestReflectionContract)constructor.Invoke(new object[] { types });
            Set(contract, nameof(AutoHarvestReflectionContract.PlotStableId),
                AutoHarvestStableIdAccessor.Bind(typeof(StubPlot)));
            Set(contract, nameof(AutoHarvestReflectionContract.ActionStableId),
                AutoHarvestStableIdAccessor.Bind(typeof(StubAction)));
            Set(contract, nameof(AutoHarvestReflectionContract.PlotGetActionInstances),
                typeof(StubPlot).GetMethod(nameof(StubPlot.GetActionInstances))!);
            Set(contract, nameof(AutoHarvestReflectionContract.InstanceGetElement),
                typeof(StubInstance).GetMethod(nameof(StubInstance.GetElement))!);
            Set(contract, nameof(AutoHarvestReflectionContract.InstanceGetAction),
                typeof(StubInstance).GetMethod(nameof(StubInstance.GetAction))!);
            return contract;
        }

        private static void Set(object target, string property, object value) =>
            target.GetType().GetProperty(property)!.SetValue(target, value);
    }

    private sealed class StubPlot : IdScriptableObject
    {
        public readonly List<object> instances = new();

        public List<object> GetActionInstances() => instances;
    }

    private sealed class StubAction : IdScriptableObject
    {
    }

    private sealed class StubInstance
    {
        private readonly StubPlot _plot;
        private readonly StubAction _action;

        internal StubInstance(StubPlot plot, StubAction action)
        {
            _plot = plot;
            _action = action;
        }

        public StubPlot GetElement() => _plot;
        public StubAction GetAction() => _action;
    }
}
