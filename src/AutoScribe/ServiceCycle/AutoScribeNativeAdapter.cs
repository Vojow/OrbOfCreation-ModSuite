using System;
using System.Collections;
using System.Reflection;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;

namespace OrbAutomata;

internal sealed class AutoScribeNativeAdapter
{
    private const BindingFlags Instance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly AutoScribeFeatureDependencies _dependencies;
    private string? _quarantine;

    internal AutoScribeNativeAdapter(AutoScribeFeatureDependencies dependencies) =>
        _dependencies = dependencies;

    internal ServiceActionResult TryExecute(
        in AutoScribeCycleAction action,
        in SuiteRuntimeConfiguration config,
        in ServiceActionContext context)
    {
        var nativeCallsAttempted = 0;
        if (!AutoScribeServiceCycleFeature.IsOperational(config))
            return ServiceActionResult.Rejected(CommonActionResultCodes.ServiceDisabled);
        if (_quarantine is not null ||
            !Safe(_dependencies.CanConsumeScrolls) ||
            !Safe(_dependencies.Owns) ||
            !Safe(_dependencies.CapturePermit))
            return ServiceActionResult.Rejected(CommonActionResultCodes.PolicyRejected);
        if (_dependencies.ReadEpoch() != action.CollectedAtEpoch)
            return ServiceActionResult.Rejected(CommonActionResultCodes.LifecycleReplaced);

        try
        {
            if (!TryResolve(action.RecipeId, "CraftingRecipeSO", out var recipe) ||
                !TryResolve(_dependencies.Profile.ActiveInstances.Uuid,
                    _dependencies.Profile.ActiveInstances.ExpectedType, out var queue) ||
                !_dependencies.Profile.TryFindByScroll(action.ScrollId, out var role) ||
                !TryResolve(
                    action.ScrollId,
                    role.Scroll.ExpectedType,
                    out var scroll))
                return ServiceActionResult.Rejected(CommonActionResultCodes.NativeRejected);
            var queueValues = Field(queue!, "value") as IList;
            if (queueValues is null || !Invoke<bool>(queue!, "HasEmptySpot"))
                return ServiceActionResult.Rejected(CommonActionResultCodes.NativeRejected);
            if (!AutoItemsScrollTargetPreflight.TryCountValidTargetsAtLevel(
                    scroll!,
                    action.Level,
                    out var liveCandidates,
                    out _) ||
                liveCandidates <= CountCurrentSupply(
                    scroll!,
                    queue!,
                    action.RecipeId,
                    action.Level))
            {
                return ServiceActionResult.Rejected(CommonActionResultCodes.NativeRejected);
            }
            if (!Invoke<bool>(recipe!, "IsVisible") ||
                !Invoke<bool>(recipe!, "CanBuyAt", typeof(BigDouble), new BigDouble(action.Level)))
                return ServiceActionResult.Rejected(CommonActionResultCodes.NativeRejected);

            var before = queueValues.Count;
            var beforeOwned = CountOwned(scroll!, action.Level);
            var instanceType = ReflectionUtil.FindLoadedType("CraftingInstance");
            var recipeType = recipe!.GetType();
            var ctor = instanceType?.GetConstructor(
                Instance, null, new[] { recipeType, typeof(BigDouble) }, null);
            if (ctor is null)
                return ServiceActionResult.Faulted(CommonActionResultCodes.AdapterFault);

            nativeCallsAttempted++;
            InvokeVoid(recipe!, "PurchaseQuantity",
                new[] { typeof(BigDouble), typeof(BigDouble) },
                new object[] { new BigDouble(action.Level), BigDouble.Zero });
            var instance = ctor.Invoke(new object[] { recipe, new BigDouble(action.Level) });
            nativeCallsAttempted++;
            InvokeVoid(instance, "Initiate");
            var instantCraft = Invoke<bool>(instance, "CheckInstantCraft");
            nativeCallsAttempted++;
            if (instantCraft)
                InvokeVoid(instance, "InstantCraft");
            else
                InvokeVoid(queue!, "Add", new[] { instanceType! }, new[] { instance });

            var after = queueValues.Count;
            var afterOwned = CountOwned(scroll!, action.Level);
            var verified = instantCraft
                ? after == before && afterOwned == beforeOwned + 1
                : after == before + 1 && afterOwned == beforeOwned;
            if (!verified)
            {
                _quarantine = instantCraft
                    ? "Instant Scribe stock postcondition was ambiguous."
                    : "Scribe queue postcondition was ambiguous.";
                return FaultedMutation(
                    nativeCallsAttempted,
                    NativeMutationOutcome.PostconditionFailed);
            }
            var calls = new NativeMutationCallOutcome(3, 1, 1);
            return ServiceActionResult.Committed(
                CommonActionResultCodes.Committed,
                ServiceNativeMutationEvidence.Observed(
                    NativeMutationOutcome.Verified, calls));
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or TargetInvocationException or
                MemberAccessException or MissingMemberException)
        {
            _quarantine = ex.GetBaseException().Message;
            return nativeCallsAttempted > 0
                ? FaultedMutation(
                    nativeCallsAttempted,
                    NativeMutationOutcome.ExecutionThrew)
                : ServiceActionResult.Faulted(CommonActionResultCodes.AdapterFault);
        }
    }

