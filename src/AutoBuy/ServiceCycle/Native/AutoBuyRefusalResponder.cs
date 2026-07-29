using System;

namespace OrbAutomata;

/// <summary>
/// What happens when the game refuses a purchase the worker planned. The action boundary reports the
/// refusal; the responder decides what the suite does about it.
/// </summary>
internal interface IAutoBuyRefusalResponsePort
{
    void ObserveRefusal(in AutoBuyRefusalReport report);
}

/// <summary>
/// Writes every refusal in full, skips expected affordability staleness, and stands Auto Buy down
/// only for structural or otherwise impossible disagreements.
/// </summary>
/// <remarks>
/// <para>
/// A live structural contradiction is not a race the planner should ride out: retrying one produced
/// 1,988 identical refusals in a prior session, so those still stop after one full diagnostic.
/// Affordability is different. Resource quantities can move after collection through drain and
/// earlier queue-time spending, so a price-only disagreement is expected snapshot staleness: the
/// action skips, the service stays active, and the next fresh world is planned normally.
/// </para>
/// <para>
/// Standing down means turning Auto Buy's own setting off, through the same write path the toggle
/// button uses. That is deliberate: the Mod Config screen then shows it off, and turning it back on
/// is the one-click thing an operator already knows how to do. Nothing here re-enables it, and there
/// is no separate quarantine state that could disagree with the setting.
/// </para>
/// </remarks>
internal sealed class AutoBuyRefusalResponder : IAutoBuyRefusalResponsePort
{
    private readonly Func<bool> _isActive;
    private readonly Action<string> _standDown;
    private readonly IAutoBuyRefusalBundlePort _bundles;
    private readonly Action<string> _log;
    private readonly Func<DateTime> _utcNow;

    public AutoBuyRefusalResponder(
        Func<bool> isActive,
        Action<string> standDown,
        IAutoBuyRefusalBundlePort bundles,
        Action<string> log,
        Func<DateTime>? utcNow = null)
    {
        _isActive = isActive ?? throw new ArgumentNullException(nameof(isActive));
        _standDown = standDown ?? throw new ArgumentNullException(nameof(standDown));
        _bundles = bundles ?? throw new ArgumentNullException(nameof(bundles));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public void ObserveRefusal(in AutoBuyRefusalReport report)
    {
        // Already off: there is no active service policy to recover or stand down, and another
        // callback would only duplicate an earlier diagnosis. Re-enabling opts into handling a new
        // refusal again.
        if (!_isActive()) return;

        var now = _utcNow();
        var located = _bundles.TryWrite(AutoBuyRefusalBundle.Render(in report, now), now, out var path);
        var where = located ? path : "unavailable (the bundle could not be written)";
        if (report.Diagnosis.Classification ==
            AutoBuyRefusalClassification.AffordabilityChanged)
        {
            var affordabilitySummary =
                $"Auto Buy skipped a purchase whose live resources had moved since planning " +
                $"({report.Candidate}): {report.Diagnosis.Describe()}. Diagnostic bundle: {where}. " +
                "Auto Buy remains enabled and will re-plan from the next world collection.";
            _log(affordabilitySummary);
            return;
        }

        var summary =
            $"Auto Buy planned a purchase the game would not take ({report.Candidate}): " +
            $"{report.Diagnosis.Describe()}. Diagnostic bundle: {where}. " +
            "Auto Buy disabled itself; re-enable in Mod Config after reviewing.";

        _standDown(summary);
        _log(summary);
    }
}
