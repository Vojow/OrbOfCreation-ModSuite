using System;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using BepInEx.Configuration;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

/// <summary>
/// The player-facing trigger for verification: press a key, the suite checks itself against the
/// running game, and reports one verdict per thing checked.
/// </summary>
/// <remarks>
/// <para>
/// Bound directly on the plugin's <see cref="ConfigFile"/> rather than threaded through
/// <c>SuiteRuntimeConfiguration</c>, because this is a diagnostic action, not runtime policy. Keeping it
/// out of the canonical configuration record means the schema's steps and their tests stay about
/// gameplay behaviour: the schema version says when this file was written, and this class decides what
/// that means for its own key, because a migration step may only write keys the transaction binds.
/// </para>
/// <para>
/// Passes still report separately — "cost passed, rate failed" is immediately actionable where a
/// single combined verdict would not be — but they all run to completion inside the frame the key was
/// pressed in. Spreading the work across ticks was the earlier design and was wrong for a manual
/// diagnostic twice over: it left every pass reading a different frame's game state, and it hid the
/// run. <b>The stall is the acknowledgement.</b> A player who presses the key and sees the game hitch
/// knows it happened, without needing to go and read a log to find out.
/// </para>
/// <para>
/// Nothing here is bounded any more, because nothing here needs to be: the entity budget existed to
/// cap per-frame cost, and there is now exactly one frame. Every entity in every registry is checked.
/// </para>
/// <para>
/// The default chord takes two modifiers and a main key nothing else in the suite uses, because
/// pressing it costs the player a frozen frame and nobody should hit it by accident. Both earlier
/// defaults were built on M, which is Mentor's toggle key: BepInEx vetoes a shortcut whenever any key
/// outside it is held, so the three-modifier chord never fired at all, and the Left Alt + M it
/// replaced fired Mentor and a frozen frame together.
/// </para>
/// </remarks>
internal sealed class AutomataDifferentialVerificationControl
{
    /// <summary>
    /// The schema version this file gets once the chord has moved. A configuration written before it
    /// may still carry a superseded default, and is the only one this rebinds.
    /// </summary>
    internal const int RechordSchemaVersion = 2;

    private static readonly KeyboardShortcut DefaultShortcut = new(
        UnityEngine.KeyCode.Y,
        UnityEngine.KeyCode.LeftControl,
        UnityEngine.KeyCode.LeftAlt);

    /// <summary>
    /// Every chord this diagnostic has ever defaulted to before the current one. A persisted value
    /// matching one of these was never chosen, it was inherited, and both collide with Mentor's
    /// toggle on M.
    /// </summary>
    private static readonly KeyboardShortcut[] SupersededDefaults =
    {
        new(UnityEngine.KeyCode.M, UnityEngine.KeyCode.LeftAlt),
        new(
            UnityEngine.KeyCode.M,
            UnityEngine.KeyCode.LeftControl,
            UnityEngine.KeyCode.LeftShift,
            UnityEngine.KeyCode.LeftAlt),
    };

    private readonly ConfigEntry<KeyboardShortcut> _shortcut;
    private readonly Action<string> _report;

    /// <param name="rebindSupersededDefault">
    /// Whether this launch read a configuration written before the chord moved. Changing a code
    /// default does nothing to a file that already carries the old one, so the one launch that
    /// migrates the file is also the one launch allowed to rewrite the value — and only if the value
    /// is a default the player never chose.
    /// </param>
    internal AutomataDifferentialVerificationControl(
        ConfigFile config,
        Action<string> report,
        bool rebindSupersededDefault)
    {
        if (config is null) throw new ArgumentNullException(nameof(config));
        _report = report ?? throw new ArgumentNullException(nameof(report));

        _shortcut = config.Bind(
            "Diagnostics",
            "VerifyGameMathShortcut",
            DefaultShortcut,
            "Press to check the suite against the running game: its ported math against the game's " +
            "own results, and its world collection against the game's own accessors. Runs everything " +
            "in one frame, so the game will visibly hitch — that hitch is how you know it ran. " +
            "Diagnostic only; it changes nothing in game. Default: Left Ctrl + Left Alt + Y.");
        if (rebindSupersededDefault && IsSupersededDefault(_shortcut.Value)) RebindToDefault();
    }

    /// <summary>
    /// Whether the configuration this launch bound predates the chord move, and so may still carry a
    /// default the player never chose.
    /// </summary>
    internal static bool ShouldRebindSupersededDefault(in ConfigurationSchemaStatus status) =>
        status.State == ConfigurationSchemaState.Migrated &&
        status.FromVersion < RechordSchemaVersion;

