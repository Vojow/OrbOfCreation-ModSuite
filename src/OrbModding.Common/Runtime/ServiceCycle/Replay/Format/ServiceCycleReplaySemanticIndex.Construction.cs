using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

internal sealed partial class ServiceCycleReplaySemanticIndex
{
    internal static ServiceCycleReplaySemanticIndex Build(
        ServiceCycleTraceDocument semantic,
        ServiceCycleReplayFormatWorkCounter? work = null)
    {
        if (semantic is null) throw new ArgumentNullException(nameof(semantic));
        var eventIndices = new Dictionary<ServiceCycleTraceEventId, int>(semantic.Count);
        for (var index = 0; index < semantic.Count; index++)
        {
            work?.Add();
            eventIndices.TryAdd(semantic[index].Id, index);
        }

        var directChildCounts = new Dictionary<ParentKindKey, int>();
        var receiptTerminals = new Dictionary<CycleBatchKey, IndexedMatch>();
        var actionTerminals = new Dictionary<CycleBatchActionKey, IndexedMatch>();
        var publications = new Dictionary<PublicationKey, IndexedMatch>();
        var parentIndices = new int[semantic.Count];
        var emergencyTransitions = new long[semantic.Count];
        Array.Fill(parentIndices, -1);
        long transitions = 0;
        for (var index = 0; index < semantic.Count; index++)
        {
            work?.Add();
            var item = semantic[index];
            if (item.Parent.IsValid && eventIndices.TryGetValue(item.Parent, out var parentIndex))
            {
                parentIndices[index] = parentIndex;
                Increment(directChildCounts, new ParentKindKey(item.Parent, item.Kind));
            }
            if (item.Kind is ServiceCycleSemanticEventKind.EmergencyEntered or
                ServiceCycleSemanticEventKind.EmergencyCleared)
                transitions++;
            emergencyTransitions[index] = transitions;

            if (ServiceCycleReplaySemanticMatch.HasFullCycleIdentity(item))
            {
                var cycle = ServiceCycleReplaySemanticMatch.KeyFrom(item);
                if (item.Kind is ServiceCycleSemanticEventKind.BatchCompleted or
                    ServiceCycleSemanticEventKind.BatchAborted or
                    ServiceCycleSemanticEventKind.BatchOrphaned)
                    AddMatch(receiptTerminals, new CycleBatchKey(cycle, item.Payload.Batch), index);
                if (item.Kind is ServiceCycleSemanticEventKind.ActionRejected or
                    ServiceCycleSemanticEventKind.ActionFaulted)
                    AddMatch(actionTerminals,
                        new CycleBatchActionKey(cycle, item.Payload.Batch, item.Payload.ActionIndex), index);
            }

            if (item.Kind == ServiceCycleSemanticEventKind.ConfigurationPublished)
                AddMatch(publications,
                    new PublicationKey(item.Payload.Service, item.Payload.Configuration, true), index);
            else if (item.Kind == ServiceCycleSemanticEventKind.StrategyPublished)
                AddMatch(publications,
                    new PublicationKey(item.Payload.Service, item.Payload.Strategy, false), index);
        }

        BuildAncestry(parentIndices, out var entry, out var exit, work);
        return new ServiceCycleReplaySemanticIndex(
            semantic, eventIndices, directChildCounts, receiptTerminals, actionTerminals,
            publications, entry, exit, emergencyTransitions, work);
    }

    private static void BuildAncestry(
        int[] parents,
        out int[] entry,
        out int[] exit,
        ServiceCycleReplayFormatWorkCounter? work)
    {
        var firstChild = new int[parents.Length];
        var nextSibling = new int[parents.Length];
        Array.Fill(firstChild, -1);
        Array.Fill(nextSibling, -1);
        for (var index = parents.Length - 1; index >= 0; index--)
        {
            work?.Add();
            var parent = parents[index];
            if (parent < 0) continue;
            nextSibling[index] = firstChild[parent];
            firstChild[parent] = index;
        }
        entry = new int[parents.Length];
        exit = new int[parents.Length];
        var stack = new Stack<int>(Math.Max(4, parents.Length));
        var order = 0;
        for (var root = parents.Length - 1; root >= 0; root--)
        {
            if (parents[root] >= 0) continue;
            stack.Push(root);
            while (stack.Count != 0)
            {
                work?.Add();
                var value = stack.Pop();
                if (value < 0)
                {
                    var completed = ~value;
                    exit[completed] = order++;
                    continue;
                }
                entry[value] = order++;
                stack.Push(~value);
                for (var child = firstChild[value]; child >= 0; child = nextSibling[child])
                    stack.Push(child);
            }
        }
    }

    private static void Increment(Dictionary<ParentKindKey, int> values, ParentKindKey key) =>
        values[key] = values.TryGetValue(key, out var count) ? checked(count + 1) : 1;

    private static void AddMatch<TKey>(Dictionary<TKey, IndexedMatch> values, TKey key, int index)
        where TKey : notnull =>
        values[key] = values.TryGetValue(key, out var match) ? match.AddDuplicate() : new IndexedMatch(index, 1);
}
