using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using OrbAutomata;
using Xunit;

namespace OrbModding.Tests;

public sealed class HarmonyBindingHeadlessTests
{
    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void AutomataHarmonyTargets_ResolveExactNativeShapedMethods()
    {
        AssertTarget("OrbAutomata.AutoBuyStructureQueuePatch", "QueueBuild", typeof(global::StructureSO), typeof(int));
        AssertTarget("OrbAutomata.AutoBuyUpgradeQueuePatch", "Purchase", typeof(UpgradeSO));
        AssertTarget("OrbAutomata.SpellFirePatch", "Fire", typeof(Spell));

        AssertTargets(
            "OrbAutomata.AutoBuyNativeCompletionPatch",
            (typeof(global::StructureSO), "CompleteAction", Type.EmptyTypes),
            (typeof(UpgradeSO), "CompleteAction", Type.EmptyTypes));
        AssertTargets(
            "OrbAutomata.AutoBuyLifecyclePatch",
            (typeof(Player), "ManagerStart", Type.EmptyTypes),
            (typeof(SaveStateManager), "ImplementLoadedJson", Type.EmptyTypes));
        AssertTargets(
            "OrbAutomata.AutoConceptActiveListPatch",
            (typeof(AlchemyInstanceListVariable), "AddAlchemyInstances", new[] { typeof(AlchemyRecipeSO), typeof(int) }),
            (typeof(AlchemyInstanceListVariable), "RemoveAlchemyInstances", new[] { typeof(AlchemyRecipeSO), typeof(int) }),
            (typeof(AlchemyInstanceListVariable), "RebuildCounts", Type.EmptyTypes),
            (typeof(AlchemyInstanceListVariable), "SetupMaxSlotsValue", Type.EmptyTypes));
        AssertTargets(
            "OrbAutomata.AutoConceptProgressionPatch",
            (typeof(AlchemyRecipeSO), "Discover", Type.EmptyTypes),
            (typeof(AlchemyRecipeSO), "ApplyMastery", Type.EmptyTypes));
    }

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void AutoBuyPatchCallbacks_SuppressOnlyExactAutomatedIdentityAndEmitOneCompletionSignal()
    {
        var automated = new global::StructureSO();
        var manual = new global::StructureSO();
        var structureSignals = new List<object>();
        var completions = 0;
        AutoBuyLifecycleSignal.StructureQueueChanged += OnStructure;
        AutoBuyLifecycleSignal.NativeCompletion += OnCompletion;
        try
        {
            using (AutoBuyLifecycleSignal.EnterAutomatedMutation(automated))
            {
                InvokePatch("OrbAutomata.AutoBuyStructureQueuePatch", "Postfix", automated);
                InvokePatch("OrbAutomata.AutoBuyStructureQueuePatch", "Postfix", manual);
            }

            InvokePatch("OrbAutomata.AutoBuyNativeCompletionPatch", "Postfix");

            Assert.Single(structureSignals);
            Assert.Same(manual, structureSignals[0]);
            Assert.Equal(1, completions);
        }
        finally
        {
            AutoBuyLifecycleSignal.StructureQueueChanged -= OnStructure;
            AutoBuyLifecycleSignal.NativeCompletion -= OnCompletion;
        }

        void OnStructure(object identity) => structureSignals.Add(identity);
        void OnCompletion() => completions++;
    }

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void SpellFirePrefix_DistinguishesManualAndAutomatedFireScopes()
    {
        var manualSignals = 0;
        AutoCastManualSignal.ManualSpellFired += OnManual;
        try
        {
            InvokePatch("OrbAutomata.SpellFirePatch", "Prefix");
            using (AutoCastManualSignal.EnterAutomatedFire())
            {
                InvokePatch("OrbAutomata.SpellFirePatch", "Prefix");
            }

            Assert.Equal(1, manualSignals);
        }
        finally
        {
            AutoCastManualSignal.ManualSpellFired -= OnManual;
        }

        void OnManual() => manualSignals++;
    }

    private static void AssertTarget(
        string patchType,
        string methodName,
        Type declaringType,
        params Type[] parameters)
    {
        var target = Assert.IsAssignableFrom<MethodBase>(InvokePatch(patchType, "TargetMethod"));
        AssertMethod(target, declaringType, methodName, parameters);
    }

    private static void AssertTargets(
        string patchType,
        params (Type DeclaringType, string Name, Type[] Parameters)[] expected)
    {
        var result = Assert.IsAssignableFrom<IEnumerable>(InvokePatch(patchType, "TargetMethods"));
        var methods = result.Cast<MethodBase>().ToArray();
        Assert.Equal(expected.Length, methods.Length);
        foreach (var contract in expected)
        {
            Assert.Contains(methods, method =>
                MethodMatches(method, contract.DeclaringType, contract.Name, contract.Parameters));
        }
    }

    private static void AssertMethod(
        MethodBase method,
        Type declaringType,
        string name,
        IReadOnlyList<Type> parameters)
    {
        Assert.True(
            MethodMatches(method, declaringType, name, parameters),
            $"Unexpected target {method.DeclaringType?.FullName}.{method.Name}.");
    }

    private static bool MethodMatches(
        MethodBase method,
        Type declaringType,
        string name,
        IReadOnlyList<Type> parameters)
    {
        return method.DeclaringType == declaringType &&
               method.Name == name &&
               method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(parameters);
    }

    private static object? InvokePatch(string patchTypeName, string methodName, params object[] arguments)
    {
        var type = typeof(AutoBuyLifecycleSignal).Assembly.GetType(patchTypeName, throwOnError: true)!;
        var method = type.GetMethod(
            methodName,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) ??
            throw new MissingMethodException(patchTypeName, methodName);
        return method.Invoke(null, arguments);
    }
}
