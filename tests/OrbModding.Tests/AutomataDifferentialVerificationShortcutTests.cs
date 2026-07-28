using System.Linq;
using BepInEx.Configuration;
using OrbAutomata;
using OrbModding;
using OrbModding.Common;
using Xunit;
using KeyCode = UnityEngine.KeyCode;

namespace OrbModding.Tests;

public sealed class AutomataDifferentialVerificationShortcutTests
{
    private const string AutoCastSection = "AutoCast";
    private const string AutoCastKey = "ToggleShortcut";
    private const string VerificationSection = "Diagnostics";
    private const string VerificationKey = "VerifyGameMathShortcut";

    [Fact]
    public void SchemaThreeMigratesOnlyInheritedShortcutDefaults()
    {
        var file = VersionTwoFile(
            "X + LeftAlt",
            "Y + LeftControl + LeftAlt");

        var result = SuiteConfiguration.TryBind(file);

        Assert.True(result.Success, result.Status.Reason);
        Assert.Equal(ConfigurationSchemaState.Migrated, result.Status.State);
        Assert.Equal(2, result.Status.FromVersion);
        Assert.Equal(3, result.Status.ToVersion);
        Assert.Equal(KeyCode.F8, result.Config!.Automata.AutoCastToggleShortcut.Value.MainKey);
        Assert.Equal(KeyCode.None, ReadVerificationShortcut(file).MainKey);
        Assert.Equal(2, result.Diagnostics.Count);
    }

