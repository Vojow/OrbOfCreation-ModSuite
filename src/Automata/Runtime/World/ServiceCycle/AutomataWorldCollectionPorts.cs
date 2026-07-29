using System;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

/// <summary>
/// The Unity-thread half of collection, behind an interface so the service can be driven without a
/// game.
/// </summary>
/// <remarks>
/// The collector is the only implementation that matters, but it needs a loaded Unity player loop and
/// a live assembly to construct, and the service's own logic — when to collect, what to do with a
/// partial pass, what to publish — is worth testing without either.
/// </remarks>
internal interface IAutomataWorldCapturePort
{
    /// <summary>Whether any category resolved on this build. False means collection cannot run at all.</summary>
    bool IsAvailable { get; }

    /// <summary>Reads every category into <paramref name="frame"/>. Unity thread only.</summary>
    WorldCollectionReport Collect(GameWorldCycleFrame frame);
}

/// <summary>Collects through the real <see cref="GameWorldCollector"/>.</summary>
internal sealed class AutomataWorldCapturePort : IAutomataWorldCapturePort
{
    private readonly GameWorldCollector _collector;
    private readonly Func<long> _readFrameIdentity;
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Action<WorldCollectionReport>? _announce;
    private string _announced = string.Empty;

    /// <param name="readFrameIdentity">
    /// The same frame counter the host pumps with. Read here rather than threaded through the cycle
    /// contexts because capture already runs on the Unity thread, where the counter is meaningful,
    /// and both callers resolve it from one delegate so they cannot drift.
    /// </param>
    /// <param name="readLifecycleEpoch">
    /// The same native lifecycle counter the host replaces its runners on. Read at the moment the game
    /// is read, for the same reason the frame counter is: an epoch resolved later would name the run
    /// the derivation finished under rather than the one the readings came from.
    /// </param>
    internal AutomataWorldCapturePort(
        GameWorldCollector collector,
        Func<long> readFrameIdentity,
        Func<long> readLifecycleEpoch,
        Action<WorldCollectionReport>? announce = null)
    {
        _collector = collector ?? throw new ArgumentNullException(nameof(collector));
        _readFrameIdentity = readFrameIdentity ?? throw new ArgumentNullException(nameof(readFrameIdentity));
        _readLifecycleEpoch = readLifecycleEpoch ?? throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _announce = announce;
    }

    /// <summary>
    /// True while any category can be read. A build that renamed one member still yields every other
    /// category, so a partial collector is worth running; a collector that resolved nothing is not.
    /// </summary>
    public bool IsAvailable => _collector.IsAnyCategoryAvailable;

    public WorldCollectionReport Collect(GameWorldCycleFrame frame)
    {
        frame.CollectedAtFrame = _readFrameIdentity();
        frame.CollectedAtEpoch = _readLifecycleEpoch();
        var report = _collector.Collect(frame);
        Announce(in report);
        return report;
    }

    /// <summary>
    /// Says what the pass managed — once, and again only when the answer changes.
    /// </summary>
    /// <remarks>
    /// Collection runs four times a second, so announcing every pass would bury the log, and
    /// announcing none of them leaves a build that renamed one member indistinguishable from a quiet
    /// game: the projection carries a count of unavailable categories and no member name anywhere.
    /// A healthy pass compares as one stable key so a growing entity count does not re-announce.
    /// </remarks>
    private void Announce(in WorldCollectionReport report)
    {
        if (_announce is null) return;
        var key = report.IsComplete ? "complete" : report.Describe();
        if (string.Equals(key, _announced, StringComparison.Ordinal)) return;
        _announced = key;
        _announce(report);
    }
}
