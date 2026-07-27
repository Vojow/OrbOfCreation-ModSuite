using System;
using OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Control;
using OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Format;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common.Runtime.Tracing;

namespace OrbAutomata;

internal readonly struct AutomataFullTraceSessionSpec
{
    internal AutomataFullTraceSessionSpec(
        FullTraceSessionId session,
        ServiceCycleTraceSessionId semanticSession,
        ISegmentSessionStorage storage,
        string artifactName)
    {
        if (!session.IsValid) throw new ArgumentException("A valid full-trace session is required.", nameof(session));
        if (!semanticSession.IsValid)
            throw new ArgumentException("A valid semantic session is required.", nameof(semanticSession));
        Storage = storage ?? throw new ArgumentNullException(nameof(storage));
        if (!ManualFullTraceStatus.IsSafeArtifactName(artifactName))
            throw new ArgumentException("A bounded artifact basename is required.", nameof(artifactName));
        Session = session;
        SemanticSession = semanticSession;
        ArtifactName = artifactName;
    }

    internal FullTraceSessionId Session { get; }
    internal ServiceCycleTraceSessionId SemanticSession { get; }
    internal ISegmentSessionStorage Storage { get; }
    internal string ArtifactName { get; }
}

internal interface IAutomataFullTraceSessionSource
{
    AutomataFullTraceSessionSpec Create();
}

internal readonly struct AutomataFullTraceOptions
{
    internal AutomataFullTraceOptions(
        ManualFullTraceControlRegistry control,
        IAutomataFullTraceSessionSource sessions)
    {
        Control = control ?? throw new ArgumentNullException(nameof(control));
        Sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
    }

    internal bool Enabled => Control is not null && Sessions is not null;
    internal ManualFullTraceControlRegistry? Control { get; }
    internal IAutomataFullTraceSessionSource? Sessions { get; }
}
