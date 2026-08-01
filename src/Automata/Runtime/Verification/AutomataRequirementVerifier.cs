using System;
using System.Reflection;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

/// <summary>
/// Compares the admission verdict the suite derives from the snapshot against the verdict the game's
/// own per-level prerequisite container gives, for real entities in a live session.
/// </summary>
/// <remarks>
/// <para>
/// The offline tests prove the port agrees with values read out of the decompiled source, which is
/// exactly the check a misreading survives: the same misreading would sit in the port and in its
/// expected value. <c>prerequisitesPerLevel.Check(level)</c> is the only oracle that cannot be talked
/// into agreeing with us. It is also the reason the snapshot models conditions at all — Auto Buy
/// planned a purchase the game then refused, and no published fact said why.
/// </para>
/// <para>
/// <b>The oracle is safe to call.</b> The parameterised overload takes the level as an argument and
/// walks the conditions; unlike the no-argument one it neither stamps a game id nor latches
/// <c>available</c>. That difference is the whole reason this verification is possible without the
/// verifier changing the state it is verifying.
/// </para>
/// <para>
/// The level is read from the live entity rather than from the snapshot, so a disagreement means the
/// conditions were evaluated differently rather than that the two sides were asked different
/// questions. Both the level expressions reproduced here are the game's own, and they are not the
/// same shape: an upgrade asks about the level it is about to reach, a structure about the quantity it
/// already has.
/// </para>
/// <para>
/// An entity whose conditions the suite cannot evaluate is unverifiable rather than a mismatch. That
/// is not leniency: an unevaluable verdict already refuses the purchase, so the planner is safe
/// either way, and counting it as a disagreement would drown the signal this pass exists to give in
/// noise from condition classes nobody has modelled yet. The session reports it as an incomplete run
/// and names the first one.
/// </para>
/// </remarks>
internal sealed class AutomataRequirementVerifier
{
    private readonly RequirementContract? _contract;

    internal AutomataRequirementVerifier(Type ownerType, bool isUpgrade)
    {
        _contract = RequirementContract.TryResolve(ownerType, isUpgrade);
    }

    /// <summary>Whether the native members needed to reach the oracle at all were resolved.</summary>
    internal bool IsAvailable => _contract is not null;

    /// <summary>
    /// Verifies one entity. Returns false when the entity could not be read or its conditions could
    /// not be evaluated, which is distinct from the two sides disagreeing.
    /// </summary>
    internal bool TryVerify(
        object entity,
        GameWorldState world,
        DifferentialRun run,
        out string failure)
    {
        if (entity is null) throw new ArgumentNullException(nameof(entity));
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (run is null) throw new ArgumentNullException(nameof(run));

        if (_contract is null)
        {
            failure = "The per-level prerequisite contract is unavailable on this build.";
            return false;
        }

        try
        {
            return TryVerifyCore(_contract, entity, world, run, out failure);
        }
        catch (Exception ex)
        {
            failure = $"Reading the entity's per-level prerequisites threw: {ex.GetBaseException().Message}";
            return false;
        }
    }

    private static bool TryVerifyCore(
        RequirementContract contract,
        object entity,
        GameWorldState world,
        DifferentialRun run,
        out string failure)
    {
        var entityId = contract.ReadGuid(entity);
        var level = contract.ReadCheckLevel(entity);

        if (!TryNameUnevaluable(world, entityId, level, out var unevaluable))
        {
            failure = $"the {unevaluable} condition on {entityId} is not modelled.";
            return false;
        }

        var ours = WorldRequirementEvaluator.Evaluate(world, entityId, level);
        var theirs = contract.InvokeCheck(entity, level);

        run.Compare(
            entityId,
            $"requirements@{level}",
            ours == WorldRequirementVerdict.Met ? BigDouble.One : BigDouble.Zero,
            theirs ? BigDouble.One : BigDouble.Zero);

        failure = string.Empty;
        return true;
    }

