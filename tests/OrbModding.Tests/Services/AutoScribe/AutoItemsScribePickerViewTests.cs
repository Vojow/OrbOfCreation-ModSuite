using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using OrbAutomata;
using OrbModConfig;
using OrbModding.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Xunit;

namespace OrbModding.Tests.Services.AutoScribe;

public sealed class AutoItemsScribePickerViewTests
{
    public AutoItemsScribePickerViewTests()
    {
        var prototype = new GameObject("prototype").AddComponent<MonoBehaviour>();
        ModConfigUiFactory.UseNativeVisuals(new NativeFeatureRailVisualPrimitives(
            prototype,
            new Sprite(),
            new Sprite(),
            new Sprite(),
            new Sprite(),
            new Sprite(),
            new Sprite()));
    }

    [Fact]
    public void RolePickerExpandsStagesSemanticRolesAndRestoresDefault()
    {
        var edit = Edit("AutoScribe", "Roles", "scribe.power");
        var rebuilds = 0;
        ConfigEditValue? changed = null;
        var view = new AutoScribeRolePickerView(
            LabelTemplate(),
            () => rebuilds++,
            value => changed = value);

        Assert.True(AutoScribeRolePickerView.AppliesTo(edit.Setting));
        Assert.Equal(64f, view.Measure(64f));

        var collapsed = Parent();
        view.Render(collapsed, edit);
        Assert.Equal(4, collapsed.childCount);
        Assert.Contains("Roles (1/", Label(Child(collapsed, "Roles")));

        Click(collapsed, "Roles");
        Assert.Equal(1, rebuilds);
        Assert.True(view.Measure(64f) > 64f);

        var expanded = Parent();
        view.Render(expanded, edit);
        Assert.True(expanded.childCount > 4);
        Click(expanded, "Role.scribe.advancement");

        Assert.Contains("scribe.advancement", edit.StagedSerialized);
        Assert.Contains("scribe.power", edit.StagedSerialized);
        Assert.Same(edit, changed);

        Click(expanded, "None");
        Assert.Equal("none", edit.StagedSerialized);
        Click(expanded, "All");
        Assert.Equal(string.Empty, edit.StagedSerialized);
        edit.Stage("none");
        Click(expanded, "Default");
        Assert.Equal("scribe.power", edit.StagedSerialized);
        Assert.True(rebuilds >= 5);
    }

    [Fact]
    public void RolePickerFiltersUnknownKeysAndRejectsOtherSettings()
    {
        var edit = Edit(
            "AutoScribe",
            "Roles",
            "unknown,scribe.power,scribe.power");
        var view = new AutoScribeRolePickerView(
            LabelTemplate(),
            () => { },
            _ => { });

        var parent = Parent();
        view.Render(parent, edit);

        Assert.Contains("Roles (1/", Label(Child(parent, "Roles")));
        Assert.False(AutoScribeRolePickerView.AppliesTo(
            Edit("AutoItems", "Roles", string.Empty).Setting));
        Assert.Throws<ArgumentNullException>(() =>
            new AutoScribeRolePickerView(null!, () => { }, _ => { }));
        Assert.Throws<ArgumentNullException>(() =>
            new AutoScribeRolePickerView(LabelTemplate(), null!, _ => { }));
        Assert.Throws<ArgumentNullException>(() =>
            new AutoScribeRolePickerView(LabelTemplate(), () => { }, null!));
    }

