using OrbAutomata;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Runtime.World;

/// <summary>
/// The epoch a snapshot was collected under: which run of the game it describes, as against how new
/// it is.
/// </summary>
/// <remarks>
/// A frame identity answers "has the world been re-read since my action"; an epoch answers "is this
/// still the same game". They are different questions and the snapshot carries both, because a save
/// load can replace what stands behind an entity while leaving its identity — and its frame
/// comparison — saying nothing at all.
/// </remarks>
public sealed class WorldCollectedEpochTests
{
    /// <summary>
    /// Stamped where the game is read, for the same reason the frame identity is: an epoch resolved
    /// after derivation would name the run that finished the arithmetic rather than the run the
    /// readings came from.
    /// </summary>
    [Fact]
    public void TheCapturePortStampsTheEpochTheGameWasReadUnder()
    {
        var port = new AutomataWorldCapturePort(new GameWorldCollector(), () => 41, () => 7);
        var frame = new GameWorldCycleFrame();

        port.Collect(frame);

        Assert.Equal(7, frame.CollectedAtEpoch);
        Assert.Equal(41, frame.CollectedAtFrame);
    }

    /// <summary>
    /// Derivation carries it through untouched. Nothing off the Unity thread can re-read the epoch, so
    /// a snapshot that lost it could never get it back.
    /// </summary>
    [Fact]
    public void TheEpochTheGameWasReadUnderReachesTheSnapshot()
    {
        var port = new AutomataWorldCapturePort(new GameWorldCollector(), () => 41, () => 7);
        var frame = new GameWorldCycleFrame();

        port.Collect(frame);
        var world = GameWorldFrameDeriver.Build(frame);

        Assert.Equal(7, world.CollectedAtEpoch);
    }

    /// <summary>
    /// A lifecycle boundary moves the epoch, and the next pass says so. The stamp follows the game's
    /// counter rather than counting for itself, so a run that never reloads never moves.
    /// </summary>
    [Fact]
    public void AnEpochThatMovesIsStampedOnTheNextPass()
    {
        var epoch = 3L;
        var port = new AutomataWorldCapturePort(new GameWorldCollector(), () => 1, () => epoch);
        var frame = new GameWorldCycleFrame();

        port.Collect(frame);
        var before = GameWorldFrameDeriver.Build(frame).CollectedAtEpoch;

        port.Collect(frame);
        var unchanged = GameWorldFrameDeriver.Build(frame).CollectedAtEpoch;

        epoch = 4;
        port.Collect(frame);
        var after = GameWorldFrameDeriver.Build(frame).CollectedAtEpoch;

        Assert.Equal(3, before);
        Assert.Equal(3, unchanged);
        Assert.Equal(4, after);
    }

    /// <summary>
    /// The absent value is zero, which is the epoch no lifecycle ever has — so a consumer comparing
    /// against its own pinned lifecycle can never mistake "nobody stamped this" for a match.
    /// </summary>
    [Fact]
    public void AFrameNobodyStampedCarriesTheEpochNoLifecycleEverHas()
    {
        var world = GameWorldFrameDeriver.Build(new GameWorldCycleFrame());

        Assert.Equal(0, world.CollectedAtEpoch);
        Assert.Equal(0, GameWorldStateDefaults.Empty.CollectedAtEpoch);
    }
}
