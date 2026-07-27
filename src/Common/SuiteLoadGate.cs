using System;
using System.Text;

namespace OrbModding.Common;

/// <summary>
/// The single decision every suite plugin asks before doing anything: may this build be loaded at
/// all?
/// </summary>
/// <remarks>
/// <para>
/// The suite computes the game's own economy math itself rather than asking the game to recompute
/// it (see <c>Runtime/GameMath</c>). That math is transcribed from one specific, audited assembly
/// pair. On any other build the transcription is not merely unverified — it may be wrong in ways
/// that produce confident, plausible, incorrect numbers.
/// </para>
/// <para>
/// <b>There is deliberately no degraded mode.</b> "Fall back to asking the game" is not a safe
/// option: a hash mismatch means the build is unaudited, which invalidates the reflected member
/// contracts exactly as much as it invalidates the ported math. A fallback would read members by
/// name from an assembly whose shape is equally unknown, while feeling safer — a second full
/// implementation, exercised only when something is already wrong. Refusing to load is the only
/// honest response.
/// </para>
/// <para>
/// This is a deliberate trade: any game patch, including one that changes nothing we touch,
/// disables the suite until the build is re-audited. That cost is accepted because the alternative
/// is silent divergence, which is the one failure mode a user cannot detect.
/// </para>
/// </remarks>
public static class SuiteLoadGate
{
    /// <summary>
    /// Decides whether the suite may load against the installed game.
    /// Callers must invoke this <b>before</b> applying any Harmony patch, subscribing to any game
    /// event, or registering any service, so that a refusal leaves the game untouched.
    /// </summary>
    public static SuiteLoadDecision Evaluate(string gameRoot)
    {
        AssemblyAuditResult audit;
        try
        {
            audit = GameAssemblyAudit.Check(gameRoot);
        }
        catch (Exception ex)
        {
            // An audit that cannot run is not an audit that passed.
            return SuiteLoadDecision.Refused(
                $"Refusing to load: the game assembly audit could not be completed ({ex.GetBaseException().Message}).");
        }

        return audit.MatchesExpected
            ? SuiteLoadDecision.Allowed(audit.MatchedBaselineId)
            : SuiteLoadDecision.Refused(DescribeRefusal(audit));
    }

    /// <summary>
    /// Builds the one message a user gets when the suite refuses. It names the observed hashes and
    /// the audited baselines, because "the mod did not load" without them is indistinguishable from
    /// a broken install and cannot be acted on.
    /// </summary>
    private static string DescribeRefusal(AssemblyAuditResult audit)
    {
        var message = new StringBuilder();
        message.Append("Refusing to load: the installed game build does not match an audited baseline. ");

        if (audit.DiscoveryFailure.Length != 0)
        {
            message.Append(audit.DiscoveryFailure).Append(' ');
        }

        message
            .Append("Observed Assembly-CSharp=")
            .Append(Describe(audit.AssemblyCSharp.ActualSha256))
            .Append(", Assembly-CSharp-firstpass=")
            .Append(Describe(audit.AssemblyCSharpFirstPass.ActualSha256))
            .Append(". Audited baselines: ")
            .Append(GameAssemblyAudit.WindowsBaselineId)
            .Append(", ")
            .Append(GameAssemblyAudit.MacBaselineId)
            .Append(". The suite computes the game's economy math itself, so it will not run against ")
            .Append("an unverified build; re-audit this build to restore it.");

        return message.ToString();
    }

    private static string Describe(string? sha256) =>
        string.IsNullOrEmpty(sha256) ? "<missing>" : sha256!;
}

/// <summary>The outcome of <see cref="SuiteLoadGate.Evaluate"/>.</summary>
public readonly struct SuiteLoadDecision
{
    private SuiteLoadDecision(bool shouldLoad, string baselineId, string message)
    {
        ShouldLoad = shouldLoad;
        BaselineId = baselineId;
        Message = message;
    }

    /// <summary>Whether the plugin may proceed. When false, it must do nothing at all.</summary>
    public bool ShouldLoad { get; }

    /// <summary>The matched baseline identifier, empty when refused.</summary>
    public string BaselineId { get; }

    /// <summary>An explanation suitable for a single log line, in both outcomes.</summary>
    public string Message { get; }

    internal static SuiteLoadDecision Allowed(string baselineId) =>
        new(true, baselineId ?? string.Empty, $"Game build matches audited baseline {baselineId}.");

    internal static SuiteLoadDecision Refused(string message) =>
        new(false, string.Empty, message);
}
