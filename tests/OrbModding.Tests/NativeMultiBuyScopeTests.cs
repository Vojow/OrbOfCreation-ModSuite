using System;
using OrbAutomata;
using Xunit;

namespace OrbModding.Tests;

public sealed class NativeMultiBuyScopeTests : IDisposable
{
    public NativeMultiBuyScopeTests()
    {
        NativeMultiBuyScope.ResetQuarantineForTests();
        GlobalVariables.MultiBuy = new IntVariable { Value = 7 };
    }

    [Fact]
    public void SetterWritesThenThrows_RestoresOriginalAndDoesNotQuarantine()
    {
        GlobalVariables.MultiBuy.ThrowAfterWriteFor = 1;

        Assert.False(NativeMultiBuyScope.TryEnterOne(out _, out var reason));

        Assert.Contains("after write", reason, StringComparison.Ordinal);
        Assert.Contains("restoration to 7 verified", reason, StringComparison.Ordinal);
        Assert.Equal(7, GlobalVariables.MultiBuy.Value);
        Assert.False(NativeMultiBuyScope.IsMutationQuarantined);

        GlobalVariables.MultiBuy.ThrowAfterWriteFor = null;
        Assert.True(NativeMultiBuyScope.TryEnterOne(out var scope, out reason), reason);
        scope.Dispose();
        Assert.Equal(7, GlobalVariables.MultiBuy.Value);
    }

    [Fact]
    public void RestorationThrowsWithoutRestoring_QuarantinesFurtherMutation()
    {
        Assert.True(NativeMultiBuyScope.TryEnterOne(out var scope, out var reason), reason);
        Assert.Equal(1, GlobalVariables.MultiBuy.Value);
        GlobalVariables.MultiBuy.ThrowBeforeWriteFor = 7;

        scope.Dispose();

        Assert.Equal(1, GlobalVariables.MultiBuy.Value);
        Assert.True(NativeMultiBuyScope.IsMutationQuarantined);
        Assert.False(NativeMultiBuyScope.TryEnterOne(out _, out reason));
        Assert.Contains("quarantined", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExceptionInsideSuccessfulScope_RestoresOriginalValue()
    {
        Assert.True(NativeMultiBuyScope.TryEnterOne(out var scope, out var reason), reason);

        Assert.Throws<InvalidOperationException>((Action)(() =>
        {
            using (scope)
            {
                Assert.Equal(1, GlobalVariables.MultiBuy.Value);
                throw new InvalidOperationException("simulated purchase failure");
            }
        }));

        Assert.Equal(7, GlobalVariables.MultiBuy.Value);
        Assert.False(NativeMultiBuyScope.IsMutationQuarantined);
    }

    public void Dispose()
    {
        GlobalVariables.MultiBuy = new IntVariable();
        NativeMultiBuyScope.ResetQuarantineForTests();
    }
}
