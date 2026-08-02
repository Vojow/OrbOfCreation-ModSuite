#if SERVICE_CYCLE_PROFILE
using System;
using JObject = OrbAutomata.GameMcp.GameMcpObjectBuilder;

namespace OrbAutomata.GameMcp;

internal static class GameMcpTargetingProjection
{
    internal static GameMcpValue Project(in TargetingSubmission submission)
    {
        var result = new JObject();
        if (submission.Verified && submission.Evidence.SubmittedTarget != Guid.Empty)
            result["submittedTargetUuid"] = submission.Evidence.SubmittedTarget.ToString("D");
        if (submission.Verified) return result.Freeze();
        result["preflight"] = GameMcpEntityWireNormalizer.Snake(submission.Preflight.ToString());
        if (submission.Evidence.Available)
        {
            result["nativeStage"] = GameMcpEntityWireNormalizer.Snake(submission.Stage.ToString());
            result["outcome"] = GameMcpEntityWireNormalizer.Snake(submission.Outcome.ToString());
            if (submission.Evidence.RequestedTarget != Guid.Empty)
                result["requestedTargetUuid"] = submission.Evidence.RequestedTarget.ToString("D");
            if (submission.Evidence.SubmittedTarget != Guid.Empty)
                result["submittedTargetUuid"] = submission.Evidence.SubmittedTarget.ToString("D");
            result["requestPendingBefore"] = submission.Evidence.RequestPendingBefore;
            result["requestPendingAfter"] = submission.Evidence.RequestPendingAfter;
        }
        if (submission.Preflight is TargetingPreflight.Quarantined or
            TargetingPreflight.PostCommitFault or TargetingPreflight.VerificationFailed)
            result["quarantined"] = true;
        return result.Freeze();
    }

    internal static Guid SubmittedTarget(GameMcpValue? value)
    {
        if (value is not GameMcpObject obj) return Guid.Empty;
        for (var index = 0; index < obj.Properties.Count; index++)
        {
            var property = obj.Properties[index];
            if (property.Name == "submittedTargetUuid" &&
                property.Value is GameMcpScalar scalar && scalar.Value is string text &&
                Guid.TryParse(text, out var id)) return id;
        }
        return Guid.Empty;
    }
}
#endif
