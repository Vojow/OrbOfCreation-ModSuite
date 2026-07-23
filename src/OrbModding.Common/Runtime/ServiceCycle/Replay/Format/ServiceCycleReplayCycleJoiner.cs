using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

internal static class ServiceCycleReplayCycleJoiner
{
    internal static ServiceCycleReplaySemanticJoin Join(
        ServiceCycleTraceDocument semantic,
        in ServiceCycleReplayArtifactFooter footer,
        ServiceCycleReplayArtifactRecord[] records,
        int[] eventIndices) =>
        Join(semantic, in footer, records, eventIndices, ServiceCycleReplaySemanticIndex.Build(semantic));

    internal static ServiceCycleReplaySemanticJoin Join(
        ServiceCycleTraceDocument semantic,
        in ServiceCycleReplayArtifactFooter footer,
        ServiceCycleReplayArtifactRecord[] records,
        int[] eventIndices,
        ServiceCycleReplaySemanticIndex semanticIndex)
    {
        if (!semantic.IsComplete)
            return ServiceCycleReplayJoinValues.Simple(ServiceCycleReplaySemanticJoinCode.SemanticTraceIncomplete);
        var coverage = ServiceCycleReplayRecordJoinValidator.Validate(in footer, records);
        if (coverage != ServiceCycleReplaySemanticJoinCode.Complete)
            return ServiceCycleReplayJoinValues.Simple(coverage);
        var evidence = ServiceCycleReplaySemanticEvidenceFinder.Find(semantic, eventIndices);
        if (evidence.Error != ServiceCycleReplaySemanticJoinCode.Complete)
            return ServiceCycleReplayJoinValues.Simple(evidence.Error);
        var required = ValidateRequiredEvidence(semantic, in evidence);
        if (required != ServiceCycleReplaySemanticJoinCode.Complete)
            return ServiceCycleReplayJoinValues.Simple(required);
        var publications = ServiceCycleReplayPublicationEvidenceValidator.Validate(
            semanticIndex,
            semantic,
            in footer,
            semantic[evidence.CaptureStarted],
            semantic[evidence.CaptureCompleted]);
        if (publications != ServiceCycleReplaySemanticJoinCode.Complete)
            return ServiceCycleReplayJoinValues.Simple(publications);
        return footer.Disposition == ServiceCycleReplayCycleFooterDisposition.Provisional
            ? JoinProvisional(semantic, semanticIndex, in footer, eventIndices, in evidence)
            : JoinAborted(semantic, semanticIndex, in footer, eventIndices, in evidence);
    }

