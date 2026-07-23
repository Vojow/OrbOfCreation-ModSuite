using System;
using System.Collections.Generic;
using System.Threading;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using Xunit;

namespace OrbModding.Tests.Runtime;

public sealed class RuntimeDiagnosticsRegistryTests
{
    [Fact]
    public void SnapshotCopiesCapabilitiesAndRejectsDuplicateStableIds()
    {
        var capabilities = new List<RuntimeCapabilityDiagnostics>
        {
            OperationalCapability("Fruit", "Fruit trees"),
        };
        var snapshot = Snapshot(capabilities);

        capabilities[0] = OperationalCapability("Treasure", "Treasure trees");

        Assert.Equal("Fruit", snapshot.Capabilities[0].CapabilityId);
        Assert.Throws<ArgumentException>(() => Snapshot(new[]
        {
            OperationalCapability("Fruit", "Fruit trees"),
            OperationalCapability("Fruit", "Duplicate"),
        }));
    }

    [Fact]
    public void CapabilityStateAndReasonMustRemainConsistent()
    {
        Assert.Throws<ArgumentException>(() => new RuntimeCapabilityDiagnostics(
            "Fruit", "Fruit trees", true, FeatureStatusState.Operational,
            new FeatureStatusReason(FeatureStatusReasonCode.NativeBusy, "waiting")));
        Assert.Throws<ArgumentException>(() => new RuntimeCapabilityDiagnostics(
            "Fruit", "Fruit trees", true, FeatureStatusState.Locked));
        Assert.Throws<ArgumentException>(() => new RuntimeCapabilityDiagnostics(
            "Fruit", "Fruit trees", false, FeatureStatusState.NotReady,
            new FeatureStatusReason(FeatureStatusReasonCode.GameplayNotReady, "waiting")));
    }

    [Fact]
    public void RegistryOwnsOnePublisherAndSuppressesEquivalentUpdates()
    {
        var registry = new RuntimeDiagnosticsRegistry();
        var transitions = new List<RuntimeDiagnosticsTransition>();
        registry.Transitioned += transitions.Add;
        var initial = Snapshot(new[] { OperationalCapability("Fruit", "Fruit trees") });

        using var registration = registry.Register(initial);

        Assert.False(registration.Update(Snapshot(new[] { OperationalCapability("Fruit", "Fruit trees") })));
        Assert.Single(transitions);
        Assert.Equal(RuntimeDiagnosticsTransitionKind.Added, transitions[0].Kind);
        Assert.Throws<InvalidOperationException>(() => registry.Register(initial));

        var changed = Snapshot(new[] { LockedCapability("Fruit", "Fruit trees") });
        Assert.True(registration.Update(changed));
        Assert.Equal(2, transitions.Count);
        Assert.Equal(RuntimeDiagnosticsTransitionKind.Changed, transitions[1].Kind);
        Assert.Equal((long)2, transitions[1].Revision);
        Assert.Equal(FeatureStatusState.Locked, transitions[1].Current!.Capabilities[0].State);
    }

    [Fact]
    public void SnapshotOrderingRemovalAndSubscriberIsolationAreDeterministic()
    {
        var registry = new RuntimeDiagnosticsRegistry();
        var observed = 0;
        registry.Transitioned += _ => throw new InvalidOperationException("isolated");
        registry.Transitioned += _ => observed++;

        var second = registry.Register(Snapshot(
            new[] { OperationalCapability("Fruit", "Fruit trees") },
            key: new FeatureStatusKey("plugin.z", "service")));
        using var first = registry.Register(Snapshot(
            new[] { OperationalCapability("Fruit", "Fruit trees") },
            key: new FeatureStatusKey("plugin.a", "service")));

        Assert.Equal(2, observed);
        var snapshot = registry.GetSnapshot();
        Assert.Equal("plugin.a", snapshot[0].Key.PluginId);
        Assert.Equal("plugin.z", snapshot[1].Key.PluginId);

        second.Dispose();
        Assert.Equal(3, observed);
        Assert.Single(registry.GetSnapshot());
    }

    [Fact]
    public void RegistryRejectsCrossThreadReadsAndWrites()
    {
        var registry = new RuntimeDiagnosticsRegistry();
        using var registration = registry.Register(Snapshot(
            new[] { OperationalCapability("Fruit", "Fruit trees") }));
        Exception? readFailure = null;
        Exception? writeFailure = null;
        var thread = new Thread(() =>
        {
            try { registry.GetSnapshot(); }
            catch (Exception ex) { readFailure = ex; }
            try
            {
                registration.Update(Snapshot(
                    new[] { LockedCapability("Fruit", "Fruit trees") }));
            }
            catch (Exception ex) { writeFailure = ex; }
        });

        thread.Start();
        thread.Join();

        Assert.IsType<InvalidOperationException>(readFailure);
        Assert.IsType<InvalidOperationException>(writeFailure);
    }

    private static RuntimeServiceDiagnosticsSnapshot Snapshot(
        IReadOnlyList<RuntimeCapabilityDiagnostics> capabilities,
        FeatureStatusKey? key = null) => new(
        key ?? new FeatureStatusKey("plugin.test", "AutoHarvest"),
        "Auto Harvest",
        "ServiceCycle",
        lifecycleGeneration: 4,
        capabilities);

    private static RuntimeCapabilityDiagnostics OperationalCapability(string id, string name) =>
        new(id, name, true, FeatureStatusState.Operational);

    private static RuntimeCapabilityDiagnostics LockedCapability(string id, string name) =>
        new(
            id,
            name,
            true,
            FeatureStatusState.Locked,
            new FeatureStatusReason(FeatureStatusReasonCode.ProgressionLocked, "Not unlocked."));
}
