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
        AssertTarget("OrbAutomata.SpellFirePatch", "Fire", typeof(Spell));

        AssertTargets(
            "OrbAutomata.AutoConceptActiveListPatch",
            (typeof(AlchemyInstanceListVariable), "AddAlchemyInstances", new[] { typeof(AlchemyRecipeSO), typeof(int) }),
            (typeof(AlchemyInstanceListVariable), "RemoveAlchemyInstances", new[] { typeof(AlchemyRecipeSO), typeof(int) }));
        AssertTargets(
            "OrbAutomata.AutoConceptActiveListBroadPatch",
            (typeof(AlchemyInstanceListVariable), "RebuildCounts", Type.EmptyTypes),
            (typeof(AlchemyInstanceListVariable), "SetupMaxSlotsValue", Type.EmptyTypes));
        AssertTargets(
            "OrbAutomata.AutoConceptProgressionPatch",
            (typeof(AlchemyRecipeSO), "Discover", Type.EmptyTypes),
            (typeof(AlchemyRecipeSO), "ApplyMastery", Type.EmptyTypes));
    }

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void AutoConceptPatchCallbacks_SeparateInventoryFromProgression()
    {
        var recipe = new AlchemyRecipeSO();
        var inventory = new List<object?>();
        var progression = new List<object>();
        AutoConceptLifecycleSignal.InventoryChanged += OnInventory;
        AutoConceptLifecycleSignal.ProgressionChanged += OnProgression;
        try
        {
            InvokePatch("OrbAutomata.AutoConceptActiveListPatch", "Postfix", recipe);
            InvokePatch("OrbAutomata.AutoConceptActiveListBroadPatch", "Postfix");
            InvokePatch("OrbAutomata.AutoConceptProgressionPatch", "Postfix", recipe);

            Assert.Equal(2, inventory.Count);
            Assert.Same(recipe, inventory[0]);
            Assert.Null(inventory[1]);
            Assert.Same(recipe, Assert.Single(progression));
        }
        finally
        {
            AutoConceptLifecycleSignal.InventoryChanged -= OnInventory;
            AutoConceptLifecycleSignal.ProgressionChanged -= OnProgression;
        }

        void OnInventory(object? identity) => inventory.Add(identity);
        void OnProgression(object identity) => progression.Add(identity);
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
        var type = typeof(AutoConceptLifecycleSignal).Assembly.GetType(patchTypeName, throwOnError: true)!;
        var method = type.GetMethod(
            methodName,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) ??
            throw new MissingMethodException(patchTypeName, methodName);
        return method.Invoke(null, arguments);
    }
}
