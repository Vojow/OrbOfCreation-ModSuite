using OrbAutomata;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using CycleKey = OrbModding.Common.Runtime.ServiceCycle.Replay.Recording.ServiceCycleReplayCycleKey;

namespace OrbModding.ServiceCycleTrace.Profiles;

internal sealed class AutoHarvestTraceProfile : IServiceCycleTraceFeatureProfile
{
    private readonly int _traceServiceKey;
    private readonly IReadOnlyDictionary<CycleKey, string> _actions;

    private AutoHarvestTraceProfile(
        int traceServiceKey,
        IReadOnlyDictionary<CycleKey, string> actions)
    {
        _traceServiceKey = traceServiceKey;
        _actions = actions;
    }

    public string DisplayName => "Auto Harvest";

    internal static AutoHarvestTraceProfile BindAssertedFeature(
        ServiceCycleReplayArtifactDocument artifact)
    {
        var inputCodec = new AutoHarvestCycleInputCodec();
        var stateCodec = new AutoHarvestStateCodec();
        var actionCodec = new AutoHarvestActionCodec();
        var matchingService = 0;

        for (var index = 0; index < artifact.CodecCount; index++)
        {
            var entry = artifact.GetCodec(index);
            if (entry.Role != ServiceCycleReplayCodecRole.CycleInput ||
                entry.Descriptor != inputCodec.Descriptor)
                continue;
            if (artifact.GetCodecDescriptor(entry.TraceServiceKey, ServiceCycleReplayCodecRole.State) !=
                    stateCodec.Descriptor ||
                artifact.GetCodecDescriptor(entry.TraceServiceKey, ServiceCycleReplayCodecRole.Action) !=
                    actionCodec.Descriptor)
                continue;
            if (matchingService != 0)
                throw new InvalidDataException(
                    "The artifact has more than one service compatible with the Auto Harvest profile.");
            matchingService = entry.TraceServiceKey;
        }

        if (matchingService == 0)
            throw new InvalidDataException(
                "The artifact has no service compatible with the Auto Harvest profile.");

        var actions = new Dictionary<CycleKey, string>();
        try
        {
            for (var index = 0; index < artifact.CycleCount; index++)
            {
                var cycle = artifact.GetCycle(index);
                if (cycle.Key.TraceServiceKey != matchingService) continue;
                actions.Add(cycle.Key, DecodeCycle(cycle, inputCodec, stateCodec, actionCodec));
            }
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                "The artifact contains an invalid Auto Harvest replay record.",
                exception);
        }
        if (actions.Count == 0)
            throw new InvalidDataException(
                "The artifact has no service compatible with the Auto Harvest profile.");
        return new AutoHarvestTraceProfile(matchingService, actions);
    }

    public bool Includes(ServiceCycleReplayArtifactCycle cycle) =>
        cycle.Key.TraceServiceKey == _traceServiceKey;

    public string DescribeAction(ServiceCycleReplayArtifactCycle cycle) =>
        _actions.TryGetValue(cycle.Key, out var action)
            ? action
            : throw new InvalidOperationException("The cycle does not belong to this trace profile.");

    private static string DecodeCycle(
        ServiceCycleReplayArtifactCycle cycle,
        AutoHarvestCycleInputCodec inputCodec,
        AutoHarvestStateCodec stateCodec,
        AutoHarvestActionCodec actionCodec)
    {
        var expectedActions = cycle.Footer.ExpectedActionCount;
        if (expectedActions is < 0 or > 1 || cycle.IsComplete &&
            cycle.RecordCount != expectedActions + 3)
        {
            throw InvalidRecordSequence();
        }
        var inputCount = 0;
        var previousStateCount = 0;
        var nextStateCount = 0;
        var actionCount = 0;
        var actionName = cycle.Footer.ExpectedActionCount == 0 ? "No action" : "Unavailable";
        var previousRank = -1;
        for (var index = 0; index < cycle.RecordCount; index++)
        {
            var record = cycle.GetRecord(index);
            var rank = RecordRank(record.Identity, expectedActions);
            if (rank <= previousRank) throw InvalidRecordSequence();
            previousRank = rank;
            var payload = record.GetPayloadCopy();
            switch (record.Identity.Kind)
            {
                case ServiceCycleReplayRecordKind.CycleInput:
                    if (++inputCount != 1) throw InvalidRecordSequence();
                    inputCodec.Decode(payload);
                    break;
                case ServiceCycleReplayRecordKind.PreviousState:
                    if (++previousStateCount != 1) throw InvalidRecordSequence();
                    stateCodec.Decode(payload);
                    break;
                case ServiceCycleReplayRecordKind.NextState:
                    if (++nextStateCount != 1) throw InvalidRecordSequence();
                    stateCodec.Decode(payload);
                    break;
                case ServiceCycleReplayRecordKind.Action:
                    if (++actionCount != 1 || record.Identity.Index != 0)
                        throw InvalidRecordSequence();
                    actionName = Name(actionCodec.Decode(payload).Pair);
                    break;
                default:
                    throw InvalidRecordSequence();
            }
        }

        return actionName;
    }

    private static int RecordRank(ServiceCycleReplayRecordIdentity identity, int actionCount)
    {
        return identity.Kind switch
        {
            ServiceCycleReplayRecordKind.CycleInput => 0,
            ServiceCycleReplayRecordKind.PreviousState => 1,
            ServiceCycleReplayRecordKind.Action when identity.Index < actionCount => identity.Index + 2,
            ServiceCycleReplayRecordKind.NextState => actionCount + 2,
            _ => -1,
        };
    }

    private static string Name(AutoHarvestPair pair) => pair switch
    {
        AutoHarvestPair.FruitTree => "Fruit tree",
        AutoHarvestPair.TreasureTree => "Treasure tree",
        _ => throw new ArgumentException("The Auto Harvest action pair is invalid."),
    };

    private static InvalidDataException InvalidRecordSequence() =>
        new("An Auto Harvest cycle contains an invalid replay record sequence.");
}
