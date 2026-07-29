using System;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>
/// The suite lifecycle surface of the one Automata-owned ServiceCycle runtime, plus its
/// saved-configuration publication. Kept as an interface so deferred activation can be
/// exercised without standing up a real host.
/// </summary>
internal interface IAutomataServiceCycleRuntime : IDisposable
{
    void Tick(float unscaledDeltaTime);
    void PublishSavedConfiguration(
        SuiteRuntimeConfiguration configuration,
        ConfigGeneration configurationGeneration);
    void CancelPreparedWork();
    void InvalidateLifecycle();
}

/// <summary>
/// Lazily brings the one Automata-owned ServiceCycle runtime
/// online once the game lifecycle is ready, then forwards the suite lifecycle surface
/// to it. Creation receives the latest configuration directly, so activation never
/// constructs from one snapshot and then replays another publication.
/// </summary>
internal sealed class AutomataServiceCycleActivation : IDisposable
{
    private const int MaximumActivationAttempts = 2;

    private readonly Func<bool> _canActivate;
    private readonly Func<
        SuiteRuntimeConfiguration,
        ConfigGeneration,
        IAutomataServiceCycleRuntime?> _tryCreate;
    private readonly Action<SuiteRuntimeConfiguration, ConfigGeneration>? _observeHostUnavailable;
    private IAutomataServiceCycleRuntime? _runtime;
    private SuiteRuntimeConfiguration _latestConfiguration;
    private ConfigGeneration _latestConfigurationGeneration;
    private int _activationAttempts;
    private bool _disposed;

    public AutomataServiceCycleActivation(
        Func<bool> canActivate,
        Func<SuiteRuntimeConfiguration, ConfigGeneration, IAutomataServiceCycleRuntime?> tryCreate,
        SuiteRuntimeConfiguration initialConfiguration,
        ConfigGeneration initialConfigurationGeneration,
        Action<SuiteRuntimeConfiguration, ConfigGeneration>? observeHostUnavailable = null)
    {
        _canActivate = canActivate ?? throw new ArgumentNullException(nameof(canActivate));
        _tryCreate = tryCreate ?? throw new ArgumentNullException(nameof(tryCreate));
        _latestConfiguration = initialConfiguration ??
            throw new ArgumentNullException(nameof(initialConfiguration));
        _latestConfigurationGeneration = initialConfigurationGeneration;
        _observeHostUnavailable = observeHostUnavailable;
    }

    public void Tick(float unscaledDeltaTime)
    {
        if (_disposed) return;
        if (_runtime is null &&
            _activationAttempts < MaximumActivationAttempts &&
            _canActivate())
        {
            _activationAttempts++;
            _runtime = _tryCreate(
                _latestConfiguration,
                _latestConfigurationGeneration);
            if (_runtime is null)
                ObserveHostUnavailable();
        }
        _runtime?.Tick(unscaledDeltaTime);
    }

    public void PublishSavedConfiguration(
        SuiteRuntimeConfiguration configuration,
        ConfigGeneration configurationGeneration)
    {
        if (_disposed) return;
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));
        if (configurationGeneration.Value <= _latestConfigurationGeneration.Value) return;
        _latestConfiguration = configuration;
        _latestConfigurationGeneration = configurationGeneration;
        if (_runtime is not null)
        {
            _runtime.PublishSavedConfiguration(configuration, configurationGeneration);
            return;
        }
        if (_activationAttempts != 0) ObserveHostUnavailable();
    }

    public void CancelPreparedWork()
    {
        if (_disposed) return;
        _runtime?.CancelPreparedWork();
    }

    public void InvalidateLifecycle()
    {
        if (_disposed) return;
        _runtime?.InvalidateLifecycle();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _runtime?.Dispose();
        _runtime = null;
    }

    private void ObserveHostUnavailable()
    {
        _observeHostUnavailable?.Invoke(
            _latestConfiguration,
            _latestConfigurationGeneration);
    }
}
