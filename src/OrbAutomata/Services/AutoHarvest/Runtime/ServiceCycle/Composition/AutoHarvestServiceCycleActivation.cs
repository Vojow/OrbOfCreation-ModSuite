using System;

namespace OrbAutomata;

internal interface IAutoHarvestServiceCycleRuntime : IAutomataService
{
    void PublishSavedConfiguration(AutomataConfiguration configuration);
}

internal sealed class AutoHarvestServiceCycleActivation : IAutomataService
{
    private readonly Func<bool> _canActivate;
    private readonly Func<IAutoHarvestServiceCycleRuntime?> _tryCreate;
    private IAutoHarvestServiceCycleRuntime? _runtime;
    private bool _activationAttempted;
    private bool _disposed;

    public AutoHarvestServiceCycleActivation(
        Func<bool> canActivate,
        Func<IAutoHarvestServiceCycleRuntime?> tryCreate)
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

    public void PublishSavedConfiguration(AutomataConfiguration configuration)
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
