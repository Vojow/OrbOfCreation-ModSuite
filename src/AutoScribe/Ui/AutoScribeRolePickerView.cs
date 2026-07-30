using System;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OrbModConfig;

/// <summary>
/// Semantic Auto Scribe role editor. UUIDs stay behind the identity facade; configuration stores
/// only stable role keys, while an empty value means every producible audited role.
/// </summary>
internal sealed class AutoScribeRolePickerView
{
    private const float EditorTop = 76f;
    private const float RoleHeight = 40f;
    private const float RoleStride = 44f;
    private const string NoneValue = "none";

    private readonly TextMeshProUGUI _labelTemplate;
    private readonly Action _rebuildRequested;
    private readonly Action<ConfigEditValue?> _statusChanged;
    private bool _expanded;

    internal AutoScribeRolePickerView(
        TextMeshProUGUI labelTemplate,
        Action rebuildRequested,
        Action<ConfigEditValue?> statusChanged)
    {
        _labelTemplate = labelTemplate ??
            throw new ArgumentNullException(nameof(labelTemplate));
        _rebuildRequested = rebuildRequested ??
            throw new ArgumentNullException(nameof(rebuildRequested));
        _statusChanged = statusChanged ??
            throw new ArgumentNullException(nameof(statusChanged));
    }

    internal static bool AppliesTo(ConfigSettingDescriptor setting) =>
        string.Equals(setting.PluginGuid, PluginIds.SuiteGuid, StringComparison.Ordinal) &&
        string.Equals(setting.SourceSection, "AutoScribe", StringComparison.Ordinal) &&
        string.Equals(setting.Key, "Roles", StringComparison.Ordinal) &&
        setting.SettingType == typeof(string);

    internal float Measure(float minimumHeight) =>
        _expanded ? 88f + Roles.Count * RoleStride : minimumHeight;

    internal void Render(Transform parent, ConfigEditValue edit)
    {
        var selected = Parse(edit.StagedSerialized);
        CreateTopButton(
            "Roles",
            parent,
            0.58f,
            0.72f,
            12f,
            52f,
            $"Roles ({selected.Count}/{Roles.Count})",
            () =>
            {
                _expanded = !_expanded;
                _rebuildRequested();
            },
            _expanded);
        CreateTopButton(
            "All",
            parent,
            0.73f,
            0.81f,
            12f,
            52f,
            "All",
            () => Stage(edit, string.Empty),
            selected.Count == Roles.Count);
        CreateTopButton(
            "None",
            parent,
            0.82f,
            0.9f,
            12f,
            52f,
            "None",
            () => Stage(edit, NoneValue),
            selected.Count == 0);
        CreateTopButton(
            "Default",
            parent,
            0.91f,
            0.98f,
            12f,
            52f,
            "Default",
            () =>
            {
                edit.StageDefault();
                _statusChanged(edit);
                _rebuildRequested();
            });

        if (!_expanded) return;
        var top = EditorTop;
        for (var index = 0; index < Roles.Count; index++)
        {
            var role = Roles[index];
            var enabled = selected.Contains(role.Key.Value);
            CreateTopButton(
                "Role." + role.Key.Value,
                parent,
                0.58f,
                0.98f,
                top,
                RoleHeight,
                $"{(enabled ? "[x]" : "[ ]")} {role.DisplayName}",
                () => Toggle(edit, role.Key.Value),
                enabled);
            top += RoleStride;
        }
    }

    private void Toggle(ConfigEditValue edit, string key)
    {
        var selected = Parse(edit.StagedSerialized);
        if (!selected.Add(key)) selected.Remove(key);
        Stage(edit, Serialize(selected));
    }

    private void Stage(ConfigEditValue edit, string value)
    {
        edit.Stage(value);
        _statusChanged(edit);
        _rebuildRequested();
    }

    private static HashSet<string> Parse(string value)
    {
        var selected = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(value))
        {
            for (var index = 0; index < Roles.Count; index++)
                selected.Add(Roles[index].Key.Value);
            return selected;
        }
        if (string.Equals(value.Trim(), NoneValue, StringComparison.OrdinalIgnoreCase))
            return selected;
        foreach (var entry in value.Split(','))
        {
            var normalized = entry.Trim();
            if (IsKnownRole(normalized)) selected.Add(normalized);
        }
        return selected;
    }

    private static bool IsKnownRole(string key)
    {
        for (var index = 0; index < Roles.Count; index++)
            if (string.Equals(Roles[index].Key.Value, key, StringComparison.Ordinal))
                return true;
        return false;
    }

    private static string Serialize(HashSet<string> selected)
    {
        if (selected.Count == 0) return NoneValue;
        if (selected.Count == Roles.Count)
        {
            var all = true;
            for (var index = 0; index < Roles.Count; index++)
                all &= selected.Contains(Roles[index].Key.Value);
            if (all) return string.Empty;
        }
        var ordered = new List<string>(selected);
        ordered.Sort(StringComparer.Ordinal);
        return string.Join(",", ordered);
    }

    private Button CreateTopButton(
        string name,
        Transform parent,
        float left,
        float right,
        float top,
        float height,
        string label,
        UnityEngine.Events.UnityAction action,
        bool active = false)
    {
        var button = ModConfigUiFactory.CreateButton(
            name,
            parent,
            new Vector2(left, 1f),
            new Vector2(right, 1f),
            _labelTemplate,
            label,
            action,
            active);
        ModConfigUiFactory.SetTopAnchoredHeight(
            (RectTransform)button.transform,
            top,
            height);
        return button;
    }

    private static IReadOnlyList<AutoScribeRoleDescriptor> CreateRoles()
    {
        var catalog = new AutoScribeIdentityCatalog();
        if (!catalog.TryGetProfile(
                GameAssemblyAudit.WindowsV1052BaselineId,
                out var profile))
        {
            return Array.Empty<AutoScribeRoleDescriptor>();
        }
        var roles = new List<AutoScribeRoleDescriptor>();
        for (var index = 0; index < profile.Roles.Count; index++)
            if (profile.Roles[index].IsProducible) roles.Add(profile.Roles[index]);
        return roles;
    }

    private static IReadOnlyList<AutoScribeRoleDescriptor> Roles { get; } =
        CreateRoles();
}
