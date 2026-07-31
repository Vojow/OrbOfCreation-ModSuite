using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using OrbModding;
using OrbModding.Common;
using UnityEngine;

namespace OrbAutomata;

internal readonly struct NativeAudioPoolSnapshot
{
    internal NativeAudioPoolSnapshot(
        int maximum,
        int currentIndex,
        int idle,
        int reusableNonLooping,
        int playingLooping)
    {
        Maximum = maximum;
        CurrentIndex = currentIndex;
        Idle = idle;
        ReusableNonLooping = reusableNonLooping;
        PlayingLooping = playingLooping;
    }

    internal int Maximum { get; }
    internal int CurrentIndex { get; }
    internal int Idle { get; }
    internal int ReusableNonLooping { get; }
    internal int PlayingLooping { get; }
    internal int Reusable => checked(Idle + ReusableNonLooping);
}

/// <summary>
/// Read-only projection of the exact selection rule used by SoundManager.GetAudioElement(). It
/// never advances currentIndex and never starts, stops, or replaces an AudioElement.
/// </summary>
internal sealed class NativeAudioPoolReadinessAdapter
{
    private const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;
    private const BindingFlags PublicStatic = BindingFlags.Static | BindingFlags.Public;
    private const BindingFlags AnyInstance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    internal static NativeAudioPoolReadinessAdapter Shared { get; } = new();

    private Type? _managerType;
    private Type? _elementType;
    private FieldInfo? _instance;
    private FieldInfo? _maximum;
    private FieldInfo? _elements;
    private FieldInfo? _currentIndex;
    private MethodInfo? _isPlaying;
    private MethodInfo? _isLooping;
    private string? _blockedReason;

    internal bool TryRead(out NativeAudioPoolSnapshot snapshot, out string reason)
    {
        snapshot = default;
        if (!TryBind(out reason)) return false;

        try
        {
            var manager = _instance!.GetValue(null);
            if (manager is null || manager.GetType() != _managerType)
            {
                reason = "SoundManager.instance is unavailable.";
                return false;
            }
            if (_maximum!.GetValue(manager) is not int maximum || maximum <= 0)
            {
                reason = "SoundManager.audioMaximum is not positive.";
                return false;
            }
            if (_elements!.GetValue(manager) is not IList elements || elements.Count < maximum)
            {
                reason = $"SoundManager.audioElements is incomplete for audioMaximum={maximum}.";
                return false;
            }
            if (_currentIndex!.GetValue(manager) is not int current ||
                current < 0 || current >= maximum)
            {
                reason = $"SoundManager.currentIndex is outside [0,{maximum}).";
                return false;
            }

            var idle = 0;
            var reusableNonLooping = 0;
            var playingLooping = 0;
            for (var offset = 0; offset < maximum; offset++)
            {
                var index = (current + offset) % maximum;
                var element = elements[index];
                if (element is null || element.GetType() != _elementType)
                {
                    reason = $"SoundManager.audioElements[{index}] is unavailable.";
                    return false;
                }

                var playing = InvokeBool(_isPlaying!, element);
                var looping = InvokeBool(_isLooping!, element);
                if (!playing) idle++;
                else if (!looping) reusableNonLooping++;
                else playingLooping++;
            }

            snapshot = new NativeAudioPoolSnapshot(
                maximum,
                current,
                idle,
                reusableNonLooping,
                playingLooping);
            reason = snapshot.Reusable == 0
                ? "SoundManager has no idle or reusable non-looping AudioElement."
                : string.Empty;
            return true;
        }
        catch (Exception ex) when (IsExpectedFailure(ex))
        {
            reason = "Native audio readiness could not be read: " + ex.GetBaseException().Message;
            return false;
        }
    }

