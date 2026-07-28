using System;
using System.Linq;
using OrbModding.Common;
using UnityEngine;
using Xunit;

namespace OrbModding.Tests;

public sealed class StatusControlGroupTests
{
    [Theory]
    [InlineData(0.0f, 0.0f, 50.0f, -12.0f)]
    [InlineData(100.0f, 0.5f, 50.0f, 63.0f)]
    [InlineData(100.0f, 1.0f, 50.0f, 38.0f)]
    public void GroupEndsOneGapBeforeNativeLeftEdge(float nativeX, float pivotX, float width, float expectedX)
    {
        var nativePosition = new Vector2(nativeX, 20.0f);
        var nativePivot = new Vector2(pivotX, 0.5f);
        var group = StatusControlGroup.CalculateGroupPosition(nativePosition, nativePivot, width, 50.0f);
        Assert.Equal(expectedX, group.x);
        Assert.Equal(20.0f, group.y);
    }

    [Fact]
    public void ModControlsPreserveNativeOutwardOrderWithUniformGaps()
    {
        const float width = 50.0f;
        const int count = 3;
        var expectedCentersFromNative = new[] { 149.0f, 87.0f, 25.0f };
        for (var slotFromNative = 0; slotFromNative < expectedCentersFromNative.Length; slotFromNative++)
            Assert.Equal(expectedCentersFromNative[slotFromNative], StatusControlGroup.CalculateSlotCenterX(width, count, slotFromNative));
        Assert.Equal(174.0f, StatusControlGroup.CalculateGroupWidth(width, count));
        Assert.Equal(12.0f, expectedCentersFromNative[0] - expectedCentersFromNative[1] - width);
        Assert.Equal(12.0f, expectedCentersFromNative[1] - expectedCentersFromNative[2] - width);
    }

    [Theory]
    [InlineData(1, 50.0f, 25.0f)]
    [InlineData(2, 112.0f, 87.0f)]
    [InlineData(3, 174.0f, 149.0f)]
    [InlineData(4, 236.0f, 211.0f)]
    [InlineData(8, 484.0f, 459.0f)]
    public void ClosestPresentControlRemainsBesideNative(int count, float expectedGroupWidth, float expectedCenter)
    {
        const float width = 50.0f;
        Assert.Equal(expectedGroupWidth, StatusControlGroup.CalculateGroupWidth(width, count));
        Assert.Equal(expectedCenter, StatusControlGroup.CalculateSlotCenterX(width, count, 0));
        Assert.Equal(25.0f, StatusControlGroup.CalculateSlotCenterX(width, count, count - 1));
    }

    [Fact]
    public void ReflowOrdersAnyNumberOfRegisteredControlsIndependentlyOfCreationOrder()
    {
        var parent = new GameObject("AttributeBar");
        var native = new GameObject("AutoBuyToggle");
        native.transform.SetParent(parent.transform, false);
        var nativeRect = (RectTransform)native.transform;
        nativeRect.anchorMin = new Vector2(0.0f, 0.5f);
        nativeRect.anchorMax = new Vector2(0.0f, 0.5f);
        nativeRect.pivot = new Vector2(0.0f, 0.5f);
        nativeRect.anchoredPosition = new Vector2(0.0f, -35.0f);
        nativeRect.rect = new Rect(0.0f, 0.0f, 50.0f, 50.0f);

        var group = StatusControlGroup.GetOrCreate(nativeRect);
        var mentor = AddControl(group, "OrbMentor.Toggle", 300);
        var future = AddControl(group, "OrbFuture.Toggle", 400);
        var autoBuy = AddControl(group, "OrbAutomata.AutoBuyToggle", 100);
        var ignoredDecoration = new GameObject("Decoration");
        ignoredDecoration.transform.SetParent(group, false);
        var autoCast = AddControl(group, "OrbAutomata.AutoCastToggle", 200);
        var inactive = AddControl(group, "OrbInactive.Toggle", 250);
        inactive.SetActive(false);
        var autoConcept = AddControl(group, "OrbAutomata.AutoConceptToggle", 300);

        StatusControlGroup.Reflow(group, nativeRect);

        Assert.Equal(298.0f, ((RectTransform)group).sizeDelta.x);
        Assert.Equal(-12.0f, ((RectTransform)group).anchoredPosition.x);
        Assert.Equal(273.0f, ((RectTransform)autoBuy.transform).anchoredPosition.x);
        Assert.Equal(211.0f, ((RectTransform)autoCast.transform).anchoredPosition.x);
        Assert.Equal(149.0f, ((RectTransform)autoConcept.transform).anchoredPosition.x);
        Assert.Equal(87.0f, ((RectTransform)mentor.transform).anchoredPosition.x);
        Assert.Equal(25.0f, ((RectTransform)future.transform).anchoredPosition.x);
        Assert.Equal(0.0f, ((RectTransform)ignoredDecoration.transform).anchoredPosition.x);
        Assert.Equal(0.0f, ((RectTransform)inactive.transform).anchoredPosition.x);
    }

    [Fact]
    public void SlotCalculationRejectsInvalidIndexes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StatusControlGroup.CalculateSlotCenterX(50.0f, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => StatusControlGroup.CalculateSlotCenterX(50.0f, 3, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => StatusControlGroup.CalculateSlotCenterX(50.0f, 3, 3));
    }

    [Fact]
    public void DeclaredControlOrdersAreUniqueAndPreserveCurrentSequence()
    {
        var orders = typeof(StatusControlOrder).GetFields()
            .Where(field => field.IsLiteral && field.FieldType == typeof(int))
            .Select(field => (int)field.GetRawConstantValue()!)
            .ToArray();
        Assert.Equal(orders.Length, orders.Distinct().Count());
        Assert.True(StatusControlOrder.EmergencyStop < StatusControlOrder.AutoBuy);
        Assert.True(StatusControlOrder.AutoBuy < StatusControlOrder.AutoCast);
        Assert.True(StatusControlOrder.AutoCast < StatusControlOrder.AutoConcept);
        Assert.True(StatusControlOrder.AutoConcept < StatusControlOrder.Mentor);
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

    private static GameObject AddControl(Transform group, string name, int order)
    {
        var control = new GameObject(name);
        control.transform.SetParent(group, false);
        StatusControlGroup.RegisterControl(control, order);
        return control;
    }
}
