using System;
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

    private static void AssertMethod(
        MethodBase method,
        Type declaringType,
        string name,
        Type[] parameters)
    {
        Assert.True(
            MethodMatches(method, declaringType, name, parameters),
            $"Unexpected target {method.DeclaringType?.FullName}.{method.Name}.");
    }

    private static bool MethodMatches(
        MethodBase method,
        Type declaringType,
        string name,
        Type[] parameters)
    {
        return method.DeclaringType == declaringType &&
               method.Name == name &&
               ParametersMatch(method, parameters);
    }

    private static bool ParametersMatch(MethodBase method, Type[] parameters)
    {
        var actual = method.GetParameters();
        if (actual.Length != parameters.Length) return false;
        for (var index = 0; index < actual.Length; index++)
            if (actual[index].ParameterType != parameters[index]) return false;
        return true;
    }

    private static object? InvokePatch(string patchTypeName, string methodName, params object[] arguments)
    {
        var type = typeof(SpellFirePatch).Assembly.GetType(patchTypeName, throwOnError: true)!;
        var method = type.GetMethod(
            methodName,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) ??
            throw new MissingMethodException(patchTypeName, methodName);
        return method.Invoke(null, arguments);
    }
}
