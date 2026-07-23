using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime.Catalog;
using Xunit;

namespace OrbModding.Tests.Runtime.Catalog;

public sealed class LifecycleDefinitionCatalogTests
{
    [Fact]
    public void CatalogAssignsDenseHandlesAndResolvesByStableIdentity()
    {
        var builder = new DefinitionCatalogBuilder();
        var fruit = builder.Add("11111111-1111-4111-8111-111111111111", "PlotNodeSO", "FruitPlot");
        var treasure = builder.Add("22222222-2222-4222-8222-222222222222", "PlotNodeSO", "TreasurePlot");
        var catalog = builder.Build(schemaVersion: 1, lifecycleGeneration: 7);

        Assert.Equal(0, fruit.Value);
        Assert.Equal(1, treasure.Value);
        Assert.Equal(2, catalog.Count);
        Assert.Equal(7, catalog.LifecycleGeneration);
        Assert.Equal(1, catalog.SchemaVersion);
        Assert.True(catalog.TryResolve("11111111-1111-4111-8111-111111111111", "PlotNodeSO", out var handle));
        Assert.Equal(fruit, handle);
        Assert.Equal("FruitPlot", catalog[handle].DiagnosticName);
        Assert.False(catalog.TryResolve("11111111-1111-4111-8111-111111111111", "UpgradeSO", out _));
    }

    [Fact]
    public void CatalogPreservesImmutableStaticRelationships()
    {
        var builder = new DefinitionCatalogBuilder();
        var plot = builder.Add("aaaaaaaa-1111-4111-8111-111111111111", "PlotNodeSO", "Plot");
        var action = builder.Add("bbbbbbbb-2222-4222-8222-222222222222", "PlotNodeActionSO", "Collect");
        builder.Relate(plot, action);
        var catalog = builder.Build(1, 1);

        var relations = catalog.RelationsOf(plot);
        Assert.Single(relations);
        Assert.Equal(action, relations[0]);
        Assert.Empty(catalog.RelationsOf(action));
        Assert.False(relations is DefinitionHandle[]);
        Assert.Throws<NotSupportedException>(() => ((IList<DefinitionHandle>)relations)[0] = default);
        Assert.Equal(action, catalog.RelationsOf(plot)[0]);
    }

    [Fact]
    public void DuplicateUuidIsRejectedAndBuilderIsSingleUse()
    {
        var builder = new DefinitionCatalogBuilder();
        builder.Add("cccccccc-1111-4111-8111-111111111111", "PlotNodeSO", "A");
        Assert.Throws<ArgumentException>(() =>
            builder.Add("cccccccc-1111-4111-8111-111111111111", "UpgradeSO", "B"));

        builder.Build(1, 1);
        Assert.Throws<InvalidOperationException>(() =>
            builder.Add("dddddddd-1111-4111-8111-111111111111", "PlotNodeSO", "C"));
        Assert.Throws<InvalidOperationException>(() => builder.Build(1, 1));
    }

    [Fact]
    public void DefaultHandleIsInvalidAndForeignCatalogHandleIsRejected()
    {
        Assert.False(default(DefinitionHandle).IsValid);

        var builderA = new DefinitionCatalogBuilder();
        var fruitA = builderA.Add("11111111-1111-4111-8111-111111111111", "PlotNodeSO", "FruitPlot");
        var treasureA = builderA.Add("22222222-2222-4222-8222-222222222222", "PlotNodeSO", "TreasurePlot");
        builderA.Relate(fruitA, treasureA);
        var catalogA = builderA.Build(schemaVersion: 1, lifecycleGeneration: 5);

        Assert.Throws<ArgumentException>(() => catalogA[default]);
        Assert.Throws<ArgumentException>(() => catalogA.RelationsOf(default));
        Assert.Equal("FruitPlot", catalogA[fruitA].DiagnosticName);
        Assert.Single(catalogA.RelationsOf(fruitA));

        var builderB = new DefinitionCatalogBuilder();
        builderB.Add("11111111-1111-4111-8111-111111111111", "PlotNodeSO", "FruitPlot");
        builderB.Add("22222222-2222-4222-8222-222222222222", "PlotNodeSO", "TreasurePlot");
        var catalogB = builderB.Build(schemaVersion: 1, lifecycleGeneration: 6);

        Assert.Throws<ArgumentException>(() => catalogB[fruitA]);
        Assert.Throws<ArgumentException>(() => catalogB.RelationsOf(fruitA));
        Assert.True(catalogB.TryResolve("11111111-1111-4111-8111-111111111111", "PlotNodeSO", out var fruitB));
        Assert.Equal("FruitPlot", catalogB[fruitB].DiagnosticName);
        Assert.NotEqual(fruitA, fruitB);
    }
}
