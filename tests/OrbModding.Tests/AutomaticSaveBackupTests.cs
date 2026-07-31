using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutomaticSaveBackupTests
{
    private const string Version = "0.5.0-beta.1";
    private static readonly DateTime FirstRun =
        new(2026, 7, 31, 10, 11, 12, DateTimeKind.Utc);

    [Fact]
    public void FreshInstallCreatesVerifiedCountedBackupAndStamp()
    {
        var files = NewFileSystem();
        files.AddFile("/save/ooc_save_0.sav", "slot zero");
        files.AddFile("/save/steam_autocloud.vdf", "cloud metadata");
        files.AddFile("/save/player.log", "not a save");

        var status = Run(files, FirstRun);

        Assert.True(status.AllowsAutomation);
        Assert.True(status.BackupCreated);
        Assert.Equal(AutomaticSaveBackupTrigger.FreshInstall, status.Trigger);
        Assert.Equal(2, status.FileCount);
        Assert.Equal(
            "/save/backups/auto-modsuite-backup-20260731T101112Z",
            status.BackupPath);
        Assert.Equal("slot zero", files.ReadText(status.BackupPath + "/ooc_save_0.sav"));
        Assert.Equal(
            "cloud metadata",
            files.ReadText(status.BackupPath + "/steam_autocloud.vdf"));
        Assert.False(files.FileExists(status.BackupPath + "/player.log"));
        Assert.True(files.FileExists("/config/modsuite.stamp"));
        Assert.Equal(1, files.StampWrites);
    }

    [Fact]
    public void VersionChangeCreatesAnotherBackupAndUpdatesStamp()
    {
        var files = NewFileSystem();
        files.AddFile("/save/ooc_save_0.sav", "before update");
        var old = AutomaticSaveBackup.Run(
            "0.4.0-beta.1",
            "/save",
            "/config/modsuite.stamp",
            FirstRun.AddMinutes(-1),
            files);
        Assert.True(old.AllowsAutomation);

        files.AddFile("/save/ooc_save_0.sav", "after update", overwrite: true);
        var status = Run(files, FirstRun);

        Assert.True(status.AllowsAutomation);
        Assert.True(status.BackupCreated);
        Assert.Equal(AutomaticSaveBackupTrigger.VersionChanged, status.Trigger);
        Assert.Equal("after update", files.ReadText(status.BackupPath + "/ooc_save_0.sav"));
        Assert.Equal(2, files.OwnedBackupDirectories.Count);
        Assert.Equal(2, files.StampWrites);
    }

    [Fact]
    public void SameVersionAndSaveRootAreANoOp()
    {
        var files = NewFileSystem();
        files.AddFile("/save/ooc_save_0.sav", "unchanged");
        var created = Run(files, FirstRun);
        var writesAfterCreation = files.NewFileWrites;

        var status = Run(files, FirstRun.AddHours(1));

        Assert.True(status.AllowsAutomation);
        Assert.False(status.BackupCreated);
        Assert.Equal(AutomaticSaveBackupTrigger.None, status.Trigger);
        Assert.Equal(created.BackupPath, status.BackupPath);
        Assert.Equal(writesAfterCreation, files.NewFileWrites);
        Assert.Equal(1, files.StampWrites);
        Assert.Single(files.OwnedBackupDirectories);
    }

    [Fact]
    public void ReadRefusalAfterOneCopiedFileBlocksAndLeavesExistingStampUntouched()
    {
        var files = NewFileSystem();
        files.AddFile("/save/ooc_save_0.sav", "first");
        files.AddFile("/save/ooc_save_1.sav", "second");
        const string previousBackup =
            "/save/backups/auto-modsuite-backup-20260730T101112Z";
        files.AddDirectory(previousBackup);
        files.AddFile(
            "/config/modsuite.stamp",
            AutomaticSaveBackupStampCodec.Encode(new AutomaticSaveBackupStamp(
                "0.4.0-beta.1",
                "/save",
                previousBackup,
                fileCount: 2)));
        var stampBefore = files.ReadRaw("/config/modsuite.stamp");
        files.RefuseSourceReadOrdinal = 2;

        var status = Run(files, FirstRun);

        Assert.False(status.AllowsAutomation);
        Assert.False(status.BackupCreated);
        Assert.Equal(AutomaticSaveBackupTrigger.VersionChanged, status.Trigger);
        Assert.Contains("ooc_save_1.sav", status.FailureReason, StringComparison.Ordinal);
        Assert.Equal(stampBefore, files.ReadRaw("/config/modsuite.stamp"));
        Assert.Equal(0, files.StampWrites);
        Assert.Equal(new[] { previousBackup }, files.OwnedBackupDirectories);
        Assert.DoesNotContain(
            files.Directories,
            path => Path.GetFileName(path).Contains(".partial-", StringComparison.Ordinal));
        Assert.False(SuiteStartupAdmission.AllowsRuntime(
            buildCompatibilityAllowsRuntime: true,
            automaticSaveBackup: status));
    }

    [Fact]
    public void RetentionPrunesOnlyExactOwnedAutomaticBackupDirectories()
    {
        var files = NewFileSystem();
        var owned = Enumerable.Range(1, 7)
            .Select(day => "/save/backups/auto-modsuite-backup-202607" +
                           day.ToString("00") +
                           "T000000Z")
            .ToArray();
        foreach (var path in owned) files.AddDirectory(path);
        files.AddDirectory("/save/backups/pre-modsuite-install-20260701T000000Z");
        files.AddDirectory("/save/backups/auto-modsuite-backup-not-a-timestamp");
        files.AddDirectory("/save/backups/auto-modsuite-backup-20261399T999999Z");
        files.AddDirectory("/save/backups/auto-modsuite-backup-20260701T000000Z.partial-abcd");
        files.AddFile(
            "/config/modsuite.stamp",
            AutomaticSaveBackupStampCodec.Encode(new AutomaticSaveBackupStamp(
                Version,
                "/save",
                owned[^1],
                fileCount: 1)));

        var status = Run(files, FirstRun);

        Assert.True(status.AllowsAutomation);
        Assert.Equal(2, status.PrunedBackupCount);
        Assert.Equal(AutomaticSaveBackup.RetainedBackups, files.OwnedBackupDirectories.Count);
        Assert.DoesNotContain(owned[0], files.Directories);
        Assert.DoesNotContain(owned[1], files.Directories);
        Assert.Contains("/save/backups/pre-modsuite-install-20260701T000000Z", files.Directories);
        Assert.Contains("/save/backups/auto-modsuite-backup-not-a-timestamp", files.Directories);
        Assert.Contains("/save/backups/auto-modsuite-backup-20261399T999999Z", files.Directories);
        Assert.Contains(
            "/save/backups/auto-modsuite-backup-20260701T000000Z.partial-abcd",
            files.Directories);
        Assert.All(
            files.DeletedDirectories,
            path => Assert.True(AutomaticSaveBackup.IsOwnedBackupName(Path.GetFileName(path))));
    }

    [Fact]
    public void CorruptStampIsTreatedAsAChangeAndReplacedAfterSuccess()
    {
        var files = NewFileSystem();
        files.AddFile("/save/ooc_save_0.sav", "safe");
        files.AddFile("/config/modsuite.stamp", "not a stamp");

        var status = Run(files, FirstRun);

        Assert.True(status.AllowsAutomation);
        Assert.True(status.BackupCreated);
        Assert.Equal(AutomaticSaveBackupTrigger.CorruptStamp, status.Trigger);
        Assert.True(AutomaticSaveBackupStampCodec.TryDecode(
            files.ReadRaw("/config/modsuite.stamp"),
            out var stamp));
        Assert.Equal(Version, stamp.SuiteVersion);
        Assert.Equal(status.BackupPath, stamp.BackupPath);
    }

    [Fact]
    public void ChangingTheUnitySaveRootCreatesANewContextBackup()
    {
        var files = NewFileSystem();
        files.AddDirectory("/other-save");
        files.AddFile("/save/ooc_save_0.sav", "old context");
        files.AddFile("/other-save/ooc_save_0.sav", "new context");
        var first = Run(files, FirstRun);
        Assert.True(first.AllowsAutomation);

        var status = AutomaticSaveBackup.Run(
            Version,
            "/other-save",
            "/config/modsuite.stamp",
            FirstRun.AddMinutes(1),
            files);

        Assert.True(status.AllowsAutomation);
        Assert.True(status.BackupCreated);
        Assert.Equal(AutomaticSaveBackupTrigger.SaveRootChanged, status.Trigger);
        Assert.StartsWith("/other-save/backups/", status.BackupPath, StringComparison.Ordinal);
        Assert.Equal("new context", files.ReadText(status.BackupPath + "/ooc_save_0.sav"));
    }

    [Fact]
    public void RetentionFailureIsLoudButDoesNotBlockTheNewBackup()
    {
        var files = NewFileSystem();
        files.AddFile("/save/ooc_save_0.sav", "safe");
        for (var day = 1; day <= AutomaticSaveBackup.RetainedBackups; day++)
        {
            files.AddDirectory(
                "/save/backups/auto-modsuite-backup-202607" +
                day.ToString("00") +
                "T000000Z");
        }
        files.RefuseDeletePath = "/save/backups/auto-modsuite-backup-20260701T000000Z";

        var status = Run(files, FirstRun);

        Assert.True(status.AllowsAutomation);
        Assert.True(status.BackupCreated);
        Assert.True(status.HasRetentionFailure);
        Assert.Contains("20260701T000000Z", status.RetentionFailures[0], StringComparison.Ordinal);
        Assert.True(files.DirectoryExists(status.BackupPath));
        Assert.True(files.FileExists("/config/modsuite.stamp"));
        Assert.Equal(AutomaticSaveBackup.RetainedBackups, files.OwnedBackupDirectories.Count);
        Assert.DoesNotContain(
            "/save/backups/auto-modsuite-backup-20260702T000000Z",
            files.Directories);
    }

    private static FakeAutomaticSaveBackupFileSystem NewFileSystem()
    {
        var files = new FakeAutomaticSaveBackupFileSystem("/save");
        files.AddDirectory("/config");
        return files;
    }

    private static AutomaticSaveBackupStatus Run(
        FakeAutomaticSaveBackupFileSystem files,
        DateTime utcNow) =>
        AutomaticSaveBackup.Run(
            Version,
            "/save",
            "/config/modsuite.stamp",
            utcNow,
            files);

    private sealed class FakeAutomaticSaveBackupFileSystem : IAutomaticSaveBackupFileSystem
    {
        private readonly string _sourceRoot;
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);
        private readonly HashSet<string> _directories = new(StringComparer.Ordinal);
        private int _sourceReads;

        internal FakeAutomaticSaveBackupFileSystem(string sourceRoot)
        {
            _sourceRoot = Normalize(sourceRoot);
            AddDirectory(_sourceRoot);
        }

        internal int? RefuseSourceReadOrdinal { get; set; }
        internal string? RefuseDeletePath { get; set; }
        internal int StampWrites { get; private set; }
        internal int NewFileWrites { get; private set; }
        internal IReadOnlyCollection<string> Directories => _directories;
        internal List<string> DeletedDirectories { get; } = new();
        internal IReadOnlyList<string> OwnedBackupDirectories => _directories
            .Where(path => string.Equals(
                Path.GetDirectoryName(path),
                _sourceRoot + "/backups",
                StringComparison.Ordinal))
            .Where(path => AutomaticSaveBackup.IsOwnedBackupName(Path.GetFileName(path)))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        public bool DirectoryExists(string path) => _directories.Contains(Normalize(path));
        public bool FileExists(string path) => _files.ContainsKey(Normalize(path));

        public IReadOnlyList<string> EnumerateFiles(string path)
        {
            var directory = Normalize(path);
            return _files.Keys
                .Where(candidate => string.Equals(
                    Path.GetDirectoryName(candidate),
                    directory,
                    StringComparison.Ordinal))
                .OrderBy(candidate => candidate, StringComparer.Ordinal)
                .ToArray();
        }

        public IReadOnlyList<string> EnumerateDirectories(string path)
        {
            var directory = Normalize(path);
            return _directories
                .Where(candidate => !string.Equals(candidate, directory, StringComparison.Ordinal))
                .Where(candidate => string.Equals(
                    Path.GetDirectoryName(candidate),
                    directory,
                    StringComparison.Ordinal))
                .OrderBy(candidate => candidate, StringComparer.Ordinal)
                .ToArray();
        }

        public void CreateDirectory(string path) => AddDirectory(path);

        public byte[] ReadAllBytes(string path)
        {
            var normalized = Normalize(path);
            if (string.Equals(Path.GetDirectoryName(normalized), _sourceRoot, StringComparison.Ordinal) &&
                (normalized.EndsWith(".sav", StringComparison.OrdinalIgnoreCase) ||
                 normalized.EndsWith("steam_autocloud.vdf", StringComparison.OrdinalIgnoreCase)))
            {
                _sourceReads++;
                if (RefuseSourceReadOrdinal == _sourceReads)
                    throw new IOException("simulated source read refusal");
            }
            return _files[normalized].ToArray();
        }

        public void WriteNewFile(string path, byte[] contents)
        {
            var normalized = Normalize(path);
            if (_files.ContainsKey(normalized)) throw new IOException("simulated file collision");
            AddDirectory(Path.GetDirectoryName(normalized)!);
            _files.Add(normalized, contents.ToArray());
            NewFileWrites++;
        }

        public void WriteStamp(string path, byte[] contents)
        {
            var normalized = Normalize(path);
            AddDirectory(Path.GetDirectoryName(normalized)!);
            _files[normalized] = contents.ToArray();
            StampWrites++;
        }

        public void MoveDirectory(string sourcePath, string destinationPath)
        {
            var source = Normalize(sourcePath);
            var destination = Normalize(destinationPath);
            if (!_directories.Contains(source)) throw new DirectoryNotFoundException(source);
            if (_directories.Contains(destination)) throw new IOException("simulated directory collision");
            var directoryMoves = _directories
                .Where(path => IsAtOrBelow(path, source))
                .Select(path => (Old: path, New: destination + path.Substring(source.Length)))
                .ToArray();
            var fileMoves = _files
                .Where(pair => IsAtOrBelow(pair.Key, source))
                .Select(pair => (Old: pair.Key, New: destination + pair.Key.Substring(source.Length), pair.Value))
                .ToArray();
            foreach (var move in directoryMoves) _directories.Remove(move.Old);
            foreach (var move in fileMoves) _files.Remove(move.Old);
            foreach (var move in directoryMoves) _directories.Add(move.New);
            foreach (var move in fileMoves) _files.Add(move.New, move.Value);
        }

        public void DeleteDirectory(string path)
        {
            var normalized = Normalize(path);
            if (string.Equals(normalized, RefuseDeletePath, StringComparison.Ordinal))
                throw new IOException("simulated retention refusal");
            foreach (var file in _files.Keys.Where(key => IsAtOrBelow(key, normalized)).ToArray())
                _files.Remove(file);
            foreach (var directory in _directories.Where(key => IsAtOrBelow(key, normalized)).ToArray())
                _directories.Remove(directory);
            DeletedDirectories.Add(normalized);
        }

        internal void AddDirectory(string path)
        {
            var current = Normalize(path);
            while (current.Length > 1)
            {
                _directories.Add(current);
                current = Path.GetDirectoryName(current) ?? "/";
            }
            _directories.Add("/");
        }

        internal void AddFile(string path, string contents, bool overwrite = false) =>
            AddFile(path, Encoding.UTF8.GetBytes(contents), overwrite);

        internal void AddFile(string path, byte[] contents, bool overwrite = false)
        {
            var normalized = Normalize(path);
            AddDirectory(Path.GetDirectoryName(normalized)!);
            if (!overwrite && _files.ContainsKey(normalized))
                throw new InvalidOperationException("The fake file already exists: " + normalized);
            _files[normalized] = contents.ToArray();
        }

        internal string ReadText(string path) => Encoding.UTF8.GetString(ReadRaw(path));
        internal byte[] ReadRaw(string path) => _files[Normalize(path)].ToArray();

        private static bool IsAtOrBelow(string path, string directory) =>
            string.Equals(path, directory, StringComparison.Ordinal) ||
            path.StartsWith(directory + "/", StringComparison.Ordinal);

        private static string Normalize(string path) =>
            Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
