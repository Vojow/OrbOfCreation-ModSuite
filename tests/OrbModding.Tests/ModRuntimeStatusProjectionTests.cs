using System.Collections.Generic;
using System.Linq;
using OrbModConfig;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

public sealed class ModRuntimeStatusProjectionTests
{
    [Fact]
    public void Projection_JoinsStatusesByExactPluginGuidAndOrdersFeatures()
    {
        var statuses = new List<FeatureStatusSnapshot>
        {
            Operational("plugin.target", "zeta", "Zeta"),
            Operational("PLUGIN.TARGET", "wrong-case", "Wrong case"),
            Operational("plugin.other", "other", "Other"),
            Blocked("plugin.target", "alpha", "Alpha"),
        };

        var projection = ModRuntimeStatusProjection.Build("plugin.target", statuses);

        Assert.Equal(new[] { "alpha", "zeta" }, projection.Features.Select(status => status.Key.FeatureId));
        Assert.DoesNotContain("Wrong case", projection.FormatCompact());
        Assert.Contains("Alpha: ON | Waiting | native queue busy", projection.FormatCompact());
        Assert.Contains("Zeta: ON | Operational", projection.FormatCompact());
        Assert.Contains("\n", projection.FormatCompact());
    }

    [Fact]
    public void Projection_DoesNotSynthesizeRuntimeStateForUnpublishedPlugin()
    {
        var projection = ModRuntimeStatusProjection.Build(
            "plugin.unpublished",
            new[] { Operational("plugin.other", "feature", "Feature") });

        Assert.Empty(projection.Features);
        Assert.Equal("Runtime status: Not reported by this plugin.", projection.FormatCompact());
    }

    [Fact]
    public void Projection_PreservesConfiguredStateSeparatelyFromRuntimeState()
    {
        var status = new FeatureStatusSnapshot(
            new FeatureStatusKey("plugin.target", "feature"),
            "Feature",
            false,
            FeatureStatusState.ConfigurationDisabled,
            new FeatureStatusReason(FeatureStatusReasonCode.ConfigurationDisabled, "disabled in saved configuration"));

        var projection = ModRuntimeStatusProjection.Build("plugin.target", new[] { status });

        Assert.False(projection.Features[0].ConfiguredEnabled);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, projection.Features[0].State);
        Assert.Contains("Feature: OFF | Off | disabled in saved configuration", projection.FormatCompact());
    }

    private static FeatureStatusSnapshot Operational(string pluginId, string featureId, string displayName) => new(
        new FeatureStatusKey(pluginId, featureId),
        displayName,
        true,
        FeatureStatusState.Operational);

    private static FeatureStatusSnapshot Blocked(string pluginId, string featureId, string displayName) => new(
        new FeatureStatusKey(pluginId, featureId),
        displayName,
        true,
        FeatureStatusState.TemporarilyBlocked,
        new FeatureStatusReason(FeatureStatusReasonCode.NativeBusy, "native queue busy"));
}
