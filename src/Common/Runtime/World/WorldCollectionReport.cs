using System;
using System.Text;

namespace OrbModding.Common.Runtime.World;

/// <summary>What happened to one entity category during a collection.</summary>
internal enum WorldCategoryOutcome
{
    /// <summary>
    /// The category could not be read at all on this build: its type, its registry, or one of its
    /// required accessors was absent. No rows were produced.
    /// </summary>
    Unavailable,

    /// <summary>The category was traversed. Individual entities may still have been skipped.</summary>
    Collected,
}

/// <summary>The outcome of traversing one category, including what it could not read and why.</summary>
internal readonly struct WorldCategoryReport
{
    internal WorldCategoryReport(
        string category,
        WorldCategoryOutcome outcome,
        int sampled,
        int skipped,
        string firstFailure)
    {
        Category = category;
        Outcome = outcome;
        Sampled = sampled;
        Skipped = skipped;
        FirstFailure = firstFailure;
    }

    internal string Category { get; }
    internal WorldCategoryOutcome Outcome { get; }

    /// <summary>Entities that produced a row.</summary>
    internal int Sampled { get; }

    /// <summary>Entities the traversal reached but could not turn into a row.</summary>
    internal int Skipped { get; }

    /// <summary>
    /// Why the category was unavailable, or why the first skipped entity was skipped. Empty when
    /// nothing went wrong.
    /// </summary>
    internal string FirstFailure { get; }

    internal bool IsClean => Outcome == WorldCategoryOutcome.Collected && Skipped == 0;

    internal static WorldCategoryReport Missing(string category, string reason) =>
        new(category, WorldCategoryOutcome.Unavailable, sampled: 0, skipped: 0, reason);
}

/// <summary>
/// The evidence one collection leaves behind: what was read, what was not, and why.
/// </summary>
/// <remarks>
/// Collection degrades per category rather than all-or-nothing, so a build that renamed one research
/// member still yields resources, structures, and upgrades. That is only defensible if the gap is
/// visible — a partial snapshot that reports itself as complete is worse than no snapshot, because
/// every consumer downstream would read "no research available" as a fact about the save rather than
/// a fact about the read.
/// </remarks>
internal sealed class WorldCollectionReport
{
    private readonly WorldCategoryReport[] _categories;

    internal WorldCollectionReport(params WorldCategoryReport[] categories) =>
        _categories = categories ?? throw new ArgumentNullException(nameof(categories));

    /// <summary>Every category the collector attempted, in the order it walked them.</summary>
    internal ReadOnlySpan<WorldCategoryReport> Categories => _categories;

    /// <summary>Whether every category was traversed with no entity skipped.</summary>
    internal bool IsComplete
    {
        get
        {
            foreach (var report in _categories)
            {
                if (!report.IsClean) return false;
            }

            return true;
        }
    }

    internal int TotalSampled
    {
        get
        {
            var total = 0;
            foreach (var report in _categories) total += report.Sampled;
            return total;
        }
    }

    /// <summary>
    /// The report for one category, or an <see cref="WorldCategoryOutcome.Unavailable"/> stand-in
    /// saying the collector never walked it. Callers asking about a category that was not attempted
    /// deserve that as an answer rather than as an exception.
    /// </summary>
    internal WorldCategoryReport For(string category)
    {
        foreach (var report in _categories)
        {
            if (string.Equals(report.Category, category, StringComparison.Ordinal)) return report;
        }

        return WorldCategoryReport.Missing(category, "the category was not collected");
    }

    /// <summary>
    /// One line: a total when everything read cleanly, otherwise every category that fell short with
    /// the first reason for each. Naming only the shortfalls keeps the line readable as the category
    /// count grows, and keeps the interesting part at the front.
    /// </summary>
    internal string Describe()
    {
        if (IsComplete)
        {
            return $"World collection complete: {TotalSampled} entities across " +
                $"{_categories.Length} categories.";
        }

        var text = new StringBuilder("World collection incomplete:");
        foreach (var report in _categories) Describe(text, in report);
        return text.ToString();
    }

    private static void Describe(StringBuilder text, in WorldCategoryReport report)
    {
        if (report.IsClean) return;

        text.Append(' ').Append(report.Category).Append(": ");
        text.Append(report.Outcome == WorldCategoryOutcome.Unavailable
            ? "unavailable"
            : $"{report.Sampled} read, {report.Skipped} skipped");

        if (report.FirstFailure.Length > 0) text.Append(" — ").Append(report.FirstFailure);
        text.Append('.');
    }
}
