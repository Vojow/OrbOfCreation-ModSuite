using System;
using System.Linq;
using OrbModding.Common;
using UnityEngine;
using Xunit;

namespace OrbModding.Tests;

public sealed class StatusControlGroupTests
{
    [Theory]
    [InlineData(0.0f, 0.0f, 50.0f, 4.0f)]
    [InlineData(100.0f, 0.5f, 50.0f, 79.0f)]
    [InlineData(100.0f, 1.0f, 50.0f, 54.0f)]
    public void TrayStartsInsideNativeRightSidebarLane(
        float nativeX,
        float pivotX,
        float width,
        float expectedX)
    {
        var position = StatusControlGroup.CalculateGroupPosition(
            new Vector2(nativeX, -35.0f),
            new Vector2(pivotX, 0.5f),
            width);
        Assert.Equal(expectedX, position.x);
        Assert.Equal(StatusControlGroup.TrayInset, position.y);
    }

    [Theory]
    [InlineData(0, 0.0f, 0.0f)]
    [InlineData(1, 34.0f, 34.0f)]
    [InlineData(2, 72.0f, 34.0f)]
    [InlineData(3, 72.0f, 72.0f)]
    [InlineData(5, 72.0f, 110.0f)]
    [InlineData(6, 72.0f, 110.0f)]
    [InlineData(8, 72.0f, 148.0f)]
    public void TrayUsesTwoCompactColumns(int count, float expectedWidth, float expectedHeight)
    {
        var size = StatusControlGroup.CalculateGroupSize(count);
        Assert.Equal(expectedWidth, size.x);
        Assert.Equal(expectedHeight, size.y);
    }

    [Fact]
    public void SixControlsFillThreeRowsInReadingOrder()
    {
        var expectedCenters = new[]
        {
            new Vector2(17.0f, 93.0f),
            new Vector2(55.0f, 93.0f),
            new Vector2(17.0f, 55.0f),
            new Vector2(55.0f, 55.0f),
            new Vector2(17.0f, 17.0f),
            new Vector2(55.0f, 17.0f),
        };
        for (var index = 0; index < expectedCenters.Length; index++)
            Assert.Equal(expectedCenters[index], StatusControlGroup.CalculateSlotCenter(index, 6));
    }

    [Fact]
    public void ReflowOrdersRegisteredControlsByVisiblePriorityAndScalesThem()
    {
        var parent = new GameObject("AttributeBar");
        var native = CreateNative(parent.transform);
        var group = StatusControlGroup.GetOrCreate(native);
        var mentor = AddControl(group, "OrbMentor.Toggle", StatusControlOrder.Mentor);
        var autoBuy = AddControl(group, "OrbAutomata.AutoBuyToggle", StatusControlOrder.AutoBuy);
        var ignoredDecoration = new GameObject("Decoration");
        ignoredDecoration.transform.SetParent(group, false);
        var autoCast = AddControl(group, "OrbAutomata.AutoCastToggle", StatusControlOrder.AutoCast);
        var inactive = AddControl(group, "OrbInactive.Toggle", 250);
        inactive.SetActive(false);
        var autoConcept = AddControl(group, "OrbAutomata.AutoConceptToggle", StatusControlOrder.AutoConcept);

        StatusControlGroup.Reflow(group, native);

        Assert.Equal(new Vector2(72.0f, 72.0f), ((RectTransform)group).sizeDelta);
        Assert.Equal(new Vector2(4.0f, 4.0f), ((RectTransform)group).anchoredPosition);
        Assert.Equal(new Vector2(17.0f, 55.0f), ((RectTransform)mentor.transform).anchoredPosition);
        Assert.Equal(new Vector2(55.0f, 55.0f), ((RectTransform)autoConcept.transform).anchoredPosition);
        Assert.Equal(new Vector2(17.0f, 17.0f), ((RectTransform)autoCast.transform).anchoredPosition);
        Assert.Equal(new Vector2(55.0f, 17.0f), ((RectTransform)autoBuy.transform).anchoredPosition);
        Assert.Equal(
            new Vector2(StatusControlGroup.ControlSize, StatusControlGroup.ControlSize),
            ((RectTransform)mentor.transform).sizeDelta);
        Assert.Equal(Vector2.zero, ((RectTransform)ignoredDecoration.transform).anchoredPosition);
        Assert.Equal(Vector2.zero, ((RectTransform)inactive.transform).anchoredPosition);
    }

