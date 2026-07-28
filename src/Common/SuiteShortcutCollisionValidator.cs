using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace OrbModding.Common;

internal enum SuiteShortcutListenerKind
{
    PerFrameKeyboardPolling,
    RuntimePageButton,
}

internal readonly record struct SuiteShortcutListener(
    string Id,
    string DisplayName,
    KeyboardShortcut Shortcut,
    SuiteShortcutListenerKind Kind);

internal readonly record struct SuiteShortcutCollision(
    string ListenerId,
    string ListenerDisplayName,
    KeyCode Key,
    string ConflictingBinding,
    bool IsMainKey,
    bool IsSuiteListener);

internal static class SuiteShortcutCollisionValidator
{
    private static readonly IReadOnlyDictionary<KeyCode, string> AuditedNativeDefaults =
        new Dictionary<KeyCode, string>
        {
            [KeyCode.Q] = "Consumable 1",
            [KeyCode.W] = "Consumable 2",
            [KeyCode.E] = "Consumable 3",
            [KeyCode.R] = "Consumable 4",
            [KeyCode.Space] = "Search",
            [KeyCode.LeftShift] = "Increase Buy",
            [KeyCode.RightShift] = "Increase Buy",
            [KeyCode.T] = "Inspect Tooltip",
            [KeyCode.X] = "Open Inventory",
            [KeyCode.Z] = "Open Loadouts",
            [KeyCode.LeftControl] = "Max Buy",
            [KeyCode.RightControl] = "Max Buy",
            [KeyCode.LeftAlt] = "More Info",
            [KeyCode.RightAlt] = "More Info",
            [KeyCode.Alpha1] = "Spell 1",
            [KeyCode.Alpha2] = "Spell 2",
            [KeyCode.Alpha3] = "Spell 3",
            [KeyCode.Alpha4] = "Spell 4",
            [KeyCode.Alpha5] = "Spell 5",
            [KeyCode.Alpha6] = "Spell 6",
            [KeyCode.Alpha7] = "Spell 7",
            [KeyCode.Alpha8] = "Spell 8",
            [KeyCode.Alpha9] = "Spell 9",
            [KeyCode.Keypad1] = "Spell 1",
            [KeyCode.Keypad2] = "Spell 2",
            [KeyCode.Keypad3] = "Spell 3",
            [KeyCode.Keypad4] = "Spell 4",
            [KeyCode.Keypad5] = "Spell 5",
            [KeyCode.Keypad6] = "Spell 6",
            [KeyCode.Keypad7] = "Spell 7",
            [KeyCode.Keypad8] = "Spell 8",
            [KeyCode.Keypad9] = "Spell 9",
            [KeyCode.UpArrow] = "Tab Up",
            [KeyCode.DownArrow] = "Tab Down",
            [KeyCode.LeftArrow] = "Tab Left",
            [KeyCode.RightArrow] = "Tab Right",
        };

    public static IReadOnlyList<SuiteShortcutListener> Inventory(
        KeyboardShortcut autoCast,
        KeyboardShortcut mentor) =>
        new[]
        {
            new SuiteShortcutListener(
                "auto-cast-toggle",
                "Auto Cast toggle",
                autoCast,
                SuiteShortcutListenerKind.PerFrameKeyboardPolling),
            new SuiteShortcutListener(
                "mentor-toggle",
                "Mentor toggle",
                mentor,
                SuiteShortcutListenerKind.PerFrameKeyboardPolling),
            new SuiteShortcutListener(
                "differential-verifier",
                "Differential verifier",
                new KeyboardShortcut(KeyCode.None),
                SuiteShortcutListenerKind.RuntimePageButton),
        };

    public static IReadOnlyList<SuiteShortcutCollision> Validate(
        IReadOnlyList<SuiteShortcutListener> listeners)
    {
        if (listeners is null) throw new ArgumentNullException(nameof(listeners));
        var collisions = new List<SuiteShortcutCollision>();
        foreach (var listener in listeners)
        {
            if (listener.Kind != SuiteShortcutListenerKind.PerFrameKeyboardPolling ||
                listener.Shortcut.MainKey == KeyCode.None)
                continue;
            AddNativeCollision(listener, listener.Shortcut.MainKey, isMainKey: true, collisions);
            foreach (var modifier in listener.Shortcut.Modifiers)
                AddNativeCollision(listener, modifier, isMainKey: false, collisions);
        }
        for (var leftIndex = 0; leftIndex < listeners.Count; leftIndex++)
        {
            var left = listeners[leftIndex];
            if (left.Kind != SuiteShortcutListenerKind.PerFrameKeyboardPolling ||
                left.Shortcut.MainKey == KeyCode.None)
                continue;
            for (var rightIndex = leftIndex + 1; rightIndex < listeners.Count; rightIndex++)
            {
                var right = listeners[rightIndex];
                if (right.Kind != SuiteShortcutListenerKind.PerFrameKeyboardPolling ||
                    !SameChord(left.Shortcut, right.Shortcut))
                    continue;
                collisions.Add(new SuiteShortcutCollision(
                    left.Id,
                    left.DisplayName,
                    left.Shortcut.MainKey,
                    right.DisplayName,
                    IsMainKey: true,
                    IsSuiteListener: true));
            }
        }
        return collisions;
    }

    public static bool SameChord(KeyboardShortcut left, KeyboardShortcut right) =>
        left.MainKey == right.MainKey &&
        left.Modifiers.OrderBy(key => key).SequenceEqual(right.Modifiers.OrderBy(key => key));

    private static void AddNativeCollision(
        SuiteShortcutListener listener,
        KeyCode key,
        bool isMainKey,
        ICollection<SuiteShortcutCollision> collisions)
    {
        if (!AuditedNativeDefaults.TryGetValue(key, out var nativeBinding)) return;
        collisions.Add(new SuiteShortcutCollision(
            listener.Id,
            listener.DisplayName,
            key,
            nativeBinding,
            isMainKey,
            IsSuiteListener: false));
    }
}
