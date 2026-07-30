using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal enum AutoItemsDecisionKind
{
    Disabled = 0,
    Idle = 1,
    NativeBusy = 2,
    WaitingForToxicityRecovery = 3,
    Relic = 4,
    Scroll = 5,
    TemporaryItem = 6,
    AwaitingTemporaryActivation = 7,
    TemporaryEffectActive = 8,
    TemporaryItemQuarantined = 9,
}

internal readonly struct AutoItemsDecisionMetrics
{
    internal AutoItemsDecisionMetrics(
        int captured,
        int rejectedProfiles,
        int temporaryItems,
        int eligibleRelics,
        int eligibleScrolls,
        int plannedActions,
        AutoItemsDecisionKind kind)
    {
        Captured = captured;
        RejectedProfiles = rejectedProfiles;
        TemporaryItems = temporaryItems;
        EligibleRelics = eligibleRelics;
        EligibleScrolls = eligibleScrolls;
        PlannedActions = plannedActions;
        Kind = kind;
    }

    internal int Captured { get; }
    internal int RejectedProfiles { get; }
    internal int TemporaryItems { get; }
    internal int EligibleRelics { get; }
    internal int EligibleScrolls { get; }
    internal int PlannedActions { get; }
    internal AutoItemsDecisionKind Kind { get; }
}

internal struct AutoItemsCycleState
{
    private AutoItemsCycleState(LifecycleGeneration lifecycle)
    {
        Lifecycle = lifecycle;
        Decision = default;
        RecoveryWaitActive = false;
        _allowlistConfiguration = default;
        _temporaryAllowlist = null;
        _quarantinedTemporaryItems = PublicationTable<Guid>.Empty;
        PendingReceiptAction = default;
        HasPendingReceipt = false;
        PendingTemporaryItem = Guid.Empty;
        TemporarySubmittedFromFrame = 0;
        TemporaryActivationSeen = false;
    }

    private ConfigGeneration _allowlistConfiguration;
    private PublicationTable<Guid>? _temporaryAllowlist;
    private PublicationTable<Guid> _quarantinedTemporaryItems;

    internal LifecycleGeneration Lifecycle { get; }
    internal AutoItemsDecisionMetrics Decision { get; private set; }
    internal bool RecoveryWaitActive { get; private set; }
    internal PublicationTable<Guid>? TemporaryAllowlist => _temporaryAllowlist;
    internal PublicationTable<Guid> QuarantinedTemporaryItems => _quarantinedTemporaryItems;
    internal AutoItemsCycleAction PendingReceiptAction { get; private set; }
    internal bool HasPendingReceipt { get; private set; }
    internal Guid PendingTemporaryItem { get; private set; }
    internal long TemporarySubmittedFromFrame { get; private set; }
    internal bool TemporaryActivationSeen { get; private set; }

    internal static AutoItemsCycleState Create(LifecycleGeneration lifecycle) => new(lifecycle);
    internal void RecordDecision(in AutoItemsDecisionMetrics decision) => Decision = decision;
    internal void BeginRecoveryWait() => RecoveryWaitActive = true;
    internal void EndRecoveryWait() => RecoveryWaitActive = false;
    internal void RecordPlannedTemporary(in AutoItemsCycleAction action)
    {
        PendingReceiptAction = action;
        HasPendingReceipt = true;
    }

    internal void ClearPendingReceipt()
    {
        PendingReceiptAction = default;
        HasPendingReceipt = false;
    }

    internal void RecordSubmittedTemporary(in AutoItemsCycleAction action)
    {
        if (IsTemporaryQuarantined(action.ItemId)) return;
        PendingTemporaryItem = action.ItemId;
        TemporarySubmittedFromFrame = action.CollectedAtFrame;
        TemporaryActivationSeen = false;
    }

    internal void MarkTemporaryActivationSeen() => TemporaryActivationSeen = true;

    internal void ClearPendingTemporary()
    {
        PendingTemporaryItem = Guid.Empty;
        TemporarySubmittedFromFrame = 0;
        TemporaryActivationSeen = false;
    }

    internal bool IsTemporaryQuarantined(Guid itemId) =>
        AutoItemsTemporaryItemAllowlist.Contains(_quarantinedTemporaryItems, itemId);

    internal void QuarantinePendingTemporary()
    {
        var itemId = PendingTemporaryItem;
        if (itemId == Guid.Empty || IsTemporaryQuarantined(itemId))
        {
            ClearPendingTemporary();
            return;
        }

        var previous = _quarantinedTemporaryItems.AsSpan();
        var rows = new Guid[previous.Length + 1];
        previous.CopyTo(rows);
        rows[previous.Length] = itemId;
        Array.Sort(rows);
        _quarantinedTemporaryItems = PublicationTable<Guid>.Create(rows, rows.Length);
        ClearPendingTemporary();
    }

    internal void ObserveConfiguration(
        ConfigGeneration generation,
        AutoItemsConfiguration configuration)
    {
        if (_allowlistConfiguration == generation) return;
        _temporaryAllowlist =
            configuration.UseFruits || configuration.UsePotions || configuration.UseThreads
                ? AutoItemsTemporaryItemAllowlist.ParsePublication(
                    configuration.TemporaryItemAllowlist)
                : null;
        _allowlistConfiguration = generation;
    }
}