    [Fact]
    public void SlotCalculationRejectsInvalidIndexes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StatusControlGroup.CalculateSlotCenter(0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => StatusControlGroup.CalculateSlotCenter(-1, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => StatusControlGroup.CalculateSlotCenter(3, 3));
    }

    [Fact]
    public void DeclaredControlOrdersAreUniqueAndPreserveVisibleSequence()
    {
        var orders = typeof(StatusControlOrder).GetFields()
            .Where(field => field.IsLiteral && field.FieldType == typeof(int))
            .Select(field => (int)field.GetRawConstantValue()!)
            .ToArray();
        Assert.Equal(orders.Length, orders.Distinct().Count());
        Assert.True(StatusControlOrder.Mentor > StatusControlOrder.AutoHarvest);
        Assert.True(StatusControlOrder.AutoHarvest > StatusControlOrder.AutoConcept);
        Assert.True(StatusControlOrder.AutoConcept > StatusControlOrder.AutoCast);
        Assert.True(StatusControlOrder.AutoCast > StatusControlOrder.AutoBuy);
        Assert.True(StatusControlOrder.AutoBuy > StatusControlOrder.EmergencyStop);
    }

    [Fact]
    public void EmergencyStopHasAProtectedGapInTheLastTrayCell()
    {
        var rightSidebar = new GameObject("RightSidebar");
        var attributeBar = new GameObject("AttributeBar");
        attributeBar.transform.SetParent(rightSidebar.transform, false);
        var native = CreateNative(attributeBar.transform);
        var group = StatusControlGroup.GetOrCreate(native);
        AddControl(group, "Mentor", StatusControlOrder.Mentor);
        AddControl(group, "Harvest", StatusControlOrder.AutoHarvest);
        AddControl(group, "Concept", StatusControlOrder.AutoConcept);
        AddControl(group, "Cast", StatusControlOrder.AutoCast);
        var autoBuy = AddControl(group, "Buy", StatusControlOrder.AutoBuy);
        var stop = AddControl(group, "Stop", StatusControlOrder.EmergencyStop);
        StatusControlGroup.RegisterControl(
            stop,
            StatusControlOrder.EmergencyStop,
            StatusControlGroup.StopSeparation);

        StatusControlGroup.Reflow(group, native);

        Assert.Equal(new Vector2(78.0f, 110.0f), ((RectTransform)group).sizeDelta);
        Assert.Equal(new Vector2(17.0f, 17.0f), ((RectTransform)autoBuy.transform).anchoredPosition);
        Assert.Equal(new Vector2(61.0f, 17.0f), ((RectTransform)stop.transform).anchoredPosition);
        Assert.Equal(
            StatusControlGroup.ColumnGap + StatusControlGroup.StopSeparation,
            ((RectTransform)stop.transform).anchoredPosition.x -
            ((RectTransform)autoBuy.transform).anchoredPosition.x -
            StatusControlGroup.ControlSize);
    }

    [Fact]
    public void NativeAnchorUsesTheAuditedHierarchyWithoutManagerState()
    {
        var rightSidebar = new GameObject("RightSidebar");
        var attributeBar = new GameObject("AttributeBar");
        attributeBar.transform.SetParent(rightSidebar.transform, false);
        var native = new GameObject("AutoBuyToggle");
        native.transform.SetParent(attributeBar.transform, false);
        Assert.True(StatusControlGroup.IsNativeAnchor(native.transform));
        native.name = "DifferentToggle";
        Assert.False(StatusControlGroup.IsNativeAnchor(native.transform));
        native.name = "AutoBuyToggle";
        attributeBar.transform.name = "DifferentBar";
        Assert.False(StatusControlGroup.IsNativeAnchor(native.transform));
    }

    private static RectTransform CreateNative(Transform parent)
    {
        var native = new GameObject("AutoBuyToggle");
        native.transform.SetParent(parent, false);
        var nativeRect = (RectTransform)native.transform;
        nativeRect.anchorMin = new Vector2(0.0f, 0.5f);
        nativeRect.anchorMax = new Vector2(0.0f, 0.5f);
        nativeRect.pivot = new Vector2(0.0f, 0.5f);
        nativeRect.anchoredPosition = new Vector2(0.0f, -35.0f);
        nativeRect.rect = new Rect(0.0f, 0.0f, 50.0f, 50.0f);
        return nativeRect;
    }

    private static GameObject AddControl(Transform group, string name, int order)
    {
        var control = new GameObject(name);
        control.transform.SetParent(group, false);
        StatusControlGroup.RegisterControl(control, order);
        return control;
    }
}
