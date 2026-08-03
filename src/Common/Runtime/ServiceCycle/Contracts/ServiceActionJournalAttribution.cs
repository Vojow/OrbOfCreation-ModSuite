using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Contracts;

/// <summary>The compact, stable wire identity of an exact native action target.</summary>
public enum ServiceActionNativeTypeId : ushort
{
    NotApplicable = 0,
    StructureSO = 1,
    UpgradeSO = 2,
    PlotNodeActionSO = 3,
    SpellRecipeSO = 4,
    AlchemyRecipeSO = 5,
    ConsumableSO = 6,
    CraftingRecipeSO = 7,
    EquipmentSO = 8,
}

/// <summary>Whether list/view routing applies to an action target and, when it does, was resolved.</summary>
public enum ServiceActionRouteStatus : byte
{
    NotApplicable = 1,
    Resolved = 2,
    Missing = 3,
    Unreadable = 4,
    Contradictory = 5,
}

/// <summary>
/// The exact identity written beside one action outcome in the always-on decision journal.
/// </summary>
public readonly struct ServiceActionJournalAttribution
{
    public ServiceActionJournalAttribution(
        Guid candidateId,
        ServiceActionNativeTypeId nativeType,
        Guid listId,
        Guid viewId,
        ServiceActionRouteStatus routeStatus)
    {
        if (nativeType is < ServiceActionNativeTypeId.NotApplicable or > ServiceActionNativeTypeId.EquipmentSO)
            throw new ArgumentOutOfRangeException(nameof(nativeType));
        if (routeStatus is < ServiceActionRouteStatus.NotApplicable or > ServiceActionRouteStatus.Contradictory)
            throw new ArgumentOutOfRangeException(nameof(routeStatus));
        if ((nativeType == ServiceActionNativeTypeId.NotApplicable) != (candidateId == Guid.Empty))
            throw new ArgumentException(
                "A native candidate requires both its UUID and exact native type.", nameof(nativeType));
        if (routeStatus == ServiceActionRouteStatus.Resolved)
        {
            if (listId == Guid.Empty || viewId == Guid.Empty)
                throw new ArgumentException("A resolved route requires exact list and view UUIDs.");
        }
        else if (listId != Guid.Empty || viewId != Guid.Empty)
        {
            throw new ArgumentException("Only a resolved route can carry list or view UUIDs.");
        }
        if (candidateId == Guid.Empty && routeStatus is not (
                ServiceActionRouteStatus.NotApplicable or ServiceActionRouteStatus.Contradictory))
            throw new ArgumentException(
                "A non-native action cannot carry a native route state.", nameof(routeStatus));
        CandidateId = candidateId;
        NativeType = nativeType;
        ListId = listId;
        ViewId = viewId;
        RouteStatus = routeStatus;
    }

    public Guid CandidateId { get; }
    public ServiceActionNativeTypeId NativeType { get; }
    public Guid ListId { get; }
    public Guid ViewId { get; }
    public ServiceActionRouteStatus RouteStatus { get; }
    public bool IsValid =>
        NativeType is >= ServiceActionNativeTypeId.NotApplicable and <= ServiceActionNativeTypeId.EquipmentSO &&
        RouteStatus is >= ServiceActionRouteStatus.NotApplicable and <= ServiceActionRouteStatus.Contradictory &&
        (NativeType == ServiceActionNativeTypeId.NotApplicable) == (CandidateId == Guid.Empty) &&
        (RouteStatus == ServiceActionRouteStatus.Resolved
            ? ListId != Guid.Empty && ViewId != Guid.Empty
            : ListId == Guid.Empty && ViewId == Guid.Empty) &&
        (CandidateId != Guid.Empty || RouteStatus is
            ServiceActionRouteStatus.NotApplicable or ServiceActionRouteStatus.Contradictory);

    public static ServiceActionJournalAttribution Native(
        Guid candidateId,
        ServiceActionNativeTypeId nativeType) =>
        new(candidateId, nativeType, Guid.Empty, Guid.Empty, ServiceActionRouteStatus.NotApplicable);

    public static ServiceActionJournalAttribution Routed(
        Guid candidateId,
        ServiceActionNativeTypeId nativeType,
        Guid listId,
        Guid viewId) =>
        new(candidateId, nativeType, listId, viewId, ServiceActionRouteStatus.Resolved);

    public static ServiceActionJournalAttribution Publication =>
        new(Guid.Empty, ServiceActionNativeTypeId.NotApplicable, Guid.Empty, Guid.Empty,
            ServiceActionRouteStatus.NotApplicable);

    internal static ServiceActionJournalAttribution Failed =>
        new(Guid.Empty, ServiceActionNativeTypeId.NotApplicable, Guid.Empty, Guid.Empty,
            ServiceActionRouteStatus.Contradictory);
}
