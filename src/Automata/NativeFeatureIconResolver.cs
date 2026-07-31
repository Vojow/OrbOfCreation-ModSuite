using System;
using System.Reflection;
using OrbModConfig;
using UnityEngine;

namespace OrbAutomata;

/// <summary>
/// One icon vocabulary for feature quick controls and the Mods rail. Every borrowed sprite comes
/// from a declared native accessor or an exact audited top-bar capture.
/// </summary>
internal static class NativeFeatureIconResolver
{
    internal static bool TryResolve(
        string pageLabel,
        NativeFeatureRailVisualPrimitives? capturedRail,
        out Sprite? icon,
        out string reason)
    {
        if (pageLabel.StartsWith("Runtime", StringComparison.Ordinal))
            return FromCaptured(capturedRail?.RuntimeIcon, "ScreenTime", out icon, out reason);
        return pageLabel switch
        {
            "General" => FromCaptured(
                capturedRail?.GeneralIcon, "ScreenMagic", out icon, out reason),
            "Auto Buy" => TryGetTooltipableIcon(
                "GetGlobalStructureType", out icon, out reason),
            "Auto Cast" => TryGetTooltipableIcon(
                "GetCastingSpeedAttr", out icon, out reason),
            "Auto Concept" => FromCaptured(
                capturedRail?.ConceptIcon, "ScreenScholar", out icon, out reason),
            "Auto Harvest" => TryGetTooltipableIcon(
                "GetHarvestSpeedAttr", out icon, out reason),
            "Mentor" => TryGetTooltipableIcon(
                "GetMasteryExpAttr", out icon, out reason),
            "Auto Items" => FromCaptured(
                capturedRail?.WorldIcon, "ScreenWorld", out icon, out reason),
            "Auto Scribe" => FromCaptured(
                capturedRail?.WorkshopIcon, "ScreenWorkshop", out icon, out reason),
            "Advanced" => FromCaptured(
                capturedRail?.AdvancedIcon, "ScreenAlchemy", out icon, out reason),
            _ => Unknown(pageLabel, out icon, out reason),
        };
    }

    private static bool FromCaptured(
        Sprite? captured,
        string auditedItemName,
        out Sprite? icon,
        out string reason)
    {
        if (captured is not null)
        {
            icon = captured;
            reason = string.Empty;
            return true;
        }
        return NativeViewAdapter.TryCaptureNamedTopBarIcon(auditedItemName, out icon, out reason);
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
            const BindingFlags flags =
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var accessor = globals.GetMethod(
                accessorName, flags, null, Type.EmptyTypes, null);
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
            if (icon is not null) return true;
            reason = $"GlobalVariables.{accessorName}().GetIcon() returned no sprite";
            return false;
        }
        catch (Exception ex)
        {
            reason = ex.GetBaseException().Message;
            return false;
        }
    }

    private static bool Unknown(
        string pageLabel,
        out Sprite? icon,
        out string reason)
    {
        icon = null;
        reason = $"unrecognized consolidated page '{pageLabel}'";
        return false;
    }
}