    private static ServiceCycleReplaySemanticJoin JoinProvisional(
        ServiceCycleTraceDocument semantic,
        ServiceCycleReplaySemanticIndex semanticIndex,
        in ServiceCycleReplayArtifactFooter footer,
        int[] eventIndices,
        in ServiceCycleReplaySemanticEvidence evidence)
    {
        if (!footer.HasReturnedWake || !footer.HasProjection)
            return ServiceCycleReplayJoinValues.Simple(ServiceCycleReplaySemanticJoinCode.FooterNotProvisional);
        if (evidence.ProjectionFaulted >= 0)
            return ServiceCycleReplayJoinValues.Simple(
                ServiceCycleReplaySemanticJoinCode.AbortedFooterEvidenceInvalid);
        if (evidence.State < 0)
            return ServiceCycleReplayJoinValues.Simple(ServiceCycleReplaySemanticJoinCode.StatePublicationMissing);
        if (evidence.Published < 0)
            return ServiceCycleReplayJoinValues.Simple(ServiceCycleReplaySemanticJoinCode.BatchPublicationMissing);
        var evaluation = semantic[evidence.Evaluation];
        if (evaluation.Kind == ServiceCycleSemanticEventKind.EvaluationFaulted)
            return ServiceCycleReplayJoinValues.Simple(
                ServiceCycleReplaySemanticJoinCode.EvaluationFaulted, evaluation.Kind);
        if (!evaluation.Payload.TryGetReturnedWake(out var semanticWake) || semanticWake != footer.ReturnedWake)
            return ServiceCycleReplayJoinValues.Simple(
                ServiceCycleReplaySemanticJoinCode.WakeMismatch, evaluation.Kind);
        if (evaluation.Payload.ActionCount != footer.ExpectedActionCount)
            return ServiceCycleReplayJoinValues.Simple(
                ServiceCycleReplaySemanticJoinCode.EvaluationActionCountMismatch, evaluation.Kind);
        var state = semantic[evidence.State];
        var projection = footer.Projection;
        if (state.Payload.Fingerprint != ServiceCycleProjectionFingerprint.Compute(in projection))
            return ServiceCycleReplayJoinValues.Simple(
                ServiceCycleReplaySemanticJoinCode.ProjectionFingerprintMismatch, evaluation.Kind);
        var published = semantic[evidence.Published];
        if (published.Payload.ActionCount != footer.ExpectedActionCount)
            return ServiceCycleReplayJoinValues.Simple(
                ServiceCycleReplaySemanticJoinCode.EvaluationActionCountMismatch, evaluation.Kind);
        if (!DirectParent(state, semantic[evidence.EvaluationStarted]) ||
            !DirectParent(evaluation, state) || !DirectParent(published, evaluation))
            return ServiceCycleReplayJoinValues.Simple(ServiceCycleReplaySemanticJoinCode.CausalParentMismatch);
        if (evidence.BatchTerminal < 0)
            return ServiceCycleReplayJoinValues.Joined(
                ServiceCycleReplaySemanticJoinCode.BatchTerminalMissing,
                evaluation, default, state, published, default);
        var terminal = semantic[evidence.BatchTerminal];
        if (terminal.Payload.Batch != published.Payload.Batch ||
            terminal.Payload.ActionCount != footer.ExpectedActionCount)
            return ServiceCycleReplayJoinValues.Joined(
                ServiceCycleReplaySemanticJoinCode.NativeEvidenceMismatch,
                evaluation, default, state, published, terminal);
        if (evidence.CycleTerminal < 0)
            return ServiceCycleReplayJoinValues.Joined(
                ServiceCycleReplaySemanticJoinCode.CycleTerminalMissing,
                evaluation, default, state, published, terminal);
        var cycleTerminal = semantic[evidence.CycleTerminal];
        if (!CycleTerminalMatches(terminal, cycleTerminal))
            return ServiceCycleReplayJoinValues.Joined(
                ServiceCycleReplaySemanticJoinCode.NativeEvidenceMismatch,
                evaluation, cycleTerminal, state, published, terminal);
        if (!DirectParent(cycleTerminal, terminal))
            return ServiceCycleReplayJoinValues.Simple(ServiceCycleReplaySemanticJoinCode.CausalParentMismatch);
        var receipt = footer.Context.PreviousReceipt;
        var prior = ServiceCycleReplaySemanticReceiptValidator.Validate(semanticIndex, semantic, in receipt);
        if (prior != ServiceCycleReplaySemanticJoinCode.Complete)
            return ServiceCycleReplayJoinValues.Joined(
                prior, evaluation, cycleTerminal, state, published, terminal);
        var actions = ServiceCycleReplaySemanticActionValidator.Validate(
            semanticIndex, semantic, eventIndices, published, terminal);
        return ServiceCycleReplayJoinValues.Joined(
            actions, evaluation, cycleTerminal, state, published, terminal);
    }

    private static ServiceCycleReplaySemanticJoin JoinAborted(
        ServiceCycleTraceDocument semantic,
        ServiceCycleReplaySemanticIndex semanticIndex,
        in ServiceCycleReplayArtifactFooter footer,
        int[] eventIndices,
        in ServiceCycleReplaySemanticEvidence evidence)
    {
        return footer.Disposition == ServiceCycleReplayCycleFooterDisposition.EvaluationAborted
            ? JoinEvaluationAborted(semantic, semanticIndex, in footer, eventIndices, in evidence)
            : JoinProjectionAborted(semantic, semanticIndex, in footer, eventIndices, in evidence);
    }

