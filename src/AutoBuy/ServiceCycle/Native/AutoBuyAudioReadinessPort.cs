using System;
using System.Collections;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// A read-only action-boundary view of the native audio pool. Auto Buy needs this because an Upgrade
/// can claim a looping processing slot and every Structure/Upgrade completion can immediately ask for
/// a one-shot slot after changing progression state.
/// </summary>
internal interface IAutoBuyAudioReadinessPort
{
    bool TryReadReusableSlots(out int reusableSlots, out string reason);
}

/// <summary>
/// Exact reflected reader for <c>SoundManager.GetAudioElement()</c>'s availability rule. It never
/// advances the native current index and never initializes, stops, or replaces an AudioElement.
/// </summary>
internal sealed class AutoBuyNativeAudioReadinessAdapter : IAutoBuyAudioReadinessPort
{
    private const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;
    private const BindingFlags PublicStatic = BindingFlags.Static | BindingFlags.Public;
    private const BindingFlags AnyInstance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private Type? _managerType;
    private Type? _elementType;
    private FieldInfo? _instance;
    private FieldInfo? _maximum;
    private FieldInfo? _elements;
    private FieldInfo? _currentIndex;
    private MethodInfo? _isPlaying;
    private MethodInfo? _isLooping;
    private string? _blockedReason;

    public bool TryReadReusableSlots(out int reusableSlots, out string reason)
    {
        reusableSlots = 0;
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
                reason =
                    $"SoundManager.audioElements is incomplete for audioMaximum={maximum}.";
                return false;
            }
            if (_currentIndex!.GetValue(manager) is not int current ||
                current < 0 || current >= maximum)
            {
                reason = $"SoundManager.currentIndex is outside [0,{maximum}).";
                return false;
            }

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
                // This is the native allocator's rule: an idle element is directly usable and a
                // playing non-loop is its fallback after a full scan. Only playing loops are pinned.
                if (!playing || !looping) reusableSlots++;
            }

            reason = reusableSlots == 0
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

/// <summary>Compatibility default for isolated component tests that do not model native audio.</summary>
internal sealed class AutoBuyPermissiveAudioReadinessPort : IAutoBuyAudioReadinessPort
{
    internal static AutoBuyPermissiveAudioReadinessPort Instance { get; } = new();

    private AutoBuyPermissiveAudioReadinessPort()
    {
    }

    public bool TryReadReusableSlots(out int reusableSlots, out string reason)
    {
        reusableSlots = int.MaxValue;
        reason = string.Empty;
        return true;
    }
}
