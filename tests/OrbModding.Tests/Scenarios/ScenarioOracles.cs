using System;
using System.Collections.Generic;
using System.Linq;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests.Scenarios;

internal static class ScenarioOracles
{
    public static void LifecycleKindsInOrder(
        LifecycleScenarioKernel kernel,
        params GameLifecycleTransitionKind[] expected)
    {
        Assert.Equal(
            expected,
            kernel.LifecycleTrace.Select(transition => transition.Current.LastTransition!.Value));
    }

    public static void LifecycleStatesInOrder(
        LifecycleScenarioKernel kernel,
        params GameLifecycleState[] expected)
    {
        Assert.Equal(expected, kernel.LifecycleTrace.Select(transition => transition.Current.State));
    }

    public static void OneNativeMutationPerFrame(LifecycleScenarioKernel kernel)
    {
        var overlaps = kernel.Mutations
            .GroupBy(mutation => mutation.Frame)
            .Where(group => group.Count() > 1)
            .Select(group => $"frame {group.Key}: {string.Join(", ", group.Select(Describe))}")
            .ToArray();
        Assert.True(
            overlaps.Length == 0,
            $"More than one native mutation was recorded in a frame:{Environment.NewLine}{string.Join(Environment.NewLine, overlaps)}");
    }

    public static void MutationRequestsAreUnique(LifecycleScenarioKernel kernel)
    {
        var duplicates = kernel.Mutations
            .GroupBy(mutation => mutation.RequestId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        Assert.True(
            duplicates.Length == 0,
            $"Mutation requests executed more than once: {string.Join(", ", duplicates)}");
    }

    public static void NoLifecycleDispatchFailures(LifecycleScenarioKernel kernel)
    {
        Assert.Empty(kernel.Lifecycle.DispatchFailures);
    }

    public static void OnlyFeaturesMutated(
        LifecycleScenarioKernel kernel,
        params string[] expectedFeatures)
    {
        var expected = new HashSet<string>(expectedFeatures, StringComparer.Ordinal);
        Assert.All(kernel.Mutations, mutation => Assert.Contains(mutation.Feature, expected));
    }

    public static ScenarioCallbackObservation IgnoredCallback(
        LifecycleScenarioKernel kernel,
        string name)
    {
        var observation = Assert.Single(
            kernel.CallbackTrace,
            callback => string.Equals(callback.Name, name, StringComparison.Ordinal));
        Assert.False(observation.Executed);
        Assert.NotEqual(observation.ScheduledGeneration, observation.CurrentGeneration);
        return observation;
    }

    public static ScenarioCallbackObservation ExecutedCallback(
        LifecycleScenarioKernel kernel,
        string name)
    {
        var observation = Assert.Single(
            kernel.CallbackTrace,
            callback => string.Equals(callback.Name, name, StringComparison.Ordinal));
        Assert.True(observation.Executed);
        Assert.Equal(observation.ScheduledGeneration, observation.CurrentGeneration);
        return observation;
    }

    public static ScenarioCallbackObservation DeliveredCallback(
        LifecycleScenarioKernel kernel,
        string name)
    {
        var observation = Assert.Single(
            kernel.CallbackTrace,
            callback => string.Equals(callback.Name, name, StringComparison.Ordinal));
        Assert.True(observation.Executed);
        return observation;
    }

    private static string Describe(ScenarioMutationObservation mutation) =>
        $"{mutation.Feature}/{mutation.ActionFamily}/{mutation.Target}/{mutation.RequestId}";
}
