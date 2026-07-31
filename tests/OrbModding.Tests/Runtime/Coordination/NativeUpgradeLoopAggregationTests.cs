using System;
using System.Collections.Generic;
using OrbAutomata;
using Xunit;

namespace OrbModding.Tests.Runtime.Coordination;

public sealed class NativeUpgradeLoopAggregationTests : IDisposable
{
    public NativeUpgradeLoopAggregationTests()
    {
        SoundManager.ResetForTests();
        NativeUpgradeLoopAggregation.ResetForTests();
    }

    public void Dispose()
    {
        SoundManager.ResetForTests();
        NativeUpgradeLoopAggregation.ResetForTests();
    }

    [Fact]
    public void IdenticalUpgradeLoopsShareOneNativeElementAndTwoLeases()
    {
        var clip = new UnityEngine.AudioClip();

        var first = StartUpgradeLoop(clip, 0.4f);
        var second = StartUpgradeLoop(clip, 0.4f);
        var snapshot = NativeUpgradeLoopAggregation.Capture();

        Assert.Same(first, second);
        Assert.Equal(1, SoundManager.instance!.PlayLoopCalls);
        Assert.Equal(1, snapshot.ActiveGroups);
        Assert.Equal(2, snapshot.ActiveLeases);
        Assert.Equal(1, snapshot.NativeLoopsStarted);
        Assert.Equal(1, snapshot.CoalescedRequests);
    }

    [Fact]
    public void DifferentClipOrVolumeKeepsNativeLoopOwnershipSeparate()
    {
        var clip = new UnityEngine.AudioClip();

        StartUpgradeLoop(clip, 0.4f);
        StartUpgradeLoop(clip, 0.5f);
        StartUpgradeLoop(new UnityEngine.AudioClip(), 0.4f);

        var snapshot = NativeUpgradeLoopAggregation.Capture();
        Assert.Equal(3, SoundManager.instance!.PlayLoopCalls);
        Assert.Equal(3, snapshot.ActiveGroups);
        Assert.Equal(3, snapshot.ActiveLeases);
        Assert.Equal(0, snapshot.CoalescedRequests);
    }

    [Fact]
    public void SharedLoopStopsSynchronouslyOnlyWhenItsFinalLeaseEnds()
    {
        var clip = new UnityEngine.AudioClip();
        var element = StartUpgradeLoop(clip, 1f);
        Assert.Same(element, StartUpgradeLoop(clip, 1f));

        AudioElement intermediateResult = null!;
        var runIntermediate = NativeUpgradeLoopAggregation.PrefixFadeOutDestroy(
            element,
            ref intermediateResult);
        Assert.False(runIntermediate);
        Assert.Same(element, intermediateResult);
        Assert.Equal(0, element.FadeOutDestroyCalls);
        Assert.Equal(0, element.StopCalls);
        Assert.Equal(1, NativeUpgradeLoopAggregation.Capture().ActiveLeases);

        AudioElement finalResult = null!;
        var runFinal = NativeUpgradeLoopAggregation.PrefixFadeOutDestroy(
            element,
            ref finalResult);
        Assert.False(runFinal);
        Assert.Same(element, finalResult);
        Assert.Equal(0, element.FadeOutDestroyCalls);
        Assert.Equal(1, element.StopCalls);
        Assert.False(element.Playing);
        var snapshot = NativeUpgradeLoopAggregation.Capture();
        Assert.Equal(0, snapshot.ActiveGroups);
        Assert.Equal(1, snapshot.FinalStops);
        Assert.Equal(0, snapshot.StopFailures);
    }

    [Fact]
    public void FinalStopFailureIsContainedAndReportedWithoutRunningDelayedRelease()
    {
        var element = StartUpgradeLoop(new UnityEngine.AudioClip(), 1f);
        element.ThrowOnStop = true;

        AudioElement result = null!;
        var runOriginal = NativeUpgradeLoopAggregation.PrefixFadeOutDestroy(element, ref result);

        Assert.False(runOriginal);
        Assert.Same(element, result);
        Assert.Equal(1, element.StopCalls);
        Assert.Equal(0, element.FadeOutDestroyCalls);
        var snapshot = NativeUpgradeLoopAggregation.Capture();
        Assert.Equal(0, snapshot.ActiveGroups);
        Assert.Equal(0, snapshot.FinalStops);
        Assert.Equal(1, snapshot.StopFailures);
    }

    [Fact]
    public void UniqueUpgradeLoopAtReserveFloorIsSafelySuppressed()
    {
        SoundManager.instance!.SetPoolForTests(
            new List<AudioElement>
            {
                new() { Playing = true, Looping = true },
                new() { Playing = false, Looping = false },
            },
            maximum: 2);

        var result = StartUpgradeLoop(new UnityEngine.AudioClip(), 1f);
        var snapshot = NativeUpgradeLoopAggregation.Capture();

        Assert.Null(result);
        Assert.Equal(0, SoundManager.instance.PlayLoopCalls);
        Assert.Equal(0, snapshot.ActiveGroups);
        Assert.Equal(1, snapshot.ReserveSuppressions);
    }