    [Fact]
    public void TemporaryItemPickerRendersItemsUnavailableSelectionsAndRawEditing()
    {
        var fruit = Guid.Parse("00000000-0000-0000-0000-000000000101");
        var potion = Guid.Parse("00000000-0000-0000-0000-000000000102");
        var unavailable = Guid.Parse("00000000-0000-0000-0000-000000000103");
        var edit = Edit(
            "AutoItems",
            "TemporaryItemAllowlist",
            $"{fruit:D},{unavailable:D}");
        var rebuilds = 0;
        ConfigEditValue? changed = null;
        var view = new AutoItemsTemporaryItemPickerView(
            LabelTemplate(),
            () => rebuilds++,
            value => changed = value);
        var catalog = AutoItemsTemporaryItemCatalogSnapshot.Available(
            new[]
            {
                new AutoItemsTemporaryItemOption(
                    fruit,
                    AutoItemsConsumableFamily.Fruit,
                    "Bright Fruit",
                    ownedQuantity: 2,
                    durationSeconds: 15d,
                    toxicityCost: "5"),
                new AutoItemsTemporaryItemOption(
                    potion,
                    AutoItemsConsumableFamily.Potion,
                    "Clear Potion",
                    ownedQuantity: 0,
                    durationSeconds: double.NaN,
                    toxicityCost: string.Empty),
            });

        Assert.True(AutoItemsTemporaryItemPickerView.AppliesTo(edit.Setting));
        Assert.Null(view.CaptureCatalog());
        Assert.Equal(64f, view.Measure(edit, catalog, 64f));

        var closed = Parent();
        view.Render(closed, edit, catalog);
        Assert.Equal(4, closed.childCount);
        Click(closed, "Items");

        Assert.Equal(220f, view.Measure(edit, catalog, 64f));
        var items = Parent();
        view.Render(items, edit, catalog);
        Assert.Contains("owned 2", Label(Child(items, "Item." + fruit.ToString("N"))));
        Assert.Contains("15s", Label(Child(items, "Item." + fruit.ToString("N"))));
        Assert.Contains(
            "Unavailable item",
            Label(Child(items, "Unavailable." + unavailable.ToString("N"))));

        Click(items, "Item." + fruit.ToString("N"));
        Assert.DoesNotContain(fruit.ToString("D"), edit.StagedSerialized);
        Assert.Same(edit, changed);

        Click(items, "Unavailable." + unavailable.ToString("N"));
        Assert.Equal(string.Empty, edit.StagedSerialized);

        Click(items, "Raw");
        Assert.Equal(140f, view.Measure(edit, catalog, 64f));
        var raw = Parent();
        view.Render(raw, edit, catalog);
        var input = Child(raw, "RawInput").gameObject.GetComponent<TMP_InputField>();
        Assert.NotNull(input);
        input!.onValueChanged.Invoke(potion.ToString("D"));
        Assert.Equal(potion.ToString("D"), edit.StagedSerialized);
        input.onEndEdit.Invoke(edit.StagedSerialized);
        Assert.True(rebuilds >= 4);
    }

    [Fact]
    public void TemporaryItemPickerExplainsUnavailableCatalogAndSupportsDefault()
    {
        var edit = Edit(
            "AutoItems",
            "TemporaryItemAllowlist",
            string.Empty);
        var view = new AutoItemsTemporaryItemPickerView(
            LabelTemplate(),
            () => { },
            _ => { });
        var closed = Parent();
        view.Render(closed, edit, null);
        Click(closed, "Items");

        var items = Parent();
        view.Render(
            items,
            edit,
            AutoItemsTemporaryItemCatalogSnapshot.Unavailable("catalog unavailable"));

        Assert.Equal(148f, view.Measure(
            edit,
            AutoItemsTemporaryItemCatalogSnapshot.Unavailable("catalog unavailable"),
            64f));
        Assert.Equal(
            "catalog unavailable",
            Child(items, "Unavailable").gameObject.GetComponent<TextMeshProUGUI>()!.text);
        Click(items, "Default");
        Assert.Equal(string.Empty, edit.StagedSerialized);
        Assert.False(AutoItemsTemporaryItemPickerView.AppliesTo(
            Edit("AutoScribe", "TemporaryItemAllowlist", string.Empty).Setting));
        Assert.Throws<ArgumentNullException>(() =>
            new AutoItemsTemporaryItemPickerView(null!, () => { }, _ => { }));
        Assert.Throws<ArgumentNullException>(() =>
            new AutoItemsTemporaryItemPickerView(LabelTemplate(), null!, _ => { }));
        Assert.Throws<ArgumentNullException>(() =>
            new AutoItemsTemporaryItemPickerView(LabelTemplate(), () => { }, null!));
    }

    private static ConfigEditValue Edit(
        string section,
        string key,
        string defaultValue)
    {
        var entry = new ConfigFile().Bind(section, key, defaultValue, "test");
        return new ConfigEditValue(
            new ConfigSettingDescriptor(PluginIds.SuiteGuid, entry));
    }

    private static TextMeshProUGUI LabelTemplate() =>
        new GameObject("label", typeof(TextMeshProUGUI))
            .GetComponent<TextMeshProUGUI>()!;

    private static Transform Parent() => new GameObject("parent").transform;

    private static Transform Child(Transform parent, string name) =>
        Enumerable.Range(0, parent.childCount)
            .Select(parent.GetChild)
            .Single(child => child.gameObject.name == name);

    private static void Click(Transform parent, string name) =>
        Child(parent, name).gameObject.GetComponent<Button>()!.onClick.Invoke();

    private static string Label(Transform button) =>
        Child(button, "Label").gameObject.GetComponent<TextMeshProUGUI>()!.text;
}
