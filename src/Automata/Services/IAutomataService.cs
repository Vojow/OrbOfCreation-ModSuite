using System;

namespace OrbAutomata;

/// <summary>
/// Common lifecycle surface for an ordered Automata runtime service.
/// Implementations remain feature-owned; this contract only coordinates their lifecycle.
/// </summary>
internal interface IAutomataService : IDisposable
{
    void Tick(float unscaledDeltaTime);
    void CancelPreparedWork();
    void InvalidateLifecycle();
}
