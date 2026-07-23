using System;
using System.IO;
using OrbModding.Common.Runtime.Tracing;
using Xunit;

namespace OrbModding.Tests.Runtime.Tracing;

public sealed class AtomicSessionDirectoryTests
{
    [Fact]
    public void ArtifactNameCannotEscapeItsRoot()
    {
        var parent = NewParent();
        try
        {
            var root = Path.Combine(parent, "root");

            Assert.Throws<ArgumentException>(() => new AtomicSessionDirectory(root, "../escape"));

            Assert.False(Directory.Exists(root));
            Assert.False(File.Exists(Path.Combine(parent, "escape")));
            Assert.False(Directory.Exists(Path.Combine(parent, "escape")));
        }
        finally
        {
            DeleteParent(parent);
        }
    }

    [Fact]
    public void FileNameCannotEscapeItsSession()
    {
        var parent = NewParent();
        try
        {
            var root = Path.Combine(parent, "root");
            var directory = new AtomicSessionDirectory(root, "session");
            directory.Initialize();

            Assert.Throws<ArgumentException>(() => directory.CommitFile("../escape", new byte[] { 1 }));

            Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(root, "session")));
            Assert.False(File.Exists(Path.Combine(root, "escape")));
            Assert.False(Directory.Exists(Path.Combine(root, "escape")));
        }
        finally
        {
            DeleteParent(parent);
        }
    }

    private static string NewParent() => Path.Combine(
        Path.GetTempPath(),
        "orb-atomic-session-directory-" + Guid.NewGuid().ToString("N"));

    private static void DeleteParent(string parent)
    {
        if (Directory.Exists(parent)) Directory.Delete(parent, recursive: true);
    }
}