    internal void InvalidateLifecycle() => _quarantine = null;
    internal bool IsQuarantined => _quarantine is not null;

    private bool TryResolve(Guid id, string typeName, out object? value)
    {
        value = null;
        var type = ReflectionUtil.FindLoadedType(typeName);
        if (type is null) return false;
        var result = _dependencies.Registry.Resolve(id, type);
        value = result.Value;
        return result.IsResolved;
    }

    private int CountCurrentSupply(
        object scroll,
        object activeQueue,
        Guid recipeId,
        int level)
    {
        var total = CountOwned(scroll, level) +
                    CountPending(scroll, level) +
                    CountWork(activeQueue, recipeId, level);
        if (!TryResolve(
                _dependencies.Profile.AutomaticInstances.Uuid,
                _dependencies.Profile.AutomaticInstances.ExpectedType,
                out var automatic))
        {
            throw new InvalidOperationException(
                "The automatic Scribe list was unavailable during preflight.");
        }
        return total + CountWork(automatic!, recipeId, level);
    }

    private static int CountOwned(object scroll, int level)
    {
        if (Field(scroll, "consumableCounts") is not IList counts)
            throw new InvalidOperationException("Scroll levelled inventory was unavailable.");
        var total = 0;
        foreach (var count in counts)
        {
            if (count is null) throw new InvalidOperationException("A Scroll count was null.");
            if (Invoke<int>(count, "GetLevel") >= level)
                total += Invoke<int>(count, "GetQuantity");
        }
        return total;
    }

    private static int CountPending(object scroll, int level)
    {
        if (Field(scroll, "consumableUsages") is not IList usages)
            throw new InvalidOperationException("Scroll pending usages were unavailable.");
        var total = 0;
        foreach (var usage in usages)
        {
            if (usage is null) throw new InvalidOperationException("A Scroll usage was null.");
            if (Field(usage, "en") is not bool engaged)
                throw new InvalidOperationException("A Scroll usage engagement flag changed.");
            var scaling = Field(usage, "baseSi");
            if (!engaged &&
                scaling is not null &&
                Invoke<int>(scaling, "GetLevelInt") >= level)
            {
                total++;
            }
        }
        return total;
    }

    private static int CountWork(
        object queue,
        Guid recipeId,
        int level)
    {
        if (Field(queue, "value") is not IList work)
            throw new InvalidOperationException("A Scribe work list was unavailable.");
        var total = 0;
        foreach (var instance in work)
        {
            if (instance is null)
                throw new InvalidOperationException("A Scribe work entry was null.");
            if (Invoke<Guid>(instance, "GetGuidReference") == recipeId &&
                BigDoubleLevel(InvokeObject(instance, "GetQuantity")) >= level &&
                !Invoke<bool>(instance, "IsExpired"))
            {
                total++;
            }
        }
        return total;
    }

    private static ServiceActionResult FaultedMutation(
        int nativeCallsAttempted,
        NativeMutationOutcome outcome)
    {
        var calls = new NativeMutationCallOutcome(nativeCallsAttempted, 1, 0);
        return ServiceActionResult.Faulted(
            CommonActionResultCodes.AdapterFault,
            ServiceNativeMutationEvidence.Observed(
                outcome, calls));
    }

    private static bool Safe(Func<bool> read)
    {
        try { return read(); }
        catch (Exception) { return false; }
    }

    private static object? Field(object owner, string name) =>
        owner.GetType().GetField(name, Instance)?.GetValue(owner);

    private static object InvokeObject(object owner, string method) =>
        owner.GetType().GetMethod(method, Instance, null, Type.EmptyTypes, null)?
            .Invoke(owner, Array.Empty<object>()) ??
        throw new MissingMethodException(owner.GetType().Name, method);

    private static Guid GuidOf(object owner) =>
        Invoke<Guid>(owner, "GetGuid");

    private static int BigDoubleLevel(object value)
    {
        if (value is not BigDouble number)
            throw new InvalidOperationException("A Scribe work level changed type.");
        var level = number.ToDouble();
        if (!double.IsFinite(level) || level < 1d || level > int.MaxValue)
            throw new InvalidOperationException("A Scribe work level was invalid.");
        return (int)Math.Floor(level);
    }

    private static T Invoke<T>(object owner, string method) =>
        Invoke<T>(owner, method, Type.EmptyTypes, Array.Empty<object>());

    private static T Invoke<T>(
        object owner, string method, Type parameter, object argument) =>
        Invoke<T>(owner, method, new[] { parameter }, new[] { argument });

    private static T Invoke<T>(
        object owner, string method, Type[] parameters, object[] arguments) =>
        owner.GetType().GetMethod(method, Instance, null, parameters, null)?
            .Invoke(owner, arguments) is T value
                ? value
                : throw new MissingMethodException(owner.GetType().Name, method);

    private static void InvokeVoid(object owner, string method) =>
        InvokeVoid(owner, method, Type.EmptyTypes, Array.Empty<object>());

    private static void InvokeVoid(
        object owner, string method, Type[] parameters, object[] arguments)
    {
        var info = owner.GetType().GetMethod(method, Instance, null, parameters, null) ??
            throw new MissingMethodException(owner.GetType().Name, method);
        if (info.ReturnType != typeof(void))
            throw new MissingMethodException(owner.GetType().Name, method);
        info.Invoke(owner, arguments);
    }
}
