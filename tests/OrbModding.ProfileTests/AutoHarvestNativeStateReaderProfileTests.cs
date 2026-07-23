using System;
using System.Collections.Generic;
using System.Reflection;
using OrbAutomata;
using OrbAutomata.Runtime.ServiceCycle.Profile;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
using Xunit;
using static OrbModding.ProfileTests.AutoHarvestProfileTestSupport;

namespace OrbModding.ProfileTests;

public sealed class AutoHarvestNativeStateReaderProfileTests
{
    [Fact]
    public void ProductionReaderCountsTheNativeWorkItPerforms()
    {
        var measurement = new CapturingMeasurementPort();
        var probe = new ServiceCycleProfileProbe();
        probe.Attach(measurement);
        var operations = new AutoHarvestProfileOperations(probe);
        var fixture = NativeReaderFixture.Create();
        var reader = new AutoHarvestNativeStateReader(operations);
        var context = CaptureContext(serviceOrdinal: 0, frameIdentity: 1);

        var activeStage = operations.Begin(
            AutoHarvestServiceCycleProfileStageCodes.ActiveActionTraversal,
            context,
            ServiceCycleProfileTemperature.Warm);
        AutoHarvestActiveActionSnapshot active;
        try
        {
            active = reader.CaptureActiveActions(fixture.Resolved);
            activeStage.Complete();
        }
        finally
        {
            activeStage.Abandon();
        }

        var factStage = operations.Begin(
            AutoHarvestServiceCycleProfileStageCodes.FruitFactCapture,
            context,
            ServiceCycleProfileTemperature.Warm);
        AutoHarvestPairFacts facts;
        try
        {
            reader.ReadFacts(
                fixture.Resolved,
                active.Project(AutoHarvestPair.FruitTree),
                out facts,
                out _);
            factStage.Complete();
        }
        finally
        {
            factStage.Abandon();
        }

        Assert.True(active.IsValid);
        Assert.Equal(AutoHarvestEvidenceState.Verified, facts.Identity);
        Assert.Equal(AutoHarvestEvidenceState.Verified, facts.Readiness);
        Assert.Collection(
            measurement.Completed,
            item => AssertOperations(
                item,
                AutoHarvestServiceCycleProfileStageCodes.ActiveActionTraversal,
                fieldReads: 1,
                methodCalls: 7,
                stableIdReads: 2,
                listEntries: 1,
                argumentArrays: 0),
            item => AssertOperations(
                item,
                AutoHarvestServiceCycleProfileStageCodes.FruitFactCapture,
                fieldReads: 1,
                methodCalls: 9,
                stableIdReads: 4,
                listEntries: 2,
                argumentArrays: 1));
        Assert.Empty(measurement.Abandoned);
        Assert.Same(measurement, probe.Detach());
    }

    private static void AssertOperations(
        in CapturedMeasurement item,
        int stageCode,
        uint fieldReads,
        uint methodCalls,
        uint stableIdReads,
        uint listEntries,
        uint argumentArrays)
    {
        Assert.Equal(stageCode, item.Context.StageCode);
        Assert.Equal(fieldReads, item.Operations.ReflectedFieldReads);
        Assert.Equal(methodCalls, item.Operations.ReflectedMethodCalls);
        Assert.Equal(stableIdReads, item.Operations.StableIdReads);
        Assert.Equal(listEntries, item.Operations.ListEntries);
        Assert.Equal(argumentArrays, item.Operations.InvocationArgumentArrays);
    }

    private sealed class NativeReaderFixture
    {
        private NativeReaderFixture(ResolvedAutoHarvestPair resolved) => Resolved = resolved;

        internal ResolvedAutoHarvestPair Resolved { get; }

        internal static NativeReaderFixture Create()
        {
            var plot = new ProfilePlot();
            var action = new ProfileAction();
            var instance = new ProfileInstance(plot, action);
            plot.availableActions.Add(action);
            plot.instances.Add(instance);
            var active = new ProfileActiveActions(instance);
            var types = CreateTypes();
            var contract = CreateContract(types);
            var shared = new AutoHarvestSharedBinding(active, new object(), null!, null!, 1);
            var fruit = new AutoHarvestPairBinding(
                AutoHarvestPair.FruitTree,
                plot,
                action,
                plot.GetGuid().ToString("D"),
                action.GetGuid().ToString("D"),
                new object(),
                null!,
                null!,
                null!,
                growthSeconds: 1,
                restSeconds: 1,
                actionSeconds: 1)
            {
                ActionSafety = AutoHarvestActionSafetyState.NativePhaseCyclePreserving,
            };
            return new NativeReaderFixture(
                new ResolvedAutoHarvestPair(contract, shared, fruit, fruit, null));
        }

        private static AutoHarvestReflectionTypes CreateTypes()
        {
            var types = (AutoHarvestReflectionTypes)Activator.CreateInstance(
                typeof(AutoHarvestReflectionTypes),
                nonPublic: true)!;
            Set(types, nameof(AutoHarvestReflectionTypes.Plot), typeof(ProfilePlot));
            Set(types, nameof(AutoHarvestReflectionTypes.Action), typeof(ProfileAction));
            Set(types, nameof(AutoHarvestReflectionTypes.Instance), typeof(ProfileInstance));
            Set(types, nameof(AutoHarvestReflectionTypes.ActiveActions), typeof(ProfileActiveActions));
            return types;
        }

