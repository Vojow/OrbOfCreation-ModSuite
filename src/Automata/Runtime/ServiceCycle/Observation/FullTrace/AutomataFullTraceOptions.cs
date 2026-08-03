using System;
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
        if (!IsSafeArtifactName(artifactName))
            throw new ArgumentException("A bounded artifact basename is required.", nameof(artifactName));
        Session = session;
        SemanticSession = semanticSession;
        ArtifactName = artifactName;
    }

    internal FullTraceSessionId Session { get; }
    internal ServiceCycleTraceSessionId SemanticSession { get; }
    internal ISegmentSessionStorage Storage { get; }
    internal string ArtifactName { get; }

    internal static bool IsSafeArtifactName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value is "." or "..") return false;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '-' or '_')
                continue;
            return false;
        }
        return true;
    }
}

internal interface IAutomataFullTraceSessionSource
{
    AutomataFullTraceSessionSpec Create();
}

internal readonly struct AutomataFullTraceOptions
{
    internal AutomataFullTraceOptions(IAutomataFullTraceSessionSource sessions)
    {
        Sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
    }

    internal bool Enabled => Sessions is not null;
    internal IAutomataFullTraceSessionSource? Sessions { get; }
}
