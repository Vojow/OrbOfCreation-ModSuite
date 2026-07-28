using System;
using OrbModding.Common.Runtime.Configuration;

namespace OrbAutomata;

/// <summary>
/// The suite lifecycle surface of the one Automata-owned ServiceCycle runtime, plus its
/// saved-configuration publication. Kept as an interface so the Tier-1 activation can be
/// exercised without standing up a real host.
/// </summary>
internal interface IAutomataServiceCycleRuntime : IAutomataService
{
    void PublishSavedConfiguration(SuiteRuntimeConfiguration configuration);
}

/// <summary>
/// Tier-1 participant that lazily brings the one Automata-owned ServiceCycle runtime
/// online once the game lifecycle is ready, then forwards the suite lifecycle surface
/// to it. It owns no host, feature, or typed service generic — only the deferred
/// creation and the forwarding.
/// </summary>
internal sealed class AutomataServiceCycleActivation : IAutomataService
{
    private const int MaximumActivationAttempts = 2;

    private readonly Func<bool> _canActivate;
    private readonly Func<IAutomataServiceCycleRuntime?> _tryCreate;
    private readonly Action<SuiteRuntimeConfiguration>? _observeHostUnavailable;
    private IAutomataServiceCycleRuntime? _runtime;
    private SuiteRuntimeConfiguration? _latestConfiguration;
    private int _activationAttempts;
    private bool _hasPendingConfiguration;
    private bool _disposed;

    public AutomataServiceCycleActivation(
        Func<bool> canActivate,
        Func<IAutomataServiceCycleRuntime?> tryCreate,
        SuiteRuntimeConfiguration? initialConfiguration = null,
        Action<SuiteRuntimeConfiguration>? observeHostUnavailable = null)
    {
        _canActivate = canActivate ?? throw new ArgumentNullException(nameof(canActivate));
        _tryCreate = tryCreate ?? throw new ArgumentNullException(nameof(tryCreate));
        _latestConfiguration = initialConfiguration;
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
            _runtime = _tryCreate();
            if (_runtime is null)
            {
                ObserveHostUnavailable();
            }
            else if (_hasPendingConfiguration && _latestConfiguration is not null)
            {
                _runtime.PublishSavedConfiguration(_latestConfiguration);
                _hasPendingConfiguration = false;
            }
        }
        _runtime?.Tick(unscaledDeltaTime);
    }

    public void PublishSavedConfiguration(SuiteRuntimeConfiguration configuration)
    {
        if (_disposed) return;
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));
        _latestConfiguration = configuration;
        if (_runtime is not null)
        {
            _runtime.PublishSavedConfiguration(configuration);
            return;
        }
        _hasPendingConfiguration = true;
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
        if (_latestConfiguration is not null)
            _observeHostUnavailable?.Invoke(_latestConfiguration);
    }
}
