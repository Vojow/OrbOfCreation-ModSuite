using System;

namespace OrbModConfig;

internal static class ModSettingsLayout
{
    private const float MinimumSettingRowHeight = 86f;
    private const float SettingRowGap = 7f;
    private const float SettingDescriptionTopInset = 34f;
    private const float SettingDescriptionBottomInset = 6f;
    private const float SettingDescriptionHeightPadding = 2f;
    private const float MinimumMeasurableContentWidth = 320f;
    private const float FallbackDescriptionWidth = 600f;
    private const float DescriptionWidthChangeTolerance = 0.5f;

    public static float CalculateSettingRowHeight(float preferredDescriptionHeight)
    {
        if (!IsFiniteNonNegative(preferredDescriptionHeight)) preferredDescriptionHeight = 0f;
        return Math.Max(
            MinimumSettingRowHeight,
            SettingDescriptionTopInset +
            preferredDescriptionHeight +
            SettingDescriptionHeightPadding +
            SettingDescriptionBottomInset +
            SettingRowGap);
    }

    public static float ClampScrollOffset(float requestedOffset, float contentHeight, float viewportHeight)
    {
        if (!IsFiniteNonNegative(requestedOffset)) requestedOffset = 0f;
        if (!IsFiniteNonNegative(contentHeight)) contentHeight = 0f;
        if (!IsFiniteNonNegative(viewportHeight)) viewportHeight = 0f;
        var maximumOffset = Math.Max(0f, contentHeight - viewportHeight);
        return Math.Max(0f, Math.Min(requestedOffset, maximumOffset));
    }

    public static float CalculateVerticalNormalizedPosition(
        float scrollOffset,
        float contentHeight,
        float viewportHeight)
    {
        if (!IsFiniteNonNegative(contentHeight)) contentHeight = 0f;
        if (!IsFiniteNonNegative(viewportHeight)) viewportHeight = 0f;
        var maximumOffset = Math.Max(0f, contentHeight - viewportHeight);
        if (maximumOffset <= 0f) return 1f;
        return 1f - ClampScrollOffset(scrollOffset, contentHeight, viewportHeight) / maximumOffset;
    }

    public static float CalculateDescriptionWidth(float contentWidth)
    {
        if (!IsFiniteNonNegative(contentWidth) || contentWidth < MinimumMeasurableContentWidth)
            return FallbackDescriptionWidth;
        const float rowWidthFraction = 0.98f;
        const float descriptionWidthFraction = 0.55f - 0.018f;
        return contentWidth * rowWidthFraction * descriptionWidthFraction;
    }

    public static bool DescriptionWidthChanged(float previousWidth, float currentWidth)
    {
        if (!IsFiniteNonNegative(currentWidth) || currentWidth <= 0f) return false;
        return !IsFiniteNonNegative(previousWidth) ||
            previousWidth <= 0f ||
            Math.Abs(previousWidth - currentWidth) > DescriptionWidthChangeTolerance;
    }

    private static bool IsFiniteNonNegative(float value) =>
        !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;
}

internal readonly struct ModSettingsNavigationState
{
    public ModSettingsNavigationState(int sectionIndex, float scrollOffset)
    {
        SectionIndex = Math.Max(0, sectionIndex);
        ScrollOffset = float.IsNaN(scrollOffset) || float.IsInfinity(scrollOffset)
            ? 0f
            : Math.Max(0f, scrollOffset);
    }

    public int SectionIndex { get; }
    public float ScrollOffset { get; }

    public ModSettingsNavigationState ClampTo(int sectionCount) =>
        sectionCount <= 0
            ? new ModSettingsNavigationState(0, 0f)
            : new ModSettingsNavigationState(Math.Min(SectionIndex, sectionCount - 1), ScrollOffset);
}
