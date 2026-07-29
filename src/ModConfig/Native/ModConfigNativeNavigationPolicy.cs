using System;
using System.Collections.Generic;

namespace OrbModConfig;

internal readonly struct NativeNavigationRecovery
{
    public NativeNavigationRecovery(bool restorePrevious, bool restoreFallback)
    {
        RestorePrevious = restorePrevious;
        RestoreFallback = restoreFallback;
    }

    public bool RestorePrevious { get; }
    public bool RestoreFallback { get; }
    public bool RepairRequired => true;
}

/// <summary>
/// Pure collection and recovery rules used by the Unity-facing navigation host.
/// Keeping these decisions outside the adapter makes teardown and host-loss
/// behavior portable-testable without constructing scene objects.
/// </summary>
internal static class ModConfigNativeNavigationPolicy
{
    public static bool HostsAlive(
        bool hostHealthy,
        bool buttonAlive,
        bool panelAlive,
        bool parentsAlive) =>
        hostHealthy && buttonAlive && panelAlive && parentsAlive;

    public static bool ShouldRestoreNativeView(bool previousAlive, bool anyNativeActive) =>
        previousAlive && !anyNativeActive;

    public static NativeNavigationRecovery OpenFailureRecovery(
        bool restoreRequested,
        bool previousAlive,
        bool fallbackAlive,
        bool anyNativeActive) =>
        new(
            restoreRequested && ShouldRestoreNativeView(previousAlive, anyNativeActive),
            restoreRequested && !previousAlive && fallbackAlive && !anyNativeActive);

    public static int PruneDead<T>(
        List<T> items,
        Func<T, bool> isAlive,
        Action<T>? onRemoved = null)
    {
        var removed = 0;
        for (var index = items.Count - 1; index >= 0; index--)
        {
            if (isAlive(items[index])) continue;
            var item = items[index];
            items.RemoveAt(index);
            removed++;
            try { onRemoved?.Invoke(item); } catch { }
        }

        return removed;
    }

    public static int DetachAll<T>(List<T> items, Action<T> detach)
    {
        var detached = 0;
        foreach (var item in items)
        {
            try { detach(item); } catch { }
            detached++;
        }

        items.Clear();
        return detached;
    }
}
