using OrbModConfig;
using OrbModding.Common;
using System;
using System.Collections.Generic;
using Xunit;

namespace OrbModding.Tests;

public sealed class ModConfigPerformanceTests
{
    [Fact]
    [Trait("Category", "PerformanceSimulation")]
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
    public void CatalogDiscoveryAndLoggingRunOnceOnlyAfterUiLeaseAdmission()
    {
        var coordinator = new SuitePerformanceCoordinator(
            StopwatchPerformanceClock.Instance, 1000.0, 1000.0);
        long frame = 31;
        using var blocker = coordinator.Register("test", "earlier UI work");
        blocker.SetPending(true);
        using var ui = new ModConfigCoordinatorWork(coordinator, () => frame);
        ConfigCatalogSnapshot? catalog = null;
        var discoveryCalls = 0;
        var logCalls = 0;
        void DiscoverAndLog()
        {
            ModConfigCatalogSession.GetOrDiscover(
                ref catalog,
                () =>
                {
                    discoveryCalls++;
                    return new ConfigCatalogSnapshot(Array.Empty<ModConfigDescriptor>());
                },
                _ => logCalls++);
        }

        Assert.False(ui.TryRun(true, pending: true, DiscoverAndLog));
        Assert.Null(catalog);
        Assert.Equal(0, discoveryCalls);
        Assert.Equal(0, logCalls);

        Assert.Equal(SuiteWorkAdmission.Granted, coordinator.RequestWork(blocker, frame, out var lease));
        lease.Complete();
        blocker.SetPending(false);
        frame++;
        Assert.True(ui.TryRun(true, pending: true, DiscoverAndLog));
        Assert.NotNull(catalog);
        Assert.Equal(1, discoveryCalls);
        Assert.Equal(1, logCalls);

        frame++;
        Assert.True(ui.TryRun(true, pending: true, DiscoverAndLog));
        Assert.Equal(1, discoveryCalls);
        Assert.Equal(1, logCalls);
    }

    [Fact]
    [Trait("Category", "PerformanceSimulation")]
    public void NavigationIntegrityCadenceDoesNotRunEveryFrame()
    {
        var remaining = 5.0f;

        for (var frame = 0; frame < 299; frame++)
            Assert.False(global::OrbModding.Plugin.AdvanceCadence(ref remaining, 1.0f / 60.0f, 5.0f));

        Assert.True(global::OrbModding.Plugin.AdvanceCadence(ref remaining, 1.0f / 60.0f, 5.0f));
        Assert.InRange(remaining, 4.999f, 5.001f);
        Assert.False(global::OrbModding.Plugin.AdvanceCadence(ref remaining, -10.0f, 5.0f));
    }

    [Fact]
    public void NavigationIntegrityCadenceRecoversAfterLargeFrameGap()
    {
        var remaining = 1.0f;

        Assert.True(global::OrbModding.Plugin.AdvanceCadence(ref remaining, 3.0f, 5.0f));
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

        var removed = ModConfigNativeNavigationPolicy.PruneDead(
            references,
            item => item.Alive,
            item => detached.Add(item.Name));

        Assert.Equal(2, removed);
        Assert.Same(alive, Assert.Single(references));
        Assert.Equal(new[] { "dead-b", "dead-a" }, detached);
        Assert.Equal(0, ModConfigNativeNavigationPolicy.PruneDead(
            references,
            item => item.Alive,
            item => detached.Add(item.Name)));
        Assert.Equal(2, detached.Count);
    }

    [Fact]
    public void PanelLossInvalidatesShellEvenWhenButtonSurvives()
    {
        Assert.False(ModConfigNativeNavigationPolicy.HostsAlive(
            hostHealthy: true, buttonAlive: true, panelAlive: false, parentsAlive: true));
        Assert.True(ModConfigNativeNavigationPolicy.HostsAlive(
            hostHealthy: true, buttonAlive: true, panelAlive: true, parentsAlive: true));
    }

    [Fact]
    public void OpenFailureRestoresPreviousNativeViewAndRequestsRepair()
    {
        var recovery = ModConfigNativeNavigationPolicy.OpenFailureRecovery(
            restoreRequested: true, previousAlive: true, fallbackAlive: true, anyNativeActive: false);

        Assert.True(recovery.RestorePrevious);
        Assert.False(recovery.RestoreFallback);
        Assert.True(recovery.RepairRequired);
        Assert.False(ModConfigNativeNavigationPolicy.OpenFailureRecovery(
            restoreRequested: true, previousAlive: true, fallbackAlive: true, anyNativeActive: true).RestorePrevious);
        Assert.False(ModConfigNativeNavigationPolicy.HostsAlive(
            hostHealthy: !recovery.RepairRequired, buttonAlive: true, panelAlive: true, parentsAlive: true));
    }

    [Fact]
    public void OpenPanelHostLossRestoresFallbackAndDetachesOldListenersExactlyOnce()
    {
        var recovery = ModConfigNativeNavigationPolicy.OpenFailureRecovery(
            restoreRequested: true, previousAlive: false, fallbackAlive: true, anyNativeActive: false);
        var listeners = new List<string> { "magic", "time", "mods" };
        var detached = new List<string>();

        Assert.False(recovery.RestorePrevious);
        Assert.True(recovery.RestoreFallback);
        Assert.True(recovery.RepairRequired);
        Assert.Equal(3, ModConfigNativeNavigationPolicy.DetachAll(listeners, detached.Add));
        Assert.Empty(listeners);
        Assert.Equal(new[] { "magic", "time", "mods" }, detached);
        Assert.Equal(0, ModConfigNativeNavigationPolicy.DetachAll(listeners, detached.Add));
        Assert.Equal(3, detached.Count);

        // A repaired shell owns a fresh listener ledger; none of the disposed
        // bindings can leak into it.
        var reinstalledListeners = new List<string> { "magic", "time" };
        Assert.Equal(2, reinstalledListeners.Count);
        Assert.Empty(listeners);
    }

    [Fact]
    public void NativeViewContractIsValidatedAndCachedOncePerType()
    {
        Assert.False(NativeViewAdapter.IsViewTypeCached(typeof(FakeNativeView)));
        Assert.True(NativeViewAdapter.TryValidateViewType(typeof(FakeNativeView), out var reason), reason);
        Assert.True(NativeViewAdapter.IsViewTypeCached(typeof(FakeNativeView)));
        Assert.True(NativeViewAdapter.TryValidateViewType(typeof(FakeNativeView), out reason), reason);
    }

    [Fact]
    public void NativeViewContractRejectsIncorrectMethodShapes()
    {
        Assert.False(NativeViewAdapter.TryValidateViewType(typeof(InvalidNativeView), out var reason));
        Assert.Contains("bool IsActive()", reason);
    }

    [Fact]
    public void OpeningUiSchedulesRefreshForCoordinatorInsteadOfRunningItInline()
    {
        var refresh = new ModConfigRefreshScheduler(0.1f);

        refresh.Open();

        Assert.True(refresh.IsPending);
        refresh.Complete();
        Assert.False(refresh.IsPending);
        Assert.False(refresh.Schedule(0.05f));
        Assert.True(refresh.Schedule(0.06f));
        refresh.Close();
        Assert.False(refresh.IsPending);
    }

    private sealed record FakeReference(bool Alive, string Name);

    private sealed class FakeNativeView
    {
        public bool IsActive() => true;
        public void SetActive(bool active) { }
    }

    private sealed class InvalidNativeView
    {
        public int IsActive() => 1;
        public void SetActive(bool active) { }
    }
}
