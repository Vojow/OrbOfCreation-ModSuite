using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;

/// <summary>Which pump frame the owner thread is currently inside.</summary>
/// <remarks>
/// <para>
/// Capture and action work runs inside a frame, but the runtime reports both from the runner rather
/// than from the pump, so the frame identity cannot ride the fact itself. The pump opens this cursor
/// as the frame begins and closes it as the frame ends; the capture and action emitters read it.
/// </para>
/// <para>
/// Frame zero is a legal frame, so absence is <see cref="Unframed"/> rather than zero. A fact
/// emitted from a host control transition rather than from inside a frame belongs to no frame and
/// must not claim one.
/// </para>
/// </remarks>
internal sealed class ServiceCycleSemanticFrameCursor
{
    internal const long Unframed = -1;

    internal long Frame { get; private set; } = Unframed;

    internal void Enter(long frameIdentity)
    {
        if (frameIdentity < 0) throw new ArgumentOutOfRangeException(nameof(frameIdentity));
        Frame = frameIdentity;
    }

    internal void Leave() => Frame = Unframed;
}
