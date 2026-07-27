using System;
using System.Reflection;
using HarmonyLib;
using OrbModding.Common;

namespace OrbAutomata;

internal static class AutoCastManualSignal
{
    [ThreadStatic]
    private static int _automatedDepth;

    private static long _fireEpoch;

    public static event Action? ManualSpellFired;

    public static long FireEpoch => _fireEpoch;

    public static IDisposable EnterAutomatedFire()
    {
        _automatedDepth++;
        return new Scope();
    }

    public static void NotifySpellFire()
    {
        _fireEpoch++;
        if (_automatedDepth == 0)
        {
            ManualSpellFired?.Invoke();
        }
    }

    private sealed class Scope : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _automatedDepth = Math.Max(0, _automatedDepth - 1);
        }
    }
}

[HarmonyPatch]
internal static class SpellFirePatch
{
    private static MethodBase? TargetMethod()
    {
        return ReflectionUtil.FindLoadedType("Spell")?.GetMethod(
            "Fire",
            ReflectionUtil.InstanceFlags,
            null,
            Type.EmptyTypes,
            null);
    }

    private static void Prefix()
    {
        AutoCastManualSignal.NotifySpellFire();
    }
}
