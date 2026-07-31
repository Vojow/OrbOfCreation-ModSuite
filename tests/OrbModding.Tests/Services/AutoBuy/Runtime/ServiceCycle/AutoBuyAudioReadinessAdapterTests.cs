using System.Collections.Generic;
using OrbAutomata;
using Xunit;

namespace OrbModding.Tests.Services.AutoBuy.Runtime.ServiceCycle;

public sealed class AutoBuyAudioReadinessAdapterTests : System.IDisposable
{
    public AutoBuyAudioReadinessAdapterTests() => global::SoundManager.ResetForTests();

    public void Dispose() => global::SoundManager.ResetForTests();

    [Fact]
    public void Read_CountsIdleAndPlayingNonLoopingEntriesAsReusable()
    {
        global::SoundManager.instance!.SetPoolForTests(
            new List<global::AudioElement>
            {
                new() { Playing = true, Looping = true },
                new() { Playing = true, Looping = false },
                new() { Playing = false, Looping = true },
            },
            maximum: 3,
            index: 1);

        var readable = new AutoBuyNativeAudioReadinessAdapter()
            .TryReadReusableSlots(out var slots, out var reason);

        Assert.True(readable, reason);
        Assert.Equal(2, slots);
        Assert.Equal(string.Empty, reason);
    }

    [Fact]
    public void Read_AllPlayingLoopsReportsZeroWithoutMutatingThePool()
    {
        var first = new global::AudioElement { Playing = true, Looping = true };
        var second = new global::AudioElement { Playing = true, Looping = true };
        global::SoundManager.instance!.SetPoolForTests(
            new List<global::AudioElement> { first, second },
            maximum: 2,
            index: 1);

        var readable = new AutoBuyNativeAudioReadinessAdapter()
            .TryReadReusableSlots(out var slots, out var reason);

        Assert.True(readable);
        Assert.Equal(0, slots);
        Assert.Contains("no idle or reusable non-looping", reason);
        Assert.True(first.Playing);
        Assert.True(second.Playing);
    }

    [Fact]
    public void Read_IncompletePoolFailsClosed()
    {
        global::SoundManager.instance!.SetPoolForTests(
            new List<global::AudioElement> { new() },
            maximum: 2);

        var readable = new AutoBuyNativeAudioReadinessAdapter()
            .TryReadReusableSlots(out var slots, out var reason);

        Assert.False(readable);
        Assert.Equal(0, slots);
        Assert.Contains("incomplete", reason);
    }

    [Fact]
    public void Read_MissingManagerFailsClosed()
    {
        global::SoundManager.instance = null;

        var readable = new AutoBuyNativeAudioReadinessAdapter()
            .TryReadReusableSlots(out var slots, out var reason);

        Assert.False(readable);
        Assert.Equal(0, slots);
        Assert.Contains("SoundManager.instance", reason);
    }
}