    internal static bool IsSupersededDefault(KeyboardShortcut value)
    {
        for (var index = 0; index < SupersededDefaults.Length; index++)
        {
            if (SameChord(value, SupersededDefaults[index])) return true;
        }
        return false;
    }

    private void RebindToDefault()
    {
        _shortcut.Value = DefaultShortcut;
        _report(
            "Differential verification was still bound to a superseded default built on M, which is " +
            "Mentor's toggle key. It has been rebound to Left Ctrl + Left Alt + Y; change it in the " +
            "configuration UI if you want it elsewhere.");
    }

    /// <summary>
    /// Compares main key and modifier set without depending on how a shortcut orders its modifiers.
    /// </summary>
    private static bool SameChord(KeyboardShortcut left, KeyboardShortcut right)
    {
        if (left.MainKey != right.MainKey) return false;
        var leftModifiers = 0;
        foreach (var modifier in left.Modifiers)
        {
            leftModifiers++;
            var found = false;
            foreach (var candidate in right.Modifiers)
            {
                if (candidate != modifier) continue;
                found = true;
                break;
            }
            if (!found) return false;
        }
        var rightModifiers = 0;
        foreach (var _ in right.Modifiers) rightModifiers++;
        return leftModifiers == rightModifiers;
    }

    /// <summary>Drives one frame. Runs everything when the shortcut is pressed.</summary>
    internal void Tick()
    {
        if (_shortcut.Value.IsDown()) RunEverything();
    }

    private void RunEverything()
    {
        _report("Verification started. Everything runs in this frame, so the game will hitch.");

        var whole = Stopwatch.StartNew();

        // Order matters, and only in this direction. The world check is a pure reader, while both
        // ported-math passes deliberately settle dirty flags first so that the two sides compare the
        // same inputs — which leaves every record they touched freshly recalculated. Running the
        // check afterwards would have it survey a cache the verifier had just warmed, and report a
        // staleness figure that says more about the verifier than about the game.
        RunWorldCollectionCheck();
        RunPass(new CostPass());
        RunPass(new RatePass());
        RunPass(new RequirementPass("Upgrade requirement", "UpgradeSO", isUpgrade: true));
        RunPass(new RequirementPass("Structure requirement", "StructureSO", isUpgrade: false));

        whole.Stop();
        _report($"Verification finished in {whole.Elapsed.TotalMilliseconds:0.###} ms.");
    }

    /// <summary>Runs one ported-math pass over every entity it can reach, then reports its verdict.</summary>
    private void RunPass(IVerificationPass pass)
    {
        if (!pass.TryBegin(out var entities, out var failure))
        {
            _report($"{pass.Subject} verification unavailable: {failure}");
            return;
        }

        // One tick, no entity ceiling: the budgets existed to spread work across frames, and there is
        // no longer anything to spread it across.
        var session = new DifferentialVerificationSession(
            pass.Subject, tickBudget: 1, entityBudget: int.MaxValue);
        session.Start();

        for (var index = 0; index < entities.Count; index++)
        {
            var entity = entities[index];
            if (entity is null)
            {
                session.RecordUnverifiable("a registry entry was null");
                continue;
            }

            if (pass.TryVerify(entity, session.Run, session, out var entityFailure))
            {
                session.RecordVerified();
            }
            else
            {
                session.RecordUnverifiable(entityFailure);
            }
        }

        session.EndTick();
        _report(session.Complete());
    }

    /// <summary>
    /// Checks world collection itself — binding, traversal, identity, edges, accessor parity, and
    /// cache warmth — against the live game. Reports several lines rather than one verdict, because
    /// the answers are measurements as much as they are pass or fail.
    /// </summary>
    private void RunWorldCollectionCheck()
    {
        try
        {
            foreach (var line in new AutomataWorldCollectionCheck().Run()) _report(line);
        }
        catch (Exception ex)
        {
            // A throw here is itself the finding — the collector reached something on a live object
            // that no stub reproduces — so it is reported rather than allowed to take down the frame
            // the player is standing in.
            _report($"World collection check threw: {ex.GetBaseException().Message}");
        }
    }

    /// <summary>One ported chain to check, its entity source, and how to compare a single entity.</summary>
    private interface IVerificationPass
    {
        string Subject { get; }

        bool TryBegin(out IList entities, out string failure);

        bool TryVerify(
            object entity,
            DifferentialRun run,
            DifferentialVerificationSession session,
            out string failure);
    }

    private sealed class CostPass : IVerificationPass
    {
        private AutomataCostVerifier? _verifier;

        public string Subject => "Cost";