    /// <summary>
    /// Whether every published condition on this owner could be evaluated, naming the first that
    /// could not.
    /// </summary>
    /// <remarks>
    /// Walked here rather than inside the evaluator so that the evaluator stays a predicate. What a
    /// consumer needs is the verdict; what an operator reading this pass needs is the class name that
    /// produced it, and only the verifier needs both.
    /// </remarks>
    private static bool TryNameUnevaluable(
        GameWorldState world,
        Guid ownerId,
        long level,
        out string conditionTypeName)
    {
        conditionTypeName = string.Empty;
        if (!WorldEntityRequirementLookup.TryFindRange(
                world.EntityRequirements, ownerId, out var start, out var count))
        {
            return true;
        }

        var rows = world.EntityRequirements.AsSpan();
        for (var offset = 0; offset < count; offset++)
        {
            ref readonly var row = ref rows[start + offset];
            if (row.NodeKind == WorldRequirementNodeKind.Group) continue;
            if (WorldRequirementEvaluator.Evaluate(world, in row, level) !=
                WorldRequirementVerdict.Unevaluable)
            {
                continue;
            }

            conditionTypeName = row.ConditionTypeName.Length == 0 ? "unnamed" : row.ConditionTypeName;
            return false;
        }

        return true;
    }

    /// <summary>
    /// The reflected members needed to ask the game its own per-level answer. Resolved once; a missing
    /// member makes the whole verifier unavailable rather than partial.
    /// </summary>
    private sealed class RequirementContract
    {
        private const BindingFlags Instance =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private readonly FieldInfo _container;
        private readonly MethodInfo _check;
        private readonly ConstructorInfo _conditionInfo;
        private readonly MethodInfo _getGuid;
        private readonly FieldInfo _level;
        private readonly FieldInfo? _queuedLevels;

        private RequirementContract(
            FieldInfo container,
            MethodInfo check,
            ConstructorInfo conditionInfo,
            MethodInfo getGuid,
            FieldInfo level,
            FieldInfo? queuedLevels)
        {
            _container = container;
            _check = check;
            _conditionInfo = conditionInfo;
            _getGuid = getGuid;
            _level = level;
            _queuedLevels = queuedLevels;
        }

        internal static RequirementContract? TryResolve(Type ownerType, bool isUpgrade)
        {
            if (ownerType is null) return null;

            var container = ownerType.GetField("prerequisitesPerLevel", Instance);
            var getGuid = ownerType.GetMethod("GetGuid", Instance, null, Type.EmptyTypes, null);
            if (container is null || getGuid is null || getGuid.ReturnType != typeof(Guid)) return null;

            // An upgrade counts what it is about to reach, a structure what it already has, so the two
            // read different fields and only one of them has levels in flight to add.
            var level = ownerType.GetField(isUpgrade ? "level" : "quantity", Instance);
            var queuedLevels = isUpgrade ? ownerType.GetField("queuedLevels", Instance) : null;
            if (level is null || (isUpgrade && queuedLevels is null)) return null;

            // The parameter type is taken from the overload rather than resolved by name, so the
            // verifier cannot bind to a same-named type from somewhere else in the domain.
            var check = FindParameterisedCheck(container.FieldType);
            if (check is null) return null;

            var conditionInfo = check.GetParameters()[0].ParameterType
                .GetConstructor(new[] { typeof(long) });
            return conditionInfo is null
                ? null
                : new RequirementContract(container, check, conditionInfo, getGuid, level, queuedLevels);
        }

        /// <summary>
        /// The one-argument <c>Check</c>. The no-argument overload of the same name latches and stamps,
        /// so picking by name alone would turn this verifier into a mutation.
        /// </summary>
        private static MethodInfo? FindParameterisedCheck(Type containerType)
        {
            foreach (var candidate in containerType.GetMethods(Instance))
            {
                if (candidate.Name != "Check" || candidate.ReturnType != typeof(bool)) continue;

                var parameters = candidate.GetParameters();
                if (parameters.Length == 1 && parameters[0].ParameterType.IsValueType) return candidate;
            }

            return null;
        }

        internal Guid ReadGuid(object entity) => (Guid)_getGuid.Invoke(entity, null)!;

        /// <summary>
        /// The level the game itself would check at: <c>level + queuedLevels + 1</c> for an upgrade's
        /// <c>HasMetQueuedLevelRequirements()</c>, and the bare <c>quantity</c> for a structure's
        /// <c>HasMetLevelRequirements()</c>.
        /// </summary>
        internal long ReadCheckLevel(object entity)
        {
            var level = Convert.ToInt64(_level.GetValue(entity));
            if (_queuedLevels is null) return level;
            return level + Convert.ToInt64(_queuedLevels.GetValue(entity)) + 1L;
        }

        internal bool InvokeCheck(object entity, long level)
        {
            var container = _container.GetValue(entity);
            if (container is null) return true;

            var info = _conditionInfo.Invoke(new object[] { level });
            return (bool)_check.Invoke(container, new[] { info })!;
        }
    }
}
