using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Status;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

internal static class GameMcpTestHarness
{
    private static readonly Lazy<EntityIdentityCatalogSnapshot> Identities =
        new(LoadIdentityFixture);

    internal static EntityIdentityCatalogSnapshot EntityCatalog => Identities.Value;

    internal static GameMcpFrameContext Context(
        WorldPublication<GameWorldState>? world = null,
        ulong configurationGeneration = 3,
        long lifecycleGeneration = 9,
        FeatureStatusSnapshot[]? features = null,
        DecisionJournalStatus? trace = null,
        GameMcpWritableSettingDescriptor[]? writable = null,
        SuiteRuntimeConfiguration? configuration = null)
    {
        return new GameMcpFrameContext(
            world,
            runtime: null,
            new ConfigurationPublication(
                new ConfigGeneration(configurationGeneration),
                configuration ?? new SuiteRuntimeConfiguration()),
            lifecycleGeneration,
            "Main",
            nativeContractsAvailable: true,
            features ?? Array.Empty<FeatureStatusSnapshot>(),
            trace ?? DecisionJournalStatus.Unavailable,
            traceWriterRevision: 0,
            writable ?? Array.Empty<GameMcpWritableSettingDescriptor>());
    }

    internal static GameMcpFrameContext Context(
        GameWorldState world,
        ulong generation = 1001)
    {
        var publisher = new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(
            world with { EntityIdentities = EntityCatalog },
            new WorldGeneration(generation));
        return Context(publisher.ReadLatest());
    }

    internal static JObject Json(GameMcpObjectBuilder value) =>
        Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(
            value.Freeze(), EntityCatalog));

    internal static JObject Json(GameMcpValue value) =>
        Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(
            value, EntityCatalog));

    internal static GameMcpProtocolResponse Handle(
        GameMcpProtocolRouter router,
        GameMcpFrameInbox inbox,
        JObject request,
        Func<GameMcpFrameOperation, GameMcpToolExecution> execute)
    {
        var pending = Task.Run(() => router.Handle(request));
        GameMcpFrameOperation[] claimed = Array.Empty<GameMcpFrameOperation>();
        Assert.True(SpinWait.SpinUntil(
            () => (claimed = inbox.ClaimPending()).Length > 0 || pending.IsCompleted,
            TimeSpan.FromSeconds(1)));
        Assert.False(pending.IsCompleted, "stateful request completed without a Unity-frame claim");
        for (var index = 0; index < claimed.Length; index++)
            inbox.Complete(claimed[index], execute(claimed[index]));
        Assert.True(pending.Wait(TimeSpan.FromSeconds(1)));
        return pending.Result;
    }

    internal static GameMcpToolExecution ExecuteRead(
        GameMcpFrameOperation operation,
        GameMcpFrameContext context)
    {
        var request = operation.Request;
        return (request.ToolName switch
        {
            "world_overview" => GameMcpToolExecution.Read(
                GameMcpWorldQuery.Overview(context).Freeze()),
            "world_categories" => GameMcpToolExecution.Read(
                GameMcpWorldQuery.ListCategories(context).Freeze()),
            "world_list" => GameMcpToolExecution.Read(GameMcpWorldQuery.ListRows(
                context, request.Category, request.Offset, request.Limit).Freeze()),
            "world_get" => GameMcpToolExecution.Read(
                GameMcpWorldQuery.GetRows(
                    context,
                    request.Category,
                    request.Uuids,
                    request.ExpectedNativeType).Freeze()),
            "entity_catalog" => GameMcpToolExecution.Read(
                GameMcpEntityCatalog.Search(
                    context.World?.Snapshot.EntityIdentities ?? EntityCatalog,
                    request.Query,
                    request.Limit).Freeze()),
            "explain_entity" => GameMcpToolExecution.Read(
                GameMcpEntityExplainer.Explain(
                    context,
                    request.Uuid.ToString("D")).Freeze()),
            "world_search" => GameMcpToolExecution.Read(
                GameMcpWorldQuery.Search(context, request.Query, request.Limit).Freeze()),
            _ => GameMcpToolExecution.Error(new GameMcpObjectBuilder
            {
                ["status"] = "not_available",
                ["code"] = "test_executor_missing",
                ["reason"] = request.ToolName,
            }.Freeze()),
        }).WithEntityIdentities(
            context.World?.Snapshot.EntityIdentities ?? EntityCatalog);
    }

    private static EntityIdentityCatalogSnapshot LoadIdentityFixture()
    {
        var data = Path.Combine(AppContext.BaseDirectory, "data");
        var displayByUuid = File.ReadLines(Path.Combine(data, "entity-display-names.tsv"))
            .Skip(1)
            .Where(line => line.Length > 0)
            .Select(line => line.Split('\t'))
            .ToDictionary(
                cells => Guid.Parse(cells[0]),
                cells => cells.Length > 3 ? cells[3] : string.Empty);
        var rows = File.ReadLines(Path.Combine(data, "entity-mappings.tsv"))
            .Skip(1)
            .Where(line => line.Length > 0)
            .Select(line => line.Split('\t'))
            .Select(cells =>
            {
                var uuid = Guid.Parse(cells[0]);
                displayByUuid.TryGetValue(uuid, out var displayName);
                return new EntityIdentityName(
                    uuid,
                    cells[2],
                    displayName ?? string.Empty,
                    cells[1]);
            })
            .OrderBy(row => row.EntityId)
            .ToArray();
        return EntityIdentityCatalogSnapshot.Bound(9, rows);
    }
}