    private static ServiceCycleReplaySemanticJoin JoinEvaluationAborted(
        ServiceCycleTraceDocument semantic,
        ServiceCycleReplaySemanticIndex semanticIndex,
        in ServiceCycleReplayArtifactFooter footer,
        int[] eventIndices,
        in ServiceCycleReplaySemanticEvidence evidence)
    {
        var evaluation = semantic[evidence.Evaluation];
        if (evaluation.Kind != ServiceCycleSemanticEventKind.EvaluationFaulted ||
            evidence.ProjectionFaulted >= 0 ||
            evidence.CycleTerminal < 0 ||
            semantic[evidence.CycleTerminal].Kind != ServiceCycleSemanticEventKind.CycleFaulted ||
            evidence.State >= 0 || evidence.Published >= 0 || evidence.BatchTerminal >= 0 ||
            HasAbortedForbiddenEvidence(semantic, eventIndices))
            return ServiceCycleReplayJoinValues.Simple(
                ServiceCycleReplaySemanticJoinCode.AbortedFooterEvidenceInvalid, evaluation.Kind);
        var cycle = semantic[evidence.CycleTerminal];
        if (!DirectParent(evaluation, semantic[evidence.EvaluationStarted]) || !DirectParent(cycle, evaluation))
            return ServiceCycleReplayJoinValues.Simple(ServiceCycleReplaySemanticJoinCode.CausalParentMismatch);
        var prior = footer.Context.PreviousReceipt;
        var receipt = ServiceCycleReplaySemanticReceiptValidator.Validate(semanticIndex, semantic, in prior);
        return receipt == ServiceCycleReplaySemanticJoinCode.Complete
            ? ServiceCycleReplayJoinValues.Aborted(evaluation, cycle)
            : ServiceCycleReplayJoinValues.Simple(receipt, evaluation.Kind);
    }

    private static ServiceCycleReplaySemanticJoin JoinProjectionAborted(
        ServiceCycleTraceDocument semantic,
        ServiceCycleReplaySemanticIndex semanticIndex,
        in ServiceCycleReplayArtifactFooter footer,
        int[] eventIndices,
        in ServiceCycleReplaySemanticEvidence evidence)
    {
        var evaluation = semantic[evidence.Evaluation];
        if (evaluation.Kind != ServiceCycleSemanticEventKind.EvaluationCompleted ||
            evidence.ProjectionFaulted < 0 || evidence.CycleTerminal < 0 ||
            semantic[evidence.CycleTerminal].Kind != ServiceCycleSemanticEventKind.CycleFaulted ||
            evidence.State >= 0 || evidence.Published >= 0 || evidence.BatchTerminal >= 0 ||
            HasAbortedForbiddenEvidence(semantic, eventIndices))
            return ServiceCycleReplayJoinValues.Simple(
                ServiceCycleReplaySemanticJoinCode.AbortedFooterEvidenceInvalid,
                evaluation.Kind);
        var projectionFault = semantic[evidence.ProjectionFaulted];
        var cycle = semantic[evidence.CycleTerminal];
        if (!evaluation.Payload.TryGetReturnedWake(out var evaluationWake) ||
            !projectionFault.Payload.TryGetReturnedWake(out var projectionWake) ||
            evaluationWake != footer.ReturnedWake || projectionWake != footer.ReturnedWake)
            return ServiceCycleReplayJoinValues.Simple(
                ServiceCycleReplaySemanticJoinCode.WakeMismatch,
                evaluation.Kind);
        if (evaluation.Payload.ActionCount != footer.ExpectedActionCount ||
            projectionFault.Payload.ActionCount != footer.ExpectedActionCount)
            return ServiceCycleReplayJoinValues.Simple(
                ServiceCycleReplaySemanticJoinCode.EvaluationActionCountMismatch,
                evaluation.Kind);
        if (!DirectParent(evaluation, semantic[evidence.EvaluationStarted]) ||
            !DirectParent(projectionFault, evaluation) || !DirectParent(cycle, projectionFault))
            return ServiceCycleReplayJoinValues.Simple(ServiceCycleReplaySemanticJoinCode.CausalParentMismatch);
        var prior = footer.Context.PreviousReceipt;
        var receipt = ServiceCycleReplaySemanticReceiptValidator.Validate(semanticIndex, semantic, in prior);
        return receipt == ServiceCycleReplaySemanticJoinCode.Complete
            ? ServiceCycleReplayJoinValues.Aborted(evaluation, cycle)
            : ServiceCycleReplayJoinValues.Simple(receipt, evaluation.Kind);
    }

