using System;
using System.Reflection;
using OrbModConfig;
using UnityEngine;

namespace OrbAutomata;

internal static class NativeFeatureIconResolver
{
    internal static bool TryGetAutoBuyIcon(out Sprite? icon, out string reason) =>
        TryGetTooltipableIcon("GetGlobalStructureType", out icon, out reason);

    internal static bool TryGetConceptIcon(out Sprite? icon, out string reason)
    {
        icon = null;
        if (!NativeViewAdapter.TryCaptureFeatureRailVisuals(out var rail, out reason))
            return false;
        icon = NativeViewAdapter.ReadFeatureRailIcon(rail!);
        if (icon is not null)
        {
            reason = string.Empty;
            return true;
        }
        reason = "audited feature-rail prototype has no viewImage sprite";
        return false;
    }

    internal static bool TryGetHarvestIcon(out Sprite? icon, out string reason) =>
        TryGetTooltipableIcon("GetHarvestSpeedAttr", out icon, out reason);

    internal static bool TryGetMentorIcon(out Sprite? icon, out string reason) =>
        TryGetTooltipableIcon("GetMasteryExpAttr", out icon, out reason);

    internal static bool TryGetItemsIcon(out Sprite? icon, out string reason) =>
        TryGetFeatureRailIcon(useAdvancedIcon: true, out icon, out reason);

    internal static bool TryGetScribeIcon(out Sprite? icon, out string reason) =>
        TryGetFeatureRailIcon(useAdvancedIcon: false, out icon, out reason);

    private static bool TryGetFeatureRailIcon(
        bool useAdvancedIcon,
        out Sprite? icon,
        out string reason)
    {
        icon = null;
        if (!NativeViewAdapter.TryCaptureFeatureRailVisuals(out var rail, out reason))
            return false;
        icon = useAdvancedIcon ? rail!.AdvancedIcon : rail!.ConceptIcon;
        if (icon is not null)
        {
            reason = string.Empty;
            return true;
        }
        reason = useAdvancedIcon
            ? "audited Alchemy top-bar sprite is unavailable"
            : "audited Scholar top-bar sprite is unavailable";
        return false;
    }

    private static bool TryGetTooltipableIcon(
        string accessorName,
        out Sprite? icon,
        out string reason)
    {
        icon = null;
        reason = string.Empty;
        try
        {
            var globals = Type.GetType("GlobalVariables, Assembly-CSharp", false);
            var tooltipable = Type.GetType("TooltipableObject, Assembly-CSharp", false);
            if (globals is null || tooltipable is null)
            {
                reason = "native GlobalVariables or TooltipableObject type is unavailable";
                return false;
            }
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var accessor = globals.GetMethod(accessorName, flags, null, Type.EmptyTypes, null);
            if (accessor is null || accessor.GetParameters().Length != 0)
            {
                reason = $"audited GlobalVariables.{accessorName}() accessor is unavailable";
                return false;
            }
            var attribute = accessor.Invoke(null, null);
            if (attribute is null || !tooltipable.IsInstanceOfType(attribute))
            {
                reason = $"GlobalVariables.{accessorName}() returned no TooltipableObject";
                return false;
            }
            var getIcon = tooltipable.GetMethod(
                "GetIcon",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);
            if (getIcon?.ReturnType != typeof(Sprite))
            {
                reason = "audited TooltipableObject.GetIcon() accessor is unavailable";
                return false;
            }
            icon = getIcon.Invoke(attribute, null) as Sprite;
            if (icon is not null)
            {
                reason = string.Empty;
                return true;
            }
            reason = $"GlobalVariables.{accessorName}().GetIcon() returned no sprite";
            return false;
        }
        catch (Exception ex)
        {
            reason = ex.GetBaseException().Message;
            return false;
        }
    }
}
