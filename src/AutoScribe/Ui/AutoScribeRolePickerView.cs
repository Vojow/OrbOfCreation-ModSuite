using System;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
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
        var selected = AutoScribeRoleSelection.ParseKnown(
            edit.StagedSerialized,
            Roles);
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
            () => Stage(edit, AutoScribeRoleSelection.NoneValue),
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
            var enabled = selected.Contains(role.Key);
            CreateTopButton(
                "Role." + role.Key.Value,
                parent,
                0.58f,
                0.98f,
                top,
                RoleHeight,
                $"{(enabled ? "[x]" : "[ ]")} {role.DisplayName}",
                () => Toggle(edit, role.Key),
                enabled);
            top += RoleStride;
        }
    }

    private void Toggle(ConfigEditValue edit, ScrollRoleKey key)
    {
        var selected = AutoScribeRoleSelection.ParseKnown(
            edit.StagedSerialized,
            Roles);
        if (!selected.Add(key)) selected.Remove(key);
        Stage(edit, AutoScribeRoleSelection.Serialize(selected, Roles));
    }

    private void Stage(ConfigEditValue edit, string value)
    {
        edit.Stage(value);
        _statusChanged(edit);
        _rebuildRequested();
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

    private static PublicationTable<AutoScribeRoleDescriptor> CreateRoles()
    {
        var catalog = new AutoScribeIdentityCatalog();
        if (!catalog.TryGetProfile(
                GameAssemblyAudit.WindowsV1052BaselineId,
                out var profile))
        {
            return PublicationTable<AutoScribeRoleDescriptor>.Empty;
        }
        var roles = new List<AutoScribeRoleDescriptor>();
        for (var index = 0; index < profile.Roles.Count; index++)
            if (profile.Roles[index].IsProducible) roles.Add(profile.Roles[index]);
        return PublicationTable<AutoScribeRoleDescriptor>.Create(
            roles.ToArray(),
            roles.Count);
    }

    private static PublicationTable<AutoScribeRoleDescriptor> Roles { get; } =
        CreateRoles();
}
