using System;
using System.Threading;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;

namespace OrbAutomata;

/// <summary>
/// How long the service is standing down after the player cast something by hand.
/// </summary>
/// <remarks>
/// <para>
/// Manual-fire detection is an event, and an event has no snapshot equivalent: the game does not
/// record that a cast was the player's, only that a cast happened. So <c>SpellFirePatch</c> survives
/// the migration, and this is the main-thread state it feeds. Both places that can start work — the
/// service's own start gate and its action boundary — refresh and consult it, which is what keeps a
/// cast planned just before a manual one from landing just after it.
/// </para>
/// <para>
/// The deadline is a timestamp rather than the legacy engine's per-frame countdown. A countdown only
/// decays while something is ticking it, so a pause taken just before the plugin stopped ticking used
/// to outlive the game session that earned it; a deadline compared against the runtime's own clock
/// expires on time whether anyone was looking or not.
/// </para>
/// </remarks>
internal sealed class AutoCastManualPauseState
{
    private long _observedFireEpoch;
    private long _pausedUntilTicks;

    /// <summary>
    /// Starts watching from now. Casts the player made before this existed are not casts it missed —
    /// counting them would silence the service for a second the first time it ever looked.
    /// </summary>
    public AutoCastManualPauseState() => Reset();

    /// <summary>
    /// Arms the pause if a manual cast has happened since the last look.
    /// </summary>
    /// <remarks>
    /// Polled rather than pushed. The Harmony prefix runs inside the game's own call stack with no
    /// clock to hand and no business taking one, so it counts and the main thread stamps.
    /// </remarks>
    public void Refresh(MonotonicTimestamp now, SuiteRuntimeConfiguration configuration)
    {
        var epoch = AutoCastManualSignal.ManualFireEpoch;
        if (Interlocked.Exchange(ref _observedFireEpoch, epoch) == epoch) return;

        var pause = AutoCastConfigurationPolicy.ManualPause(configuration);
        if (pause <= MonotonicDuration.Zero) return;
        Volatile.Write(ref _pausedUntilTicks, (now + pause).Ticks);
    }

    public bool IsPaused(MonotonicTimestamp now) => now.Ticks < Volatile.Read(ref _pausedUntilTicks);

    /// <summary>How much of the pause is left, for a start gate that has to name a wake.</summary>
    public MonotonicDuration Remaining(MonotonicTimestamp now)
    {
        var until = Volatile.Read(ref _pausedUntilTicks);
        return until <= now.Ticks
            ? MonotonicDuration.Zero
            : MonotonicDuration.FromTimeSpan(TimeSpan.FromTicks(until - now.Ticks));
    }

    /// <summary>
    /// Forgets the pause. A lifecycle boundary retains nothing, and a pause earned in the previous
    /// run of the game is not a fact about this one.
    /// </summary>
    public void Reset()
    {
        Volatile.Write(ref _pausedUntilTicks, 0);
        Interlocked.Exchange(ref _observedFireEpoch, AutoCastManualSignal.ManualFireEpoch);
    }
}
