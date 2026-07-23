using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

internal static class ServiceCycleReplaySemanticEvidenceFinder
{
    internal static ServiceCycleReplaySemanticEvidence Find(ServiceCycleTraceDocument semantic, int[] eventIndices)
    {
        var evidence = ServiceCycleReplaySemanticEvidence.Empty;
        for (var index = 0; index < eventIndices.Length; index++)
        {
            var eventIndex = eventIndices[index];
            switch (semantic[eventIndex].Kind)
            {
                case ServiceCycleSemanticEventKind.CaptureStarted:
                    if (evidence.CaptureStarted >= 0)
                        return evidence.WithError(ServiceCycleReplaySemanticJoinCode.CaptureStartedDuplicate);
                    evidence.CaptureStarted = eventIndex;
                    break;
                case ServiceCycleSemanticEventKind.CaptureCompleted:
                    if (evidence.CaptureCompleted >= 0)
                        return evidence.WithError(ServiceCycleReplaySemanticJoinCode.CaptureCompletedDuplicate);
                    evidence.CaptureCompleted = eventIndex;
                    break;
                case ServiceCycleSemanticEventKind.CycleQueued:
                    if (evidence.CycleQueued >= 0)
                        return evidence.WithError(ServiceCycleReplaySemanticJoinCode.CycleQueuedDuplicate);
                    evidence.CycleQueued = eventIndex;
                    break;
                case ServiceCycleSemanticEventKind.CycleStarted:
                    if (evidence.CycleStarted >= 0)
                        return evidence.WithError(ServiceCycleReplaySemanticJoinCode.CycleStartedDuplicate);
                    evidence.CycleStarted = eventIndex;
                    break;
                case ServiceCycleSemanticEventKind.EvaluationStarted:
                    if (evidence.EvaluationStarted >= 0)
                        return evidence.WithError(ServiceCycleReplaySemanticJoinCode.EvaluationStartedDuplicate);
                    evidence.EvaluationStarted = eventIndex;
                    break;
                case ServiceCycleSemanticEventKind.EvaluationCompleted:
                case ServiceCycleSemanticEventKind.EvaluationFaulted:
                    if (evidence.Evaluation >= 0)
                        return evidence.WithError(ServiceCycleReplaySemanticJoinCode.EvaluationTerminalDuplicate);
                    evidence.Evaluation = eventIndex;
                    break;
                case ServiceCycleSemanticEventKind.ProjectionFaulted:
                    if (evidence.ProjectionFaulted >= 0)
                        return evidence.WithError(ServiceCycleReplaySemanticJoinCode.AbortedFooterEvidenceInvalid);
                    evidence.ProjectionFaulted = eventIndex;
                    break;
                case ServiceCycleSemanticEventKind.StatePublished:
                    if (evidence.State >= 0)
                        return evidence.WithError(ServiceCycleReplaySemanticJoinCode.StatePublicationDuplicate);
                    evidence.State = eventIndex;
                    break;
                case ServiceCycleSemanticEventKind.BatchPublished:
                    if (evidence.Published >= 0)
                        return evidence.WithError(ServiceCycleReplaySemanticJoinCode.BatchPublicationDuplicate);
                    evidence.Published = eventIndex;
                    break;
                case ServiceCycleSemanticEventKind.BatchCompleted:
                case ServiceCycleSemanticEventKind.BatchAborted:
                case ServiceCycleSemanticEventKind.BatchOrphaned:
                    if (evidence.BatchTerminal >= 0)
                        return evidence.WithError(ServiceCycleReplaySemanticJoinCode.BatchTerminalDuplicate);
                    evidence.BatchTerminal = eventIndex;
                    break;
                case ServiceCycleSemanticEventKind.CycleCompleted:
                case ServiceCycleSemanticEventKind.CycleOrphaned:
                case ServiceCycleSemanticEventKind.CycleFaulted:
                    if (evidence.CycleTerminal >= 0)
                        return evidence.WithError(ServiceCycleReplaySemanticJoinCode.CycleTerminalDuplicate);
                    evidence.CycleTerminal = eventIndex;
                    break;
            }
        }
        return evidence;
    }
}

internal struct ServiceCycleReplaySemanticEvidence
{
    internal static ServiceCycleReplaySemanticEvidence Empty => new()
    {
        CaptureStarted = -1,
        CaptureCompleted = -1,
        CycleQueued = -1,
        CycleStarted = -1,
        EvaluationStarted = -1,
        Evaluation = -1,
        ProjectionFaulted = -1,
        State = -1,
        Published = -1,
        BatchTerminal = -1,
        CycleTerminal = -1,
        Error = ServiceCycleReplaySemanticJoinCode.Complete,
    };

    internal int CaptureStarted;
    internal int CaptureCompleted;
    internal int CycleQueued;
    internal int CycleStarted;
    internal int EvaluationStarted;
    internal int Evaluation;
    internal int ProjectionFaulted;
    internal int State;
    internal int Published;
    internal int BatchTerminal;
    internal int CycleTerminal;
    internal ServiceCycleReplaySemanticJoinCode Error;

    internal ServiceCycleReplaySemanticEvidence WithError(ServiceCycleReplaySemanticJoinCode error)
    {
        Error = error;
        return this;
    }
}
