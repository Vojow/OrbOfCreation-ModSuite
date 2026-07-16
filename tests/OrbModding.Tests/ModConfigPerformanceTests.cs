using OrbModConfig;
using OrbModding.Common;
using System;
using System.Collections.Generic;
using Xunit;

namespace OrbModding.Tests;

public sealed class ModConfigPerformanceTests
{
    [Fact]
    public void UiBudgetDenialRetainsDueRepairAndInstallsListenersExactlyOnce()
    {
        var coordinator = new SuitePerformanceCoordinator(
            StopwatchPerformanceClock.Instance, 1000.0, 1000.0);
        long frame = 12;
        using var blocker = coordinator.Register("test", "earlier UI work");
        blocker.SetPending(true);
        using var ui = new ModConfigCoordinatorWork(coordinator, () => frame);
        var listeners = new HashSet<string>(StringComparer.Ordinal);
        var runs = 0;

        Assert.False(ui.TryRun(true, pending: true, () =>
        {
            runs++;
            listeners.Add("magic");
            listeners.Add("time");
        }));
        Assert.True(ui.IsPending);
        Assert.Equal(0, runs);
        Assert.Empty(listeners);

        Assert.Equal(SuiteWorkAdmission.Granted, coordinator.RequestWork(blocker, frame, out var blockLease));
        blockLease.Complete();
        blocker.SetPending(false);
        frame++;
        Assert.True(ui.TryRun(true, pending: true, () =>
        {
            runs++;
            listeners.Add("magic");
            listeners.Add("time");
        }));
        ui.SetState(true, pending: false);
        Assert.Equal(1, runs);
        Assert.Equal(2, listeners.Count);

        ui.SetState(false, pending: true);
        Assert.False(ui.IsPending);
        Assert.False(ui.TryRun(false, pending: true, () => runs++));
        Assert.Equal(1, runs);
    }

    [Fact]
    public void NavigationIntegrityCadenceDoesNotRunEveryFrame()
    {
        var remaining = 5.0f;

        for (var frame = 0; frame < 299; frame++)
            Assert.False(Plugin.AdvanceCadence(ref remaining, 1.0f / 60.0f, 5.0f));

        Assert.True(Plugin.AdvanceCadence(ref remaining, 1.0f / 60.0f, 5.0f));
        Assert.InRange(remaining, 4.999f, 5.001f);
        Assert.False(Plugin.AdvanceCadence(ref remaining, -10.0f, 5.0f));
    }

    [Fact]
    public void NavigationIntegrityCadenceRecoversAfterLargeFrameGap()
    {
        var remaining = 1.0f;

        Assert.True(Plugin.AdvanceCadence(ref remaining, 3.0f, 5.0f));
        Assert.Equal(5.0f, remaining);
    }

    [Fact]
    public void DeadUiReferencesArePrunedInPlaceAndDetachExactlyOnce()
    {
        var alive = new FakeReference(true, "alive");
        var deadA = new FakeReference(false, "dead-a");
        var deadB = new FakeReference(false, "dead-b");
        var references = new List<FakeReference> { deadA, alive, deadB };
        var detached = new List<string>();

        var removed = ModConfigUiShell.PruneDead(references, item => item.Alive, item => detached.Add(item.Name));

        Assert.Equal(2, removed);
        Assert.Same(alive, Assert.Single(references));
        Assert.Equal(new[] { "dead-b", "dead-a" }, detached);
        Assert.Equal(0, ModConfigUiShell.PruneDead(references, item => item.Alive, item => detached.Add(item.Name)));
        Assert.Equal(2, detached.Count);
    }

    [Fact]
    public void PanelLossInvalidatesShellEvenWhenButtonSurvives()
    {
        Assert.False(ModConfigUiShell.HostsAlive(
            shellHealthy: true, buttonAlive: true, panelAlive: false, parentsAlive: true));
        Assert.True(ModConfigUiShell.HostsAlive(
            shellHealthy: true, buttonAlive: true, panelAlive: true, parentsAlive: true));
    }

    [Fact]
    public void OpenFailureRestoresPreviousNativeViewAndRequestsRepair()
    {
        var recovery = ModConfigUiShell.OpenFailureRecovery(
            restoreRequested: true, previousAlive: true, fallbackAlive: true, anyNativeActive: false);

        Assert.True(recovery.RestorePrevious);
        Assert.False(recovery.RestoreFallback);
        Assert.True(recovery.RepairRequired);
        Assert.False(ModConfigUiShell.OpenFailureRecovery(
            restoreRequested: true, previousAlive: true, fallbackAlive: true, anyNativeActive: true).RestorePrevious);
        Assert.False(ModConfigUiShell.HostsAlive(
            shellHealthy: !recovery.RepairRequired, buttonAlive: true, panelAlive: true, parentsAlive: true));
    }

    [Fact]
    public void OpenPanelHostLossRestoresFallbackAndDetachesOldListenersExactlyOnce()
    {
        var recovery = ModConfigUiShell.OpenFailureRecovery(
            restoreRequested: true, previousAlive: false, fallbackAlive: true, anyNativeActive: false);
        var listeners = new List<string> { "magic", "time", "mods" };
        var detached = new List<string>();

        Assert.False(recovery.RestorePrevious);
        Assert.True(recovery.RestoreFallback);
        Assert.True(recovery.RepairRequired);
        Assert.Equal(3, ModConfigUiShell.DetachAll(listeners, detached.Add));
        Assert.Empty(listeners);
        Assert.Equal(new[] { "magic", "time", "mods" }, detached);
        Assert.Equal(0, ModConfigUiShell.DetachAll(listeners, detached.Add));
        Assert.Equal(3, detached.Count);

        // A repaired shell owns a fresh listener ledger; none of the disposed
        // bindings can leak into it.
        var reinstalledListeners = new List<string> { "magic", "time" };
        Assert.Equal(2, reinstalledListeners.Count);
        Assert.Empty(listeners);
    }

    private sealed record FakeReference(bool Alive, string Name);
}
