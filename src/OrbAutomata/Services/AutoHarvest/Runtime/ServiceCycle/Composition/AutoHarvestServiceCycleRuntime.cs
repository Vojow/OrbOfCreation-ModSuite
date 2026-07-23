using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Registration;

namespace OrbAutomata;

internal sealed class AutoHarvestServiceCycleRuntime : IAutoHarvestServiceCycleRuntime
{
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<bool> _ownsActionFamily;
    private readonly AutoHarvestBindingResolver _bindings;
    private readonly AutoHarvestNativeGateSet _gates;
    private readonly ServiceCycleReplayRegistration<
        AutoHarvestCycleFrame,
        AutomataConfiguration,
        AutoHarvestCycleState,
        AutoHarvestCycleAction> _registration;
    private readonly AutomataServiceCycleHost _host;
    private readonly AutomataReplayCapture? _replayCapture;
    private readonly AutoHarvestServiceCycleDiagnosticsBridge _diagnostics;
    private AutomataConfiguration _currentConfiguration;
    private bool _disposed;

    internal AutoHarvestServiceCycleRuntime(
        Func<long> readLifecycleEpoch,
        Func<bool> ownsActionFamily,
        AutomataConfiguration initialConfiguration,
        AutoHarvestBindingResolver bindings,
        AutoHarvestNativeGateSet gates,
        ServiceCycleReplayRegistration<
            AutoHarvestCycleFrame,
            AutomataConfiguration,
            AutoHarvestCycleState,
            AutoHarvestCycleAction> registration,
        AutomataServiceCycleHost host,
        AutomataReplayCapture? replayCapture,
        AutoHarvestServiceCycleDiagnosticsBridge diagnostics)
    {
        _readLifecycleEpoch = readLifecycleEpoch ?? throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _ownsActionFamily = ownsActionFamily ?? throw new ArgumentNullException(nameof(ownsActionFamily));
        _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
        _gates = gates ?? throw new ArgumentNullException(nameof(gates));
        _registration = registration ?? throw new ArgumentNullException(nameof(registration));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _replayCapture = replayCapture;
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _currentConfiguration = initialConfiguration;
    }

    internal AutomataConfiguration CurrentConfiguration => _currentConfiguration;
    internal LifecycleGeneration CurrentLifecycle => _host.CurrentLifecycle;
    internal bool EmergencyStopEngaged => _host.EmergencyStopEngaged;

    public void Tick(float unscaledDeltaTime)
    {
        if (_disposed) return;
        var emergencyDisabled = _currentConfiguration.Safety.EmergencyDisable;
        if (_host.EmergencyStopEngaged != emergencyDisabled)
            _host.SetEmergencyStop(emergencyDisabled, EmergencyStopReason.UserRequested);
        var report = _host.Tick();
        _diagnostics.Observe(_host.Pump, in report, _ownsActionFamily());
        _replayCapture?.ObserveFrame(report);
    }

    public void PublishSavedConfiguration(AutomataConfiguration configuration)
    {
        if (_disposed) return;
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));
        _registration.Configuration.CompleteSave(
            ConfigurationSaveResult<AutomataConfiguration>.Saved(configuration));
        _currentConfiguration = configuration;
        _diagnostics.ObserveConfiguration(configuration, _ownsActionFamily());
    }

    public void CancelPreparedWork()
    {
        if (_disposed) return;
        if (!_host.EmergencyStopEngaged)
            _host.SetEmergencyStop(true, EmergencyStopReason.SuiteShutdown);
    }

    public void InvalidateLifecycle()
    {
        if (_disposed) return;
        var nativeLifecycle = _readLifecycleEpoch();
        if (!_host.TryReplaceLifecycle(nativeLifecycle)) return;
        _replayCapture?.ObserveLifecycleBoundary();
        _bindings.InvalidateLifecycle();
        _gates.ObserveLifecycle(nativeLifecycle);
        _diagnostics.ObserveLifecycle(nativeLifecycle, _currentConfiguration, _ownsActionFamily());
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (!_host.EmergencyStopEngaged)
                _host.SetEmergencyStop(true, EmergencyStopReason.SuiteShutdown);
        }
        finally
        {
            _replayCapture?.Dispose();
            try
            {
                _diagnostics.Dispose();
            }
            finally
            {
                try
                {
                    _host.Shutdown();
                }
                finally
                {
                    try { _registration.Dispose(); }
                    finally { _bindings.InvalidateLifecycle(); }
                }
            }
        }
    }
}
