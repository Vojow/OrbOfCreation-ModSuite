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
/// Diagnoses a refusal in full and then stands Auto Buy down.
/// </summary>
/// <remarks>
/// <para>
/// A native refusal of a planned purchase is not a race the planner should ride out — it is the
/// planner and the game disagreeing about the same facts, and every retry is another wrong answer.
/// A live session spent itself planning one upgrade the game refused 1,988 times in a row, so the
/// response is not to pace the retries but to stop, write down everything both halves knew, and hand
/// it to whoever can fix the planner.
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
        // Already off: the batch that produced this refusal cascade-terminates anyway, and a second
        // bundle for the same stand-down would say the same thing twice. An operator who turns it
        // back on gets the full treatment again, which is what re-enabling means.
        if (!_isActive()) return;

        var now = _utcNow();
        var located = _bundles.TryWrite(AutoBuyRefusalBundle.Render(in report, now), now, out var path);
        var where = located ? path : "unavailable (the bundle could not be written)";
        var summary =
            $"Auto Buy planned a purchase the game would not take ({report.Candidate}): " +
            $"{report.Diagnosis.Describe()}. Diagnostic bundle: {where}. " +
            "Auto Buy disabled itself; re-enable in Mod Config after reviewing.";

        _standDown(summary);
        _log(summary);
    }
}