    [Fact]
    public void ExistingSharedLoopStillCoalescesAtReserveFloor()
    {
        var clip = new UnityEngine.AudioClip();
        var first = StartUpgradeLoop(clip, 1f);
        SoundManager.instance!.SetPoolForTests(
            new List<AudioElement>
            {
                new() { Playing = true, Looping = true },
                new() { Playing = false, Looping = false },
            },
            maximum: 2);

        var second = StartUpgradeLoop(clip, 1f);

        Assert.Same(first, second);
        Assert.Equal(1, SoundManager.instance.PlayLoopCalls);
        Assert.Equal(0, NativeUpgradeLoopAggregation.Capture().ReserveSuppressions);
    }

    [Fact]
    public void SpellOrBrewingScopeNeverUsesUpgradeAggregation()
    {
        var clip = new UnityEngine.AudioClip();
        AudioElement first = null!;
        var runFirst = NativeUpgradeLoopAggregation.PrefixPlayLoop(
            clip,
            1f,
            ref first,
            out var firstState);
        AudioElement second = null!;
        var runSecond = NativeUpgradeLoopAggregation.PrefixPlayLoop(
            clip,
            1f,
            ref second,
            out var secondState);

        Assert.True(runFirst);
        Assert.True(runSecond);
        Assert.False(firstState);
        Assert.False(secondState);
        Assert.Equal(0, NativeUpgradeLoopAggregation.Capture().ActiveGroups);
    }

    [Fact]
    public void RuntimeDisableBypassesNewAggregationWithoutDroppingTrackedLeases()
    {
        var clip = new UnityEngine.AudioClip();
        StartUpgradeLoop(clip, 1f);

        var disabled = NativeUpgradeLoopAggregation.SetEnabled(false);
        NativeUpgradeLoopAggregation.EnterUpgradeScope();
        try
        {
            AudioElement result = null!;
            var runOriginal = NativeUpgradeLoopAggregation.PrefixPlayLoop(
                clip,
                1f,
                ref result,
                out var register);
            Assert.True(runOriginal);
            Assert.False(register);
        }
        finally
        {
            NativeUpgradeLoopAggregation.ExitUpgradeScope();
        }

        Assert.False(disabled.Enabled);
        Assert.Equal(1, disabled.ActiveGroups);
        Assert.Equal(1, disabled.ActiveLeases);
    }

    [Fact]
    public void CounterResetPreservesPolicyAndActiveGroups()
    {
        var clip = new UnityEngine.AudioClip();
        StartUpgradeLoop(clip, 1f);
        StartUpgradeLoop(clip, 1f);

        var reset = NativeUpgradeLoopAggregation.ResetCounters();

        Assert.True(reset.Enabled);
        Assert.Equal(1, reset.ActiveGroups);
        Assert.Equal(2, reset.ActiveLeases);
        Assert.Equal(0, reset.NativeLoopsStarted);
        Assert.Equal(0, reset.CoalescedRequests);
        Assert.Equal(0, reset.ReserveSuppressions);
        Assert.Equal(0, reset.FinalStops);
        Assert.Equal(0, reset.StopFailures);
    }

    [Fact]
    public void LifecycleReplacementDropsUnityReferencesAndCountersButPreservesPolicy()
    {
        long lifecycle = 11;
        NativeUpgradeLoopAggregation.SetLifecycleReaderForTests(() => lifecycle);
        var clip = new UnityEngine.AudioClip();
        StartUpgradeLoop(clip, 1f);
        StartUpgradeLoop(clip, 1f);
        NativeUpgradeLoopAggregation.SetEnabled(false);

        lifecycle++;
        var replaced = NativeUpgradeLoopAggregation.Capture();

        Assert.Equal(12, replaced.Lifecycle);
        Assert.False(replaced.Enabled);
        Assert.Equal(0, replaced.ActiveGroups);
        Assert.Equal(0, replaced.ActiveLeases);
        Assert.Equal(0, replaced.NativeLoopsStarted);
        Assert.Equal(0, replaced.CoalescedRequests);
        Assert.Equal(0, replaced.ReserveSuppressions);
        Assert.Equal(0, replaced.FinalStops);
        Assert.Equal(0, replaced.StopFailures);
    }

    private static AudioElement StartUpgradeLoop(UnityEngine.AudioClip clip, float volume)
    {
        NativeUpgradeLoopAggregation.EnterUpgradeScope();
        try
        {
            AudioElement result = null!;
            var runOriginal = NativeUpgradeLoopAggregation.PrefixPlayLoop(
                clip,
                volume,
                ref result,
                out var register);
            if (runOriginal)
            {
                result = SoundManager.PlayLoop(clip, volume);
                NativeUpgradeLoopAggregation.PostfixPlayLoop(
                    clip,
                    volume,
                    result,
                    register);
            }
            return result;
        }
        finally
        {
            NativeUpgradeLoopAggregation.ExitUpgradeScope();
        }
    }
}
