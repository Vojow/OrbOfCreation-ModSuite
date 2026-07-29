namespace OrbAutomata;

/// <summary>What one live admission term said, or that it could not be asked at all.</summary>
internal enum AutoBuyAdmissionTerm
{
    Unread = 0,

    /// <summary>The term is satisfied — on its own it would let the purchase through.</summary>
    Passed,

    /// <summary>The term is the reason, or one of the reasons, the game said no.</summary>
    Refused,
}

/// <summary>
/// The game's <c>CanPurchase()</c> taken apart, read term by term immediately after it refused a
/// purchase the worker had planned.
/// </summary>
/// <remarks>
/// <para>
/// <c>CanPurchase()</c> answers with a single bool, so a refusal says nothing about which of the
/// several conditions folded into it was the one that bit. A planner that disagrees with the game
/// has a bug in it, and "the game said no" is not a bug report — so the terms the game exposes
/// individually are asked individually, on the cold path only, after the fold has already refused.
/// </para>
/// <para>
/// Some of what <c>CanPurchase()</c> folds in has no parameterless reader: the per-level
/// prerequisites are checked against a level the caller supplies. When every readable term passes and
/// the fold still refused, that is what is left, and the diagnosis says so by elimination rather than
/// pretending to have read it. Since W58 the planner models those conditions itself and refuses to
/// plan a candidate whose next level is gated, so reaching this clause now means the suite's model
/// and the game disagree — which is the strongest signal this boundary can give and is worth saying
/// in those words.
/// </para>
/// </remarks>
internal readonly struct AutoBuyAdmissionDiagnosis
{
    /// <summary>What the boundary says when every term it could read was satisfied.</summary>
    internal const string RefusedByAnUnreadableTerm =
        "refused by an unreadable admission term (per-level prerequisites by elimination, " +
        "which the planner modelled as met)";

    /// <summary>What the boundary says when not one term could be read.</summary>
    internal const string RefusedWithoutDiagnosis = "native admission refused";

    public AutoBuyAdmissionDiagnosis(
        AutoBuyAdmissionTerm isAvailable,
        AutoBuyAdmissionTerm isMaxLevel,
        AutoBuyAdmissionTerm isMaxQueuedLevel,
        AutoBuyAdmissionTerm hasEnough)
    {
        IsAvailable = isAvailable;
        IsMaxLevel = isMaxLevel;
        IsMaxQueuedLevel = isMaxQueuedLevel;
        HasEnough = hasEnough;
    }

    /// <summary>The candidate is unlocked and visible. <c>Refused</c> means it is not.</summary>
    public AutoBuyAdmissionTerm IsAvailable { get; }

    /// <summary>
    /// The candidate has levels left. <c>Refused</c> means it is finished; absent on a kind with no
    /// bounded level at all, which is why it is read rather than derived.
    /// </summary>
    public AutoBuyAdmissionTerm IsMaxLevel { get; }

    /// <summary>Room remains once what is already queued is counted.</summary>
    public AutoBuyAdmissionTerm IsMaxQueuedLevel { get; }

    /// <summary>The game's own verdict on the price, from the cost list it builds for this level.</summary>
    public AutoBuyAdmissionTerm HasEnough { get; }

    /// <summary>Whether any term at all could be read.</summary>
    public bool WasAsked =>
        IsAvailable != AutoBuyAdmissionTerm.Unread ||
        IsMaxLevel != AutoBuyAdmissionTerm.Unread ||
        IsMaxQueuedLevel != AutoBuyAdmissionTerm.Unread ||
        HasEnough != AutoBuyAdmissionTerm.Unread;

    /// <summary>The first term that refused, or an empty string when none of the readable ones did.</summary>
    public string RefusingTerm
    {
        get
        {
            if (IsAvailable == AutoBuyAdmissionTerm.Refused) return "IsAvailable()";
            if (IsMaxLevel == AutoBuyAdmissionTerm.Refused) return "IsMaxLevel()";
            if (IsMaxQueuedLevel == AutoBuyAdmissionTerm.Refused) return "IsMaxQueuedLevel()";
            if (HasEnough == AutoBuyAdmissionTerm.Refused) return "GetPurchaseCost().HasEnough()";
            return string.Empty;
        }
    }

    /// <summary>One clause naming why the game refused, for the log line and the bundle alike.</summary>
    public string Describe()
    {
        if (!WasAsked) return RefusedWithoutDiagnosis;
        var refusing = RefusingTerm;
        return refusing.Length == 0 ? RefusedByAnUnreadableTerm : "refused by " + refusing;
    }
}