        private static AutoHarvestReflectionContract CreateContract(
            AutoHarvestReflectionTypes types)
        {
            var constructor = typeof(AutoHarvestReflectionContract).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                new[] { typeof(AutoHarvestReflectionTypes) },
                modifiers: null)!;
            var contract = (AutoHarvestReflectionContract)constructor.Invoke(new object[] { types });
            Set(contract, nameof(AutoHarvestReflectionContract.PlotStableId),
                AutoHarvestStableIdAccessor.Bind(typeof(ProfilePlot)));
            Set(contract, nameof(AutoHarvestReflectionContract.ActionStableId),
                AutoHarvestStableIdAccessor.Bind(typeof(ProfileAction)));
            Set(contract, nameof(AutoHarvestReflectionContract.PlotAvailableActions),
                Field<ProfilePlot>(nameof(ProfilePlot.availableActions)));
            Set(contract, nameof(AutoHarvestReflectionContract.PlotIsVisible),
                Method<ProfilePlot>(nameof(ProfilePlot.IsVisible)));
            Set(contract, nameof(AutoHarvestReflectionContract.PlotGetActionInstances),
                Method<ProfilePlot>(nameof(ProfilePlot.GetActionInstances)));
            Set(contract, nameof(AutoHarvestReflectionContract.PlotGetRemainingQuantity),
                Method<ProfilePlot>(nameof(ProfilePlot.GetRemainingQuantity)));
            Set(contract, nameof(AutoHarvestReflectionContract.ActionGetElementCost),
                Method<ProfileAction>(nameof(ProfileAction.GetElementCost), typeof(ProfilePlot)));
            Set(contract, nameof(AutoHarvestReflectionContract.InstanceGetAction),
                Method<ProfileInstance>(nameof(ProfileInstance.GetAction)));
            Set(contract, nameof(AutoHarvestReflectionContract.InstanceGetElement),
                Method<ProfileInstance>(nameof(ProfileInstance.GetElement)));
            Set(contract, nameof(AutoHarvestReflectionContract.InstanceIsVisible),
                Method<ProfileInstance>(nameof(ProfileInstance.IsVisible)));
            Set(contract, nameof(AutoHarvestReflectionContract.InstanceIsEmpty),
                Method<ProfileInstance>(nameof(ProfileInstance.IsEmpty)));
            Set(contract, nameof(AutoHarvestReflectionContract.InstanceIsEngaged),
                Method<ProfileInstance>(nameof(ProfileInstance.IsEngaged)));
            Set(contract, nameof(AutoHarvestReflectionContract.InstanceHasEnough),
                Method<ProfileInstance>(nameof(ProfileInstance.HasEnoughForOneInstance)));
            Set(contract, nameof(AutoHarvestReflectionContract.InstanceGetMaximumRemaining),
                Method<ProfileInstance>(nameof(ProfileInstance.GetMaximumRemInstances)));
            Set(contract, nameof(AutoHarvestReflectionContract.InstanceGetActualQuantity),
                Method<ProfileInstance>(nameof(ProfileInstance.GetActualQuantity)));
            Set(contract, nameof(AutoHarvestReflectionContract.ActiveValues),
                Field<ProfileActiveActions>(nameof(ProfileActiveActions.value)));
            Set(contract, nameof(AutoHarvestReflectionContract.ActiveGetUsedSpots),
                Method<ProfileActiveActions>(nameof(ProfileActiveActions.GetUsedSpots)));
            Set(contract, nameof(AutoHarvestReflectionContract.ActiveHasEmptySpot),
                Method<ProfileActiveActions>(nameof(ProfileActiveActions.HasEmptySpot)));
            return contract;
        }

        private static FieldInfo Field<T>(string name) => typeof(T).GetField(name)!;

        private static MethodInfo Method<T>(string name, params Type[] parameters) =>
            typeof(T).GetMethod(name, parameters)!;

        private static void Set(object target, string property, object value) =>
            target.GetType().GetProperty(property)!.SetValue(target, value);
    }

    private sealed class ProfilePlot : IdScriptableObject
    {
        public readonly List<ProfileAction> availableActions = new();
        public readonly List<ProfileInstance> instances = new();
        public bool IsVisible() => true;
        public List<ProfileInstance> GetActionInstances() => instances;
        public int GetRemainingQuantity() => 1;
    }

    private sealed class ProfileAction : IdScriptableObject
    {
        public int GetElementCost(ProfilePlot plot) => plot is null ? 0 : 1;
    }

    private sealed class ProfileInstance
    {
        private readonly ProfilePlot _plot;
        private readonly ProfileAction _action;

        internal ProfileInstance(ProfilePlot plot, ProfileAction action)
        {
            _plot = plot;
            _action = action;
        }

        public ProfileAction GetAction() => _action;
        public ProfilePlot GetElement() => _plot;
        public bool IsVisible() => true;
        public bool IsEmpty() => false;
        public bool IsEngaged() => false;
        public bool HasEnoughForOneInstance() => true;
        public int GetMaximumRemInstances() => 1;
        public int GetActualQuantity() => 1;
    }

    private sealed class ProfileActiveActions
    {
        internal ProfileActiveActions(ProfileInstance instance) => value.Add(instance);
        public readonly List<ProfileInstance> value = new();
        public int GetUsedSpots() => 1;
        public bool HasEmptySpot() => true;
    }
}