        public bool TryBegin(out IList entities, out string failure)
        {
            entities = Array.Empty<object>();

            var structureType = FindType("StructureSO");
            if (structureType is null)
            {
                failure = "the StructureSO type could not be resolved.";
                return false;
            }

            _verifier = new AutomataCostVerifier(structureType);
            if (!_verifier.IsAvailable)
            {
                failure = "this build does not expose the expected cost contract.";
                return false;
            }

            var all = ReadStaticList(structureType, "All");
            if (all is null || all.Count == 0)
            {
                failure = "no structures were available. Load a save first.";
                return false;
            }

            entities = all;
            failure = string.Empty;
            return true;
        }

        public bool TryVerify(
            object entity,
            DifferentialRun run,
            DifferentialVerificationSession session,
            out string failure)
        {
            if (_verifier is null)
            {
                failure = "the cost verifier was not started.";
                return false;
            }

            return _verifier.TryVerify(entity, run, session, out failure);
        }
    }

    private sealed class RatePass : IVerificationPass
    {
        private AutomataRateVerifier? _verifier;

        public string Subject => "Rate";

        public bool TryBegin(out IList entities, out string failure)
        {
            entities = Array.Empty<object>();

            var resourceType = FindType("ResourceSO");
            if (resourceType is null)
            {
                failure = "the ResourceSO type could not be resolved.";
                return false;
            }

            _verifier = new AutomataRateVerifier(resourceType, FindType("Player"));
            if (!_verifier.IsAvailable)
            {
                failure = "this build does not expose the expected rate contract.";
                return false;
            }

            var all = ReadStaticList(resourceType, "All");
            if (all is null || all.Count == 0)
            {
                failure = "no resources were available. Load a save first.";
                return false;
            }

            entities = all;
            failure = string.Empty;
            return true;
        }

        public bool TryVerify(
            object entity,
            DifferentialRun run,
            DifferentialVerificationSession session,
            out string failure)
        {
            if (_verifier is null)
            {
                failure = "the rate verifier was not started.";
                return false;
            }

            return _verifier.TryVerify(entity, run, session, out failure);
        }
    }

    /// <summary>
    /// Checks the suite's own answer to "may this be bought at its next level" against the game's, for
    /// one kind of owner.
    /// </summary>
    /// <remarks>
    /// Two passes rather than one because the two owner kinds are checked at different levels and a
    /// combined verdict would hide which of the two disagreed — and because the level expressions are
    /// the likeliest thing to be wrong.
    /// <para>
    /// Its own collector, deliberately. The requirement rows are read once per lifecycle epoch, so a
    /// collector that has already run would skip the read this pass exists to check; a fresh one reads
    /// them for the first time, which is the state a real session's first cycle is in.
    /// </para>
    /// </remarks>
    private sealed class RequirementPass : IVerificationPass
    {
        private readonly string _typeName;
        private readonly bool _isUpgrade;
        private AutomataRequirementVerifier? _verifier;
        private GameWorldState? _world;

        internal RequirementPass(string subject, string typeName, bool isUpgrade)
        {
            Subject = subject;
            _typeName = typeName;
            _isUpgrade = isUpgrade;
        }

        public string Subject { get; }

        public bool TryBegin(out IList entities, out string failure)
        {
            entities = Array.Empty<object>();

            var ownerType = FindType(_typeName);
            if (ownerType is null)
            {
                failure = $"the {_typeName} type could not be resolved.";
                return false;
            }

            _verifier = new AutomataRequirementVerifier(ownerType, _isUpgrade);
            if (!_verifier.IsAvailable)
            {
                failure = "this build does not expose the expected per-level prerequisite contract.";
                return false;
            }

            var all = ReadStaticList(ownerType, "All");
            if (all is null || all.Count == 0)
            {
                failure = $"no {_typeName} entities were available. Load a save first.";
                return false;
            }

            var collector = new GameWorldCollector();
            collector.Collect();
            _world = collector.Build();

            entities = all;
            failure = string.Empty;
            return true;
        }

        public bool TryVerify(
            object entity,
            DifferentialRun run,
            DifferentialVerificationSession session,
            out string failure)
        {
            if (_verifier is null || _world is null)
            {
                failure = "the requirement verifier was not started.";
                return false;
            }

            return _verifier.TryVerify(entity, _world, run, out failure);
        }
    }

    private static Type? FindType(string name)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var candidate = assembly.GetType(name, throwOnError: false);
            if (candidate is not null) return candidate;
        }

        return null;
    }

    /// <summary>
    /// Reads a public static list member, the same discovery mechanism Auto Buy already uses for
    /// candidate enumeration.
    /// </summary>
    private static IList? ReadStaticList(Type type, string memberName)
    {
        const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var value = type.GetField(memberName, Static)?.GetValue(null) ??
            type.GetProperty(memberName, Static)?.GetValue(null, null);
        return value as IList;
    }
}
