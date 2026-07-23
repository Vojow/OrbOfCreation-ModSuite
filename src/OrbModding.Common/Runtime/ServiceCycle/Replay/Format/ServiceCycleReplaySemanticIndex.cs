using System.Collections.Generic;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

/// <summary>
/// Immutable lookup projection built once for one semantic document. Format joins use this boundary
/// so cycle and receipt validation never rescan the complete trace for each footer.
/// </summary>
internal sealed partial class ServiceCycleReplaySemanticIndex
{
    private readonly ServiceCycleTraceDocument _semantic;
    private readonly Dictionary<ServiceCycleTraceEventId, int> _eventIndices;
    private readonly Dictionary<ParentKindKey, int> _directChildCounts;
    private readonly Dictionary<CycleBatchKey, IndexedMatch> _receiptTerminals;
    private readonly Dictionary<CycleBatchActionKey, IndexedMatch> _actionTerminals;
    private readonly Dictionary<PublicationKey, IndexedMatch> _publications;
    private readonly int[] _entry;
    private readonly int[] _exit;
    private readonly long[] _emergencyTransitions;
    private readonly ServiceCycleReplayFormatWorkCounter? _work;

    private ServiceCycleReplaySemanticIndex(
        ServiceCycleTraceDocument semantic,
        Dictionary<ServiceCycleTraceEventId, int> eventIndices,
        Dictionary<ParentKindKey, int> directChildCounts,
        Dictionary<CycleBatchKey, IndexedMatch> receiptTerminals,
        Dictionary<CycleBatchActionKey, IndexedMatch> actionTerminals,
        Dictionary<PublicationKey, IndexedMatch> publications,
        int[] entry,
        int[] exit,
        long[] emergencyTransitions,
        ServiceCycleReplayFormatWorkCounter? work)
    {
        _semantic = semantic;
        _eventIndices = eventIndices;
        _directChildCounts = directChildCounts;
        _receiptTerminals = receiptTerminals;
        _actionTerminals = actionTerminals;
        _publications = publications;
        _entry = entry;
        _exit = exit;
        _emergencyTransitions = emergencyTransitions;
        _work = work;
    }

    internal int CountDirectChildren(ServiceCycleTraceEventId parent, ServiceCycleSemanticEventKind kind)
    {
        _work?.Add();
        return _directChildCounts.TryGetValue(new ParentKindKey(parent, kind), out var count) ? count : 0;
    }

    internal bool ParentIs(ServiceCycleSemanticEvent item, ServiceCycleSemanticEventKind expected)
    {
        _work?.Add();
        return item.Parent.IsValid && _eventIndices.TryGetValue(item.Parent, out var index) &&
            _semantic[index].Kind == expected;
    }

    internal bool TryGetParent(ServiceCycleSemanticEvent item, out ServiceCycleSemanticEvent parent)
    {
        _work?.Add();
        if (item.Parent.IsValid && _eventIndices.TryGetValue(item.Parent, out var index))
        {
            parent = _semantic[index];
            return true;
        }
        parent = default;
        return false;
    }

    internal int IndexOf(ServiceCycleTraceEventId eventId)
    {
        _work?.Add();
        return _eventIndices.TryGetValue(eventId, out var index) ? index : -1;
    }

    internal bool IsAncestor(ServiceCycleTraceEventId ancestor, ServiceCycleSemanticEvent descendant)
    {
        _work?.Add();
        if (!_eventIndices.TryGetValue(ancestor, out var ancestorIndex) ||
            !_eventIndices.TryGetValue(descendant.Id, out var descendantIndex)) return false;
        return _entry[ancestorIndex] <= _entry[descendantIndex] &&
            _exit[descendantIndex] <= _exit[ancestorIndex] && ancestorIndex != descendantIndex;
    }

    internal IndexedMatch FindReceiptTerminal(in ServiceCycleReplayArtifactReceipt receipt)
    {
        _work?.Add();
        return _receiptTerminals.TryGetValue(new CycleBatchKey(receipt.Cycle, receipt.Batch), out var match)
            ? match : default;
    }

    internal IndexedMatch FindActionTerminal(in ServiceCycleReplayArtifactReceipt receipt)
    {
        _work?.Add();
        return _actionTerminals.TryGetValue(
            new CycleBatchActionKey(receipt.Cycle, receipt.Batch, receipt.TerminalIndex), out var match)
            ? match : default;
    }

    internal IndexedMatch FindPublication(int service, ulong generation, bool configuration)
    {
        _work?.Add();
        return _publications.TryGetValue(new PublicationKey(checked((ulong)service), generation, configuration), out var match)
            ? match : default;
    }

    internal long EmergencyTransitionsThrough(int eventIndex)
    {
        _work?.Add();
        return (uint)eventIndex < (uint)_emergencyTransitions.Length ? _emergencyTransitions[eventIndex] : 0;
    }

}