    private static ServiceCycleReplaySemanticJoinCode ValidateRequiredEvidence(
        ServiceCycleTraceDocument semantic,
        in ServiceCycleReplaySemanticEvidence evidence)
    {
        if (evidence.CaptureStarted < 0) return ServiceCycleReplaySemanticJoinCode.CaptureStartedMissing;
        if (evidence.CaptureCompleted < 0) return ServiceCycleReplaySemanticJoinCode.CaptureCompletedMissing;
        if (evidence.CycleQueued < 0) return ServiceCycleReplaySemanticJoinCode.CycleQueuedMissing;
        if (evidence.CycleStarted < 0) return ServiceCycleReplaySemanticJoinCode.CycleStartedMissing;
        if (evidence.EvaluationStarted < 0) return ServiceCycleReplaySemanticJoinCode.EvaluationStartedMissing;
        if (evidence.Evaluation < 0) return ServiceCycleReplaySemanticJoinCode.EvaluationTerminalMissing;
        if (!DirectParent(semantic[evidence.CaptureCompleted], semantic[evidence.CaptureStarted]) ||
            !DirectParent(semantic[evidence.CycleQueued], semantic[evidence.CaptureCompleted]) ||
            !DirectParent(semantic[evidence.CycleStarted], semantic[evidence.CycleQueued]) ||
            !DirectParent(semantic[evidence.EvaluationStarted], semantic[evidence.CycleStarted]))
            return ServiceCycleReplaySemanticJoinCode.CausalParentMismatch;
        return ServiceCycleReplaySemanticJoinCode.Complete;
    }

    private static bool HasAbortedForbiddenEvidence(ServiceCycleTraceDocument semantic, int[] eventIndices)
    {
        for (var index = 0; index < eventIndices.Length; index++)
            if (semantic[eventIndices[index]].Kind is
                ServiceCycleSemanticEventKind.ActionAttempted or
                ServiceCycleSemanticEventKind.ActionCommitted or
                ServiceCycleSemanticEventKind.ActionRejected or
                ServiceCycleSemanticEventKind.ActionFaulted)
                return true;
        return false;
    }

    private static bool DirectParent(ServiceCycleSemanticEvent child, ServiceCycleSemanticEvent parent) =>
        child.Parent == parent.Id;

    private static bool CycleTerminalMatches(ServiceCycleSemanticEvent batch, ServiceCycleSemanticEvent cycle) =>
        batch.Kind switch
        {
            ServiceCycleSemanticEventKind.BatchCompleted or ServiceCycleSemanticEventKind.BatchAborted
                when batch.Payload.Disposition != (int)BatchTerminalDisposition.Faulted =>
                    cycle.Kind == ServiceCycleSemanticEventKind.CycleCompleted,
            ServiceCycleSemanticEventKind.BatchAborted => cycle.Kind == ServiceCycleSemanticEventKind.CycleFaulted,
            ServiceCycleSemanticEventKind.BatchOrphaned => cycle.Kind == ServiceCycleSemanticEventKind.CycleOrphaned,
            _ => false,
        };
}