    private bool TryBind(out string reason)
    {
        if (_blockedReason is not null)
        {
            reason = _blockedReason;
            return false;
        }
        if (_managerType is not null)
        {
            reason = string.Empty;
            return true;
        }

        var manager = ReflectionUtil.FindLoadedType("SoundManager");
        var element = ReflectionUtil.FindLoadedType("AudioElement");
        var instance = manager?.GetField("instance", PublicStatic);
        var maximum = manager?.GetField("audioMaximum", PublicInstance);
        var elements = manager?.GetField("audioElements", AnyInstance);
        var currentIndex = manager?.GetField("currentIndex", AnyInstance);
        var isPlaying = element?.GetMethod(
            "IsPlaying", PublicInstance, null, Type.EmptyTypes, null);
        var isLooping = element?.GetMethod(
            "IsLooping", PublicInstance, null, Type.EmptyTypes, null);

        if (manager is null || element is null ||
            instance?.FieldType != manager || maximum?.FieldType != typeof(int) ||
            elements is null || currentIndex?.FieldType != typeof(int) ||
            isPlaying?.ReturnType != typeof(bool) || isLooping?.ReturnType != typeof(bool))
        {
            _blockedReason =
                "The exact SoundManager/AudioElement readiness contract is unavailable.";
            reason = _blockedReason;
            return false;
        }

        _managerType = manager;
        _elementType = element;
        _instance = instance;
        _maximum = maximum;
        _elements = elements;
        _currentIndex = currentIndex;
        _isPlaying = isPlaying;
        _isLooping = isLooping;
        reason = string.Empty;
        return true;
    }

    private static bool InvokeBool(MethodInfo method, object target) =>
        method.Invoke(target, Array.Empty<object>()) is bool value
            ? value
            : throw new InvalidOperationException(
                $"{method.DeclaringType?.Name}.{method.Name} did not return Boolean.");

    private static bool IsExpectedFailure(Exception ex) =>
        ex is TargetInvocationException or ArgumentException or InvalidOperationException or
            TargetException or MemberAccessException;
}

internal readonly struct NativeUpgradeLoopAggregationSnapshot
{
    internal NativeUpgradeLoopAggregationSnapshot(
        long lifecycle,
        bool enabled,
        int activeGroups,
        int activeLeases,
        long nativeLoopsStarted,
        long coalescedRequests,
        long reserveSuppressions,
        long finalStops,
        long stopFailures)
    {
        Lifecycle = lifecycle;
        Enabled = enabled;
        ActiveGroups = activeGroups;
        ActiveLeases = activeLeases;
        NativeLoopsStarted = nativeLoopsStarted;
        CoalescedRequests = coalescedRequests;
        ReserveSuppressions = reserveSuppressions;
        FinalStops = finalStops;
        StopFailures = stopFailures;
    }

    internal long Lifecycle { get; }
    internal bool Enabled { get; }
    internal int ActiveGroups { get; }
    internal int ActiveLeases { get; }
    internal long NativeLoopsStarted { get; }
    internal long CoalescedRequests { get; }
    internal long ReserveSuppressions { get; }
    internal long FinalStops { get; }
    internal long StopFailures { get; }
}

/// <summary>
/// Shares only identical Upgrade processing loops. Spell, brewing, and one-shot audio never enter
/// this scope. All calls are made by Harmony on Unity's main thread.
/// </summary>
internal static class NativeUpgradeLoopAggregation
{
    private const int MinimumReusableAfterNewUpgradeLoop = 1;

    [ThreadStatic]
    private static int _upgradeScopeDepth;

    private static readonly Dictionary<UpgradeLoopKey, SharedUpgradeLoop> ByKey = new();
    private static readonly Dictionary<AudioElement, SharedUpgradeLoop> ByElement =
        new(ReferenceComparer<AudioElement>.Instance);
    private static long _observedLifecycle = long.MinValue;
    private static bool _enabled = true;
    private static long _nativeLoopsStarted;
    private static long _coalescedRequests;
    private static long _reserveSuppressions;
    private static long _finalStops;
    private static long _stopFailures;
    private static Func<long> _readLifecycle = ReadCurrentLifecycle;

    internal static void EnterUpgradeScope()
    {
        SynchronizeLifecycle();
        _upgradeScopeDepth = checked(_upgradeScopeDepth + 1);
    }

    internal static void ExitUpgradeScope()
    {
        if (_upgradeScopeDepth > 0) _upgradeScopeDepth--;
    }

