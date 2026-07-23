using System;
using System.IO;
using OrbModding.Common.Runtime.Tracing;
using Xunit;

namespace OrbModding.Tests.Runtime.Tracing;

public sealed class FileTraceSegmentStorageTests
{
    [Fact]
    public void FileStorageWritesASegmentViaTempThenAtomicRename()
    {
        var directory = Path.Combine(Path.GetTempPath(), "octr-trace-" + Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new FileTraceSegmentStorage(directory, filePrefix: "seg", extension: ".octr");

            var segment = storage.BeginSegment(0);
            var payload = new byte[] { 42, 7, 255, 0, 13 };
            storage.Append(segment, payload);

            Assert.Empty(Directory.GetFiles(directory, "seg-*.octr"));
            Assert.Single(Directory.GetFiles(directory, "*.tmp-*"));

            storage.CommitSegment(segment);

            var finals = Directory.GetFiles(directory, "seg-*.octr");
            Assert.Single(finals);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp-*"));
            Assert.Equal(payload, File.ReadAllBytes(finals[0]));

            storage.DeleteOldestCommitted();
            Assert.Empty(Directory.GetFiles(directory, "seg-*.octr"));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FileStorageDiscardRemovesTheTempWithoutPublishing()
    {
        var directory = Path.Combine(Path.GetTempPath(), "octr-trace-" + Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new FileTraceSegmentStorage(directory, filePrefix: "seg", extension: ".octr");
            var segment = storage.BeginSegment(1);
            storage.Append(segment, new byte[] { 1, 2, 3 });

            storage.DiscardSegment(segment);

            Assert.Empty(Directory.GetFiles(directory, "seg-*.octr"));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp-*"));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FileStorageConstructionIsSideEffectFreeAndRejectsPathComponents()
    {
        var directory = Path.Combine(Path.GetTempPath(), "octr-trace-" + Guid.NewGuid().ToString("N"));
        try
        {
            _ = new FileTraceSegmentStorage(directory, "seg", ".octr");
            Assert.False(Directory.Exists(directory));
            Assert.Throws<ArgumentException>(() => new FileTraceSegmentStorage(directory, "../escape", ".octr"));
            Assert.Throws<ArgumentException>(() => new FileTraceSegmentStorage(directory, "bad/name", ".octr"));
            Assert.Throws<ArgumentException>(() => new FileTraceSegmentStorage(directory, "bad\\name", ".octr"));
            Assert.Throws<ArgumentException>(() => new FileTraceSegmentStorage(directory, "C:escape", ".octr"));
            Assert.Throws<ArgumentException>(() => new FileTraceSegmentStorage(directory, "seg", "../octr"));
            Assert.Throws<ArgumentException>(() => new FileTraceSegmentStorage(directory, "seg", "octr"));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FileStorageReconcilePrunesOldestResumesOrdinalAndCleansOwnedTemporaryFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "octr-trace-" + Guid.NewGuid().ToString("N"));
        try
        {
            var writer = new FileTraceSegmentStorage(directory, "seg", ".octr");
            for (var ordinal = 0; ordinal < 3; ordinal++)
            {
                var segment = writer.BeginSegment(ordinal);
                writer.Append(segment, new[] { (byte)ordinal });
                writer.CommitSegment(segment);
            }
            var stale = Path.Combine(
                directory,
                "seg-000003.octr.tmp-" + Guid.NewGuid().ToString("N"));
            File.WriteAllBytes(stale, new byte[] { 9 });
            var unrelated = Path.Combine(directory, "keep-me.txt");
            File.WriteAllText(unrelated, "unrelated");

            var recovered = new FileTraceSegmentStorage(directory, "seg", ".octr").Reconcile(2);

            Assert.Equal(3, recovered.NextOrdinal);
            Assert.Equal(2, recovered.RetainedSegments);
            Assert.Equal(1, recovered.StartupPrunedSegments);
            Assert.Equal(1, recovered.StaleTemporaryFilesRemoved);
            Assert.False(File.Exists(Path.Combine(directory, "seg-000000.octr")));
            Assert.True(File.Exists(Path.Combine(directory, "seg-000001.octr")));
            Assert.True(File.Exists(Path.Combine(directory, "seg-000002.octr")));
            Assert.True(File.Exists(unrelated));
            Assert.False(File.Exists(stale));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FileStorageNeverOverwritesCommittedCollisionAndRejectsMalformedOwnedNames()
    {
        var directory = Path.Combine(Path.GetTempPath(), "octr-trace-" + Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new FileTraceSegmentStorage(directory, "seg", ".octr");
            var first = storage.BeginSegment(0);
            storage.Append(first, new byte[] { 1 });
            storage.CommitSegment(first);

            var collision = storage.BeginSegment(0);
            storage.Append(collision, new byte[] { 2 });
            Assert.Throws<IOException>(() => storage.CommitSegment(collision));
            storage.DiscardSegment(collision);
            Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(directory, "seg-000000.octr")));

            File.WriteAllBytes(Path.Combine(directory, "seg-999999999999.octr"), new byte[] { 3 });
            Assert.Throws<IOException>(() =>
                new FileTraceSegmentStorage(directory, "seg", ".octr").Reconcile(2));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FileStorageOrdinalExhaustionFailsBeforePruningCommittedEvidence()
    {
        var directory = Path.Combine(Path.GetTempPath(), "octr-trace-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            var oldest = Path.Combine(directory, "seg-000000.octr");
            var newest = Path.Combine(directory, "seg-2147483647.octr");
            File.WriteAllBytes(oldest, new byte[] { 1 });
            File.WriteAllBytes(newest, new byte[] { 2 });

            Assert.Throws<TraceSegmentOrdinalExhaustedException>(() =>
                new FileTraceSegmentStorage(directory, "seg", ".octr").Reconcile(1));
            Assert.True(File.Exists(oldest));
            Assert.True(File.Exists(newest));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
