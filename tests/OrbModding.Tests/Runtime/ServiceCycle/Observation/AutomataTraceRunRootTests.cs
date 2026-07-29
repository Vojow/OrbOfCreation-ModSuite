using System;
using System.IO;
using OrbAutomata;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Observation;

public sealed class AutomataTraceRunRootTests
{
    [Fact]
    public void TheRunFolderNameIsAFixedWidthUtcTimestamp()
    {
        Assert.Matches("^run-[0-9]{8}-[0-9]{6}-[0-9a-f]{4}$", AutomataTraceRunRoot.RunName);
    }

    /// <summary>
    /// Nothing but this sweep deletes a completed capture folder, so the sweep is the whole cap on
    /// the trace root.
    /// </summary>
    /// <remarks>
    /// Whole folders, oldest first: a surviving folder is still the correlated full/profile pair the
    /// analysis tool requires, which pruning by file or by byte budget would destroy.
    /// </remarks>
    [Fact]
    public void SweepingKeepsTheNewestFoldersAndCountsTheCurrentLaunchAmongThem()
    {
        using var root = new TemporaryDirectory();
        root.CreateRunFolder("run-20260101-000001-aaaa");
        root.CreateRunFolder("run-20260102-000002-bbbb");
        root.CreateRunFolder("run-20260103-000003-cccc");
        root.CreateRunFolder("run-20260104-000004-dddd");

        var removed = AutomataTraceRunRoot.SweepRunFolders(
            root.Path,
            "run-20260105-000005-eeee",
            retained: 3);

        Assert.Equal(2, removed);
        Assert.False(Directory.Exists(Path.Combine(root.Path, "run-20260101-000001-aaaa")));
        Assert.False(Directory.Exists(Path.Combine(root.Path, "run-20260102-000002-bbbb")));
        Assert.True(Directory.Exists(Path.Combine(root.Path, "run-20260103-000003-cccc")));
        Assert.True(Directory.Exists(Path.Combine(root.Path, "run-20260104-000004-dddd")));
    }

    [Fact]
    public void SweepingNeverDeletesTheCurrentRunFolder()
    {
        using var root = new TemporaryDirectory();
        root.CreateRunFolder("run-20260101-000001-aaaa");
        root.CreateRunFolder("run-20260102-000002-bbbb");

        var removed = AutomataTraceRunRoot.SweepRunFolders(
            root.Path,
            "run-20260101-000001-aaaa",
            retained: 1);

        Assert.Equal(1, removed);
        Assert.True(Directory.Exists(Path.Combine(root.Path, "run-20260101-000001-aaaa")));
        Assert.False(Directory.Exists(Path.Combine(root.Path, "run-20260102-000002-bbbb")));
    }

    /// <summary>
    /// The always-on journal directory shares the trace root with the run folders and must survive.
    /// </summary>
    /// <remarks>
    /// The sweep deletes only names it is certain it wrote, so the uppercase suffix is left alone
    /// too. It carries a different timestamp than the lowercase folder because the developer's
    /// filesystem is case-insensitive and the two names would otherwise be one directory.
    /// </remarks>
    [Fact]
    public void SweepingLeavesEveryEntryThatIsNotAWellFormedRunFolder()
    {
        using var root = new TemporaryDirectory();
        root.CreateRunFolder("journal");
        root.CreateRunFolder("run-notatimestamp");
        root.CreateRunFolder("run-20260102-000002-BBBB");
        root.CreateRunFolder("run-20260101-000001-aaaa");

        var removed = AutomataTraceRunRoot.SweepRunFolders(root.Path, "run-x", retained: 1);

        Assert.Equal(1, removed);
        Assert.True(Directory.Exists(Path.Combine(root.Path, "journal")));
        Assert.True(Directory.Exists(Path.Combine(root.Path, "run-notatimestamp")));
        Assert.True(Directory.Exists(Path.Combine(root.Path, "run-20260102-000002-BBBB")));
        Assert.False(Directory.Exists(Path.Combine(root.Path, "run-20260101-000001-aaaa")));
    }

    [Fact]
    public void SweepingAnAbsentTraceRootIsNotAFailure()
    {
        var absent = Path.Combine(Path.GetTempPath(), "orb-trace-absent-" + Guid.NewGuid().ToString("N"));

        Assert.Equal(0, AutomataTraceRunRoot.SweepRunFolders(absent, "run-x", retained: 4));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AutomataTraceRunRoot.SweepRunFolders(absent, "run-x", retained: 0));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "orb-trace-root-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        internal void CreateRunFolder(string name)
        {
            var folder = System.IO.Path.Combine(Path, name);
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(System.IO.Path.Combine(folder, "artifact.bin"), new byte[] { 1 });
        }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
