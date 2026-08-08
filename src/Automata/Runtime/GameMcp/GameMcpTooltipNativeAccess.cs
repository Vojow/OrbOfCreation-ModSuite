#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections;
using OrbModding.Common.Runtime.World;
using UnityEngine;

namespace OrbAutomata.GameMcp;

/// <summary>
/// Process-lifetime native layout binding for the debug tooltip reader. It caches only a compiled
/// field accessor, never a Unity object; live tooltip objects are still resolved per command.
/// </summary>
internal sealed class GameMcpTooltipNativeAccess
{
    private readonly Func<object, IList?> _subTooltips;
    private readonly int _mainThreadId;

    private GameMcpTooltipNativeAccess(Func<object, IList?> subTooltips)
    {
        _subTooltips = subTooltips;
        _mainThreadId = Environment.CurrentManagedThreadId;
    }

    internal static bool TryCreate(
        Type? hoverTooltipType,
        out GameMcpTooltipNativeAccess access,
        out string reason)
    {
        access = null!;
        if (hoverTooltipType is null)
        {
            reason = "HoverTooltip type was unavailable during MCP startup binding";
            return false;
        }
        var elementType = NativeAccessorBinder.CollectionElementType(
            hoverTooltipType,
            "subTooltips");
        var read = NativeAccessorBinder.CollectionField(hoverTooltipType, "subTooltips");
        if (elementType != typeof(ITooltipable) || read is null)
        {
            reason = "HoverTooltip.subTooltips was not the exact audited List<ITooltipable> field";
            return false;
        }

        access = new GameMcpTooltipNativeAccess(read);
        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Whether the player can actually hover this element.
    /// </summary>
    /// <remarks>
    /// Closing a modal never deactivates anything: <c>UIModal.SetElementVisibility(false)</c> drops
    /// the canvas group's alpha, interactivity, and raycasts, and that is the whole of it. Every
    /// modal the session has ever opened therefore stays active in the hierarchy forever, so a
    /// catalog filtered on Unity liveness alone answers with panels the player closed an hour ago.
    /// The game's own <c>IsOpen</c> is what separates the panel on screen from the ones behind it.
    /// </remarks>
    internal static bool OnScreen(Component element)
    {
        if (element is null) return false;
        for (var node = element.transform; node is not null; node = node.parent)
        {
            if (node.GetComponent<UIModal>() is { } modal && !modal.IsOpen()) return false;
        }
        return true;
    }

    internal bool TryReadSubTooltips(
        object hoverTooltip,
        out ITooltipable[] subTooltips,
        out string reason)
    {
        if (Environment.CurrentManagedThreadId != _mainThreadId)
        {
            subTooltips = Array.Empty<ITooltipable>();
            reason = "tooltip native access was rejected off the Unity startup thread";
            return false;
        }
        if (hoverTooltip is null)
        {
            subTooltips = Array.Empty<ITooltipable>();
            reason = "the live HoverTooltip reference was null";
            return false;
        }

        IList? values;
        try
        {
            values = _subTooltips(hoverTooltip);
        }
        catch (Exception exception)
        {
            subTooltips = Array.Empty<ITooltipable>();
            reason = "reading bound HoverTooltip.subTooltips failed: " +
                exception.GetBaseException().Message;
            return false;
        }
        if (values is null || values.Count == 0)
        {
            subTooltips = Array.Empty<ITooltipable>();
            reason = string.Empty;
            return true;
        }

        var result = new ITooltipable[values.Count];
        for (var index = 0; index < values.Count; index++)
        {
            if (values[index] is not ITooltipable item)
            {
                subTooltips = Array.Empty<ITooltipable>();
                reason = "bound HoverTooltip.subTooltips contained a non-ITooltipable entry";
                return false;
            }
            result[index] = item;
        }
        subTooltips = result;
        reason = string.Empty;
        return true;
    }
}
#endif
