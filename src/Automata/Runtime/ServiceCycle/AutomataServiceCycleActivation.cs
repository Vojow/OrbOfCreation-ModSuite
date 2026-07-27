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
    private readonly Func<bool> _canActivate;
    private readonly Func<IAutomataServiceCycleRuntime?> _tryCreate;
    private IAutomataServiceCycleRuntime? _runtime;
    private bool _activationAttempted;
    private bool _disposed;

    public AutomataServiceCycleActivation(
        Func<bool> canActivate,
        Func<IAutomataServiceCycleRuntime?> tryCreate)
    {
        _canActivate = canActivate ?? throw new ArgumentNullException(nameof(canActivate));
        _tryCreate = tryCreate ?? throw new ArgumentNullException(nameof(tryCreate));
    }

    public void Tick(float unscaledDeltaTime)
    {
        if (_disposed) return;
        if (!_activationAttempted && _canActivate())
        {
            _activationAttempted = true;
            _runtime = _tryCreate();
        }
        _runtime?.Tick(unscaledDeltaTime);
    }

    public void PublishSavedConfiguration(SuiteRuntimeConfiguration configuration)
    {
        if (_disposed) return;
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));
        _runtime?.PublishSavedConfiguration(configuration);
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
}