    internal static bool PrefixPlayLoop(
        AudioClip audioClip,
        float volume,
        ref AudioElement result,
        out bool registerNativeResult)
    {
        registerNativeResult = false;
        SynchronizeLifecycle();
        if (_upgradeScopeDepth <= 0 || !_enabled || audioClip is null)
            return true;

        var key = new UpgradeLoopKey(audioClip, volume);
        if (ByKey.TryGetValue(key, out var shared) && shared.Element != null)
        {
            shared.Leases = checked(shared.Leases + 1);
            _coalescedRequests = checked(_coalescedRequests + 1);
            result = shared.Element;
            return false;
        }
        if (shared is not null)
        {
            ByKey.Remove(key);
            if (!ReferenceEquals(shared.Element, null)) ByElement.Remove(shared.Element);
        }

        if (!NativeAudioPoolReadinessAdapter.Shared.TryRead(out var pool, out _) ||
            pool.Reusable > MinimumReusableAfterNewUpgradeLoop)
        {
            registerNativeResult = true;
            return true;
        }

        // UpgradeSO.PlayProcessSound stores the return value without dereferencing it. Null is safe
        // in this exact scope and makes later CancelProcessSound a no-op for this cosmetic sound.
        _reserveSuppressions = checked(_reserveSuppressions + 1);
        result = null!;
        if (_reserveSuppressions == 1)
        {
            Plugin.Log?.LogAutomataWarning(
                "Upgrade processing audio reached its reserved native pool floor; suppressing " +
                "new unique Upgrade loops while preserving progression and one-shot capacity.");
        }
        return false;
    }

    internal static void PostfixPlayLoop(
        AudioClip audioClip,
        float volume,
        AudioElement result,
        bool registerNativeResult)
    {
        if (!registerNativeResult || result is null) return;
        SynchronizeLifecycle();
        var key = new UpgradeLoopKey(audioClip, volume);
        var shared = new SharedUpgradeLoop(key, result);
        ByKey[key] = shared;
        ByElement[result] = shared;
        _nativeLoopsStarted = checked(_nativeLoopsStarted + 1);
    }

    internal static bool PrefixFadeOutDestroy(
        AudioElement element,
        ref AudioElement result)
    {
        SynchronizeLifecycle();
        if (element is null || !ByElement.TryGetValue(element, out var shared))
            return true;
        if (shared.Leases > 1)
        {
            shared.Leases--;
            result = element;
            return false;
        }

        ByElement.Remove(element);
        ByKey.Remove(shared.Key);
        try
        {
            // The game's delayed FadeOutDestroy coroutine can lose its release under sustained
            // Upgrade churn. The final proven lease has no remaining native owner, so stop this
            // exact tracked loop synchronously and return its pool element immediately.
            element.Stop();
            _finalStops = checked(_finalStops + 1);
        }
        catch (Exception ex) when (!IsFatal(ex))
        {
            _stopFailures = checked(_stopFailures + 1);
            if (_stopFailures == 1)
            {
                Plugin.Log?.LogAutomataWarning(
                    "Upgrade processing audio could not stop its final shared native loop: " +
                    ex.GetBaseException().Message);
            }
        }
        result = element;
        return false;
    }

    internal static NativeUpgradeLoopAggregationSnapshot Capture()
    {
        SynchronizeLifecycle();
        var leases = 0;
        foreach (var group in ByKey.Values) leases = checked(leases + group.Leases);
        return new NativeUpgradeLoopAggregationSnapshot(
            _observedLifecycle,
            _enabled,
            ByKey.Count,
            leases,
            _nativeLoopsStarted,
            _coalescedRequests,
            _reserveSuppressions,
            _finalStops,
            _stopFailures);
    }

    internal static NativeUpgradeLoopAggregationSnapshot SetEnabled(bool enabled)
    {
        SynchronizeLifecycle();
        _enabled = enabled;
        return Capture();
    }

    internal static NativeUpgradeLoopAggregationSnapshot ResetCounters()
    {
        SynchronizeLifecycle();
        _nativeLoopsStarted = 0;
        _coalescedRequests = 0;
        _reserveSuppressions = 0;
        _finalStops = 0;
        _stopFailures = 0;
        return Capture();
    }

    internal static void ResetForTests()
    {
        _upgradeScopeDepth = 0;
        _observedLifecycle = long.MinValue;
        _enabled = true;
        _nativeLoopsStarted = 0;
        _coalescedRequests = 0;
        _reserveSuppressions = 0;
        _finalStops = 0;
        _stopFailures = 0;
        _readLifecycle = ReadCurrentLifecycle;
        ByElement.Clear();
        ByKey.Clear();
    }