    [Fact]
    public void SchemaThreePreservesPlayerCustomizedChords()
    {
        var file = VersionTwoFile(
            "F7",
            "J + LeftShift");

        var result = SuiteConfiguration.TryBind(file);

        Assert.True(result.Success, result.Status.Reason);
        Assert.Equal(KeyCode.F7, result.Config!.Automata.AutoCastToggleShortcut.Value.MainKey);
        var verifier = ReadVerificationShortcut(file);
        Assert.Equal(KeyCode.J, verifier.MainKey);
        Assert.Contains(KeyCode.LeftShift, verifier.Modifiers);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void CurrentSchemaDoesNotReinterpretOldLookingPlayerValues()
    {
        var file = new ConfigFile();
        file.SeedSerialized(
            ConfigurationSchemaTransaction.MarkerSection,
            ConfigurationSchemaTransaction.MarkerKey,
            "3");
        file.SeedSerialized(AutoCastSection, AutoCastKey, "X + LeftAlt");
        file.SeedSerialized(
            VerificationSection,
            VerificationKey,
            "Y + LeftControl + LeftAlt");

        var result = SuiteConfiguration.TryBind(file);

        Assert.True(result.Success, result.Status.Reason);
        Assert.Equal(ConfigurationSchemaState.Current, result.Status.State);
        Assert.Equal(KeyCode.X, result.Config!.Automata.AutoCastToggleShortcut.Value.MainKey);
        Assert.Equal(KeyCode.Y, ReadVerificationShortcut(file).MainKey);
    }

    [Fact]
    public void FreshConfigurationUsesF8AndNoVerifierHotkey()
    {
        var file = new ConfigFile();
        var result = SuiteConfiguration.TryBind(file);

        Assert.True(result.Success, result.Status.Reason);
        Assert.Equal(KeyCode.F8, result.Config!.Automata.AutoCastToggleShortcut.Value.MainKey);
        Assert.Equal(KeyCode.None, ReadVerificationShortcut(file).MainKey);
    }

    [Fact]
    public void RuntimeButtonCoalescesRequestsAndRunsOnceOnTick()
    {
        var runs = 0;
        var control = new AutomataDifferentialVerificationControl(_ => { }, () => runs++);

        Assert.True(control.RequestRun());
        Assert.False(control.RequestRun());
        Assert.True(control.RunRequested);
        Assert.Equal(1, control.Revision);

        control.Tick();

        Assert.Equal(1, runs);
        Assert.False(control.RunRequested);
        Assert.Equal(2, control.Revision);
        control.Tick();
        Assert.Equal(1, runs);
    }

    [Fact]
    public void InputInventoryProvesF8IsClearAndVerifierHasNoListener()
    {
        var listeners = SuiteShortcutCollisionValidator.Inventory(
            new KeyboardShortcut(KeyCode.F8),
            new KeyboardShortcut(KeyCode.M, KeyCode.LeftAlt));
        var collisions = SuiteShortcutCollisionValidator.Validate(listeners);

        Assert.Equal(3, listeners.Count);
        Assert.Equal(
            SuiteShortcutListenerKind.PerFrameKeyboardPolling,
            listeners.Single(listener => listener.Id == "auto-cast-toggle").Kind);
        Assert.Equal(
            SuiteShortcutListenerKind.PerFrameKeyboardPolling,
            listeners.Single(listener => listener.Id == "mentor-toggle").Kind);
        var verifier = listeners.Single(listener => listener.Id == "differential-verifier");
        Assert.Equal(SuiteShortcutListenerKind.RuntimePageButton, verifier.Kind);
        Assert.Equal(KeyCode.None, verifier.Shortcut.MainKey);
        Assert.DoesNotContain(
            collisions,
            collision => collision.ListenerId == "auto-cast-toggle");
        var mentorCollision = Assert.Single(
            collisions,
            collision => collision.ListenerId == "mentor-toggle");
        Assert.Equal(KeyCode.LeftAlt, mentorCollision.Key);
        Assert.Equal("More Info", mentorCollision.ConflictingBinding);
        Assert.False(mentorCollision.IsMainKey);
        Assert.False(mentorCollision.IsSuiteListener);
    }

    [Fact]
    public void CollisionValidationFindsExactSuiteDoubleBinds()
    {
        var chord = new KeyboardShortcut(KeyCode.M, KeyCode.LeftAlt);
        var collisions = SuiteShortcutCollisionValidator.Validate(
            SuiteShortcutCollisionValidator.Inventory(chord, chord));

        Assert.Contains(
            collisions,
            collision => collision.IsSuiteListener &&
                         collision.ListenerId == "auto-cast-toggle" &&
                         collision.ConflictingBinding == "Mentor toggle");
    }

    [Fact]
    public void CollisionValidationFindsNativeMainKeysAndHeldModifiers()
    {
        var collisions = SuiteShortcutCollisionValidator.Validate(
            SuiteShortcutCollisionValidator.Inventory(
                new KeyboardShortcut(KeyCode.X, KeyCode.LeftAlt),
                new KeyboardShortcut(KeyCode.M, KeyCode.LeftAlt)));

        Assert.Contains(
            collisions,
            collision => collision.ListenerId == "auto-cast-toggle" &&
                         collision.Key == KeyCode.X &&
                         collision.ConflictingBinding == "Open Inventory" &&
                         collision.IsMainKey);
        Assert.Contains(
            collisions,
            collision => collision.ListenerId == "auto-cast-toggle" &&
                         collision.Key == KeyCode.LeftAlt &&
                         collision.ConflictingBinding == "More Info" &&
                         !collision.IsMainKey);
    }

    private static ConfigFile VersionTwoFile(string autoCast, string verifier)
    {
        var file = new ConfigFile();
        file.SeedSerialized(
            ConfigurationSchemaTransaction.MarkerSection,
            ConfigurationSchemaTransaction.MarkerKey,
            "2");
        file.SeedSerialized(AutoCastSection, AutoCastKey, autoCast);
        file.SeedSerialized(VerificationSection, VerificationKey, verifier);
        return file;
    }

    private static KeyboardShortcut ReadVerificationShortcut(ConfigFile file) =>
        (KeyboardShortcut)file.Single(pair =>
                pair.Key.Section == VerificationSection &&
                pair.Key.Key == VerificationKey)
            .Value.BoxedValue!;
}
