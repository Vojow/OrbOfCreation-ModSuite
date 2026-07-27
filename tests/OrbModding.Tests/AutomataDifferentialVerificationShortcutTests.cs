using System.Collections.Generic;
using BepInEx.Configuration;
using OrbAutomata;
using OrbModding.Common;
using Xunit;
using KeyCode = UnityEngine.KeyCode;

namespace OrbModding.Tests;

/// <summary>
/// Changing a code default does nothing to a configuration file that already carries the old one, so
/// the chord move has to reach the persisted value too — without overwriting a chord the player
/// actually chose.
/// </summary>
public sealed class AutomataDifferentialVerificationShortcutTests
{
    private const string Section = "Diagnostics";
    private const string Key = "VerifyGameMathShortcut";

    private static readonly KeyboardShortcut CurrentDefault = new(
        KeyCode.Y, KeyCode.LeftControl, KeyCode.LeftAlt);
    private static readonly KeyboardShortcut MentorCollidingDefault = new(
        KeyCode.M, KeyCode.LeftAlt);
    private static readonly KeyboardShortcut ThreeModifierDefault = new(
        KeyCode.M, KeyCode.LeftControl, KeyCode.LeftShift, KeyCode.LeftAlt);
    private static readonly KeyboardShortcut ChosenByThePlayer = new(
        KeyCode.J, KeyCode.LeftShift);

    [Fact]
    public void ThePersistedChordThatFiresMentorTooIsRebound()
    {
        var file = new ConfigFile();
        var entry = Seed(file, MentorCollidingDefault);
        var reports = new List<string>();

        _ = new AutomataDifferentialVerificationControl(file, reports.Add, true);

        Assert.Equal(CurrentDefault, entry.Value);
        Assert.Single(reports);
    }

    [Fact]
    public void TheThreeModifierDefaultThatNeverFiredIsAlsoRebound()
    {
        var file = new ConfigFile();
        var entry = Seed(file, ThreeModifierDefault);

        _ = new AutomataDifferentialVerificationControl(file, _ => { }, true);

        Assert.Equal(CurrentDefault, entry.Value);
    }

    [Fact]
    public void AChordThePlayerChoseSurvives()
    {
        var file = new ConfigFile();
        var entry = Seed(file, ChosenByThePlayer);
        var reports = new List<string>();

        _ = new AutomataDifferentialVerificationControl(file, reports.Add, true);

        Assert.Equal(ChosenByThePlayer, entry.Value);
        Assert.Empty(reports);
    }

    /// <summary>
    /// A file already carrying the current schema has had its one chance to be rebound. Whatever it
    /// holds now was kept deliberately, even if it happens to read like an old default.
    /// </summary>
    [Fact]
    public void AConfigurationWrittenAfterTheChordMovedIsLeftAlone()
    {
        var file = new ConfigFile();
        var entry = Seed(file, MentorCollidingDefault);

        _ = new AutomataDifferentialVerificationControl(file, _ => { }, false);

        Assert.Equal(MentorCollidingDefault, entry.Value);
    }

    [Fact]
    public void AFreshConfigurationBindsTheCurrentChord()
    {
        var file = new ConfigFile();

        _ = new AutomataDifferentialVerificationControl(file, _ => { }, true);

        Assert.Equal(CurrentDefault, Seed(file, MentorCollidingDefault).Value);
    }

    [Theory]
    [InlineData((int)ConfigurationSchemaState.Migrated, 0, true)]
    [InlineData((int)ConfigurationSchemaState.Migrated, 1, true)]
    [InlineData((int)ConfigurationSchemaState.Migrated, 2, false)]
    [InlineData((int)ConfigurationSchemaState.Current, 2, false)]
    [InlineData((int)ConfigurationSchemaState.Failed, 1, false)]
    public void OnlyTheLaunchThatMigratesAnOlderFileMayRebindIt(
        int state,
        int fromVersion,
        bool expected)
    {
        var status = new ConfigurationSchemaStatus(
            PluginIds.SuiteGuid,
            (ConfigurationSchemaState)state,
            fromVersion,
            AutomataDifferentialVerificationControl.RechordSchemaVersion,
            saved: true,
            loaded: true,
            reason: "test",
            backupCreated: false);

        Assert.Equal(
            expected,
            AutomataDifferentialVerificationControl.ShouldRebindSupersededDefault(status));
    }

    /// <summary>
    /// Binding the same definition returns the entry already bound, so this both seeds a persisted
    /// value before the control reads it and reads back what the control left behind.
    /// </summary>
    private static ConfigEntry<KeyboardShortcut> Seed(ConfigFile file, KeyboardShortcut value) =>
        file.Bind(Section, Key, value, "Seeded by the test.");
}
