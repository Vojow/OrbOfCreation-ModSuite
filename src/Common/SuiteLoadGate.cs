using System;
using System.Text;

namespace OrbModding.Common;

/// <summary>
/// Separates loading the suite's diagnostic control plane from permitting gameplay mutation.
/// </summary>
/// <remarks>
/// <para>
/// The suite computes the game's own economy math itself rather than asking the game to recompute
/// it. An audited assembly pair may start normally. An unknown but complete pair may load only
/// configuration and differential-verification surfaces; Harmony patches, services, feature
/// controls, and gameplay mutation remain quarantined.
/// </para>
/// <para>
/// A player may explicitly accept the exact observed pair from Advanced settings. That acceptance
/// is bound to both assembly hashes, so it does not silently follow a later game update. An
/// incomplete or undiscoverable installation still refuses completely because there is no stable
/// identity to acknowledge.
/// </para>
/// </remarks>
public static class SuiteLoadGate
{
    /// <summary>
    /// Audits the installed assembly pair before any Harmony patch or gameplay service is composed.
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
            return SuiteLoadDecision.Refused(
                $"Refusing to load: the game assembly audit could not be completed ({ex.GetBaseException().Message}).");
        }

        if (audit.MatchesExpected)
            return SuiteLoadDecision.Allowed(audit.MatchedBaselineId);
        if (CanLoadQuarantined(audit))
            return SuiteLoadDecision.Quarantined(
                BuildFingerprint(audit),
                DescribeQuarantine(audit));
        return SuiteLoadDecision.Refused(DescribeRefusal(audit));
    }

    private static string DescribeQuarantine(AssemblyAuditResult audit)
    {
        var message = new StringBuilder();
        message.Append(
            "Gameplay runtime quarantined: the installed game build does not match an audited baseline. ");
        message
            .Append("Observed Assembly-CSharp=")
            .Append(Describe(audit.AssemblyCSharp.ActualSha256))
            .Append(", Assembly-CSharp-firstpass=")
            .Append(Describe(audit.AssemblyCSharpFirstPass.ActualSha256))
            .Append(". Audited baselines: ")
            .Append(GameAssemblyAudit.WindowsBaselineId)
            .Append(", ")
            .Append(GameAssemblyAudit.WindowsV1052BaselineId)
            .Append(", ")
            .Append(GameAssemblyAudit.MacBaselineId)
            .Append(", ")
            .Append(GameAssemblyAudit.MacV1052BaselineId)
            .Append(". Configuration and differential verification remain available, but all gameplay ")
            .Append("patches and services are emergency-stopped. Players may clear the General emergency ")
            .Append("stop or use the Advanced acknowledgement to accept this exact assembly pair at their ")
            .Append("own risk; a later game update requires a new acceptance.");
        return message.ToString();
    }

    private static string DescribeRefusal(AssemblyAuditResult audit)
    {
        var message = new StringBuilder(
            "Refusing to load even the diagnostic control plane: the game assembly audit is incomplete. ");
        if (audit.DiscoveryFailure.Length != 0)
            message.Append(audit.DiscoveryFailure).Append(' ');
        message
            .Append("Observed Assembly-CSharp=")
            .Append(Describe(audit.AssemblyCSharp.ActualSha256))
            .Append(", Assembly-CSharp-firstpass=")
            .Append(Describe(audit.AssemblyCSharpFirstPass.ActualSha256))
            .Append('.');
        return message.ToString();
    }

    private static bool CanLoadQuarantined(AssemblyAuditResult audit) =>
        audit.DiscoveryFailure.Length == 0 &&
        IsHash(audit.AssemblyCSharp.ActualSha256) &&
        IsHash(audit.AssemblyCSharpFirstPass.ActualSha256);

    private static string BuildFingerprint(AssemblyAuditResult audit) =>
        $"{audit.AssemblyCSharp.ActualSha256}:{audit.AssemblyCSharpFirstPass.ActualSha256}";

    private static bool IsHash(string? value) => value?.Length == 64;

    private static string Describe(string? sha256) =>
        string.IsNullOrEmpty(sha256) ? "<missing>" : sha256!;
}

/// <summary>The outcome of <see cref="SuiteLoadGate.Evaluate"/>.</summary>
public readonly struct SuiteLoadDecision
{
    private SuiteLoadDecision(
        bool shouldLoad,
        bool canLoadControlPlane,
        string baselineId,
        string observedBuildFingerprint,
        string message)
    {
        ShouldLoad = shouldLoad;
        CanLoadControlPlane = canLoadControlPlane;
        BaselineId = baselineId;
        ObservedBuildFingerprint = observedBuildFingerprint;
        Message = message;
    }

    /// <summary>Whether gameplay patches and services may start without a player override.</summary>
    public bool ShouldLoad { get; }

    /// <summary>Whether configuration and verification surfaces may load.</summary>
    public bool CanLoadControlPlane { get; }

    /// <summary>Whether only the control plane is admitted.</summary>
    public bool IsQuarantined => CanLoadControlPlane && !ShouldLoad;

    /// <summary>The matched baseline identifier, empty when the pair is not audited.</summary>
    public string BaselineId { get; }

    /// <summary>The exact complete assembly pair, empty when it could not be established.</summary>
    public string ObservedBuildFingerprint { get; }

    /// <summary>An explanation suitable for a single log line.</summary>
    public string Message { get; }

    internal static SuiteLoadDecision Allowed(string baselineId) =>
        new(
            true,
            true,
            baselineId ?? string.Empty,
            string.Empty,
            $"Game build matches audited baseline {baselineId}.");

    internal static SuiteLoadDecision Quarantined(
        string observedBuildFingerprint,
        string message) =>
        new(false, true, string.Empty, observedBuildFingerprint ?? string.Empty, message);

    internal static SuiteLoadDecision Refused(string message) =>
        new(false, false, string.Empty, string.Empty, message);
}
