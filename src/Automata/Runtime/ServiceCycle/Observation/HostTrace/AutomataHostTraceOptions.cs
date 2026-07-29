using System;
using OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Format;
using OrbModding.Common.Runtime.ServiceCycle.Observation.HostTrace.Control;
using OrbModding.Common.Runtime.Tracing;

namespace OrbAutomata;

internal readonly struct AutomataHostTraceDumpSpec
{
    internal AutomataHostTraceDumpSpec(
        FullTraceSessionId session,
        ISegmentSessionStorage storage,
        string artifactName)
    {
        if (!session.IsValid) throw new ArgumentException("A valid full-trace session is required.", nameof(session));
        Storage = storage ?? throw new ArgumentNullException(nameof(storage));
        if (!HostTraceDumpStatus.IsSafeArtifactName(artifactName))
            throw new ArgumentException("A bounded artifact basename is required.", nameof(artifactName));
        Session = session;
        ArtifactName = artifactName;
    }

    internal FullTraceSessionId Session { get; }
    internal ISegmentSessionStorage Storage { get; }
    internal string ArtifactName { get; }
}

internal interface IAutomataHostTraceDumpSource
{
    AutomataHostTraceDumpSpec Create();
}

internal readonly struct AutomataHostTraceOptions
{
    internal AutomataHostTraceOptions(
        HostTraceDumpRegistry control,
        IAutomataHostTraceDumpSource dumps)
    {
        Control = control ?? throw new ArgumentNullException(nameof(control));
        Dumps = dumps ?? throw new ArgumentNullException(nameof(dumps));
    }

    internal bool Enabled => Control is not null && Dumps is not null;
    internal HostTraceDumpRegistry? Control { get; }
    internal IAutomataHostTraceDumpSource? Dumps { get; }
}