    internal static void SetLifecycleReaderForTests(Func<long> readLifecycle)
    {
        _readLifecycle = readLifecycle ?? throw new ArgumentNullException(nameof(readLifecycle));
        _observedLifecycle = long.MinValue;
    }

    private static void SynchronizeLifecycle()
    {
        var lifecycle = ReadLifecycle();
        if (_observedLifecycle == lifecycle) return;
        _observedLifecycle = lifecycle;
        _upgradeScopeDepth = 0;
        _nativeLoopsStarted = 0;
        _coalescedRequests = 0;
        _reserveSuppressions = 0;
        _finalStops = 0;
        _stopFailures = 0;
        ByElement.Clear();
        ByKey.Clear();
    }

    private static long ReadLifecycle()
    {
        try { return _readLifecycle(); }
        catch { return -1; }
    }

    private static long ReadCurrentLifecycle() =>
        GameLifecycleMonitor.Shared.Current.Generation;

    private static bool IsFatal(Exception ex) =>
        ex is OutOfMemoryException or StackOverflowException or AccessViolationException;

    private readonly struct UpgradeLoopKey : IEquatable<UpgradeLoopKey>
    {
        internal UpgradeLoopKey(AudioClip clip, float volume)
        {
            Clip = clip;
            VolumeBits = BitConverter.SingleToInt32Bits(volume);
        }

        internal AudioClip Clip { get; }
        internal int VolumeBits { get; }

        public bool Equals(UpgradeLoopKey other) =>
            ReferenceEquals(Clip, other.Clip) && VolumeBits == other.VolumeBits;

        public override bool Equals(object? obj) => obj is UpgradeLoopKey other && Equals(other);
        public override int GetHashCode() => unchecked(
            (RuntimeHelpers.GetHashCode(Clip) * 397) ^ VolumeBits);
    }

    private sealed class SharedUpgradeLoop
    {
        internal SharedUpgradeLoop(UpgradeLoopKey key, AudioElement element)
        {
            Key = key;
            Element = element;
            Leases = 1;
        }

        internal UpgradeLoopKey Key { get; }
        internal AudioElement Element { get; }
        internal int Leases { get; set; }
    }

    private sealed class ReferenceComparer<T> : IEqualityComparer<T>
        where T : class
    {
        internal static ReferenceComparer<T> Instance { get; } = new();
        public bool Equals(T? x, T? y) => ReferenceEquals(x, y);
        public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
    }
}

[HarmonyPatch]
internal static class UpgradeProcessingSoundScopePatch
{
    internal static MethodBase? TargetMethod() =>
        ReflectionUtil.FindLoadedType("UpgradeSO")?.GetMethod(
            "PlayProcessSound",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);

    internal static void Prefix() => NativeUpgradeLoopAggregation.EnterUpgradeScope();

    internal static Exception? Finalizer(Exception? __exception)
    {
        NativeUpgradeLoopAggregation.ExitUpgradeScope();
        return __exception;
    }
}

[HarmonyPatch]
internal static class SoundManagerPlayLoopAggregationPatch
{
    internal static MethodBase? TargetMethod() =>
        ReflectionUtil.FindLoadedType("SoundManager")?.GetMethod(
            "PlayLoop",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly,
            null,
            new[] { typeof(AudioClip), typeof(float) },
            null);

    internal static bool Prefix(
        AudioClip audioClip,
        float volume,
        ref AudioElement __result,
        out bool __state) =>
        NativeUpgradeLoopAggregation.PrefixPlayLoop(
            audioClip,
            volume,
            ref __result,
            out __state);

    internal static void Postfix(
        AudioClip audioClip,
        float volume,
        AudioElement __result,
        bool __state) =>
        NativeUpgradeLoopAggregation.PostfixPlayLoop(
            audioClip,
            volume,
            __result,
            __state);
}

[HarmonyPatch]
internal static class AudioElementFadeOutDestroyAggregationPatch
{
    internal static MethodBase? TargetMethod() =>
        ReflectionUtil.FindLoadedType("AudioElement")?.GetMethod(
            "FadeOutDestroy",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            null,
            new[] { typeof(float) },
            null);

    internal static bool Prefix(AudioElement __instance, ref AudioElement __result) =>
        NativeUpgradeLoopAggregation.PrefixFadeOutDestroy(__instance, ref __result);
}
