using System;
using System.Threading.Tasks;
using OrbAutomata;
using Xunit;

namespace OrbModding.Tests.Services.RunTransition;

public sealed class ReturnToMenuGameActionTests
{
    private const long Epoch = 141;
    private readonly UIBackToMenuButton _button = new();

    public ReturnToMenuGameActionTests()
    {
        UIScreenFlash.ResetForTests();
        SaveStateManager.instance = new SaveStateManager();
    }

    [Fact]
    public void NativeUiCallbackRaisesManualSaveAndStartsTheSceneTransition()
    {
        using var boundary = Boundary();

        var result = Submit(boundary);

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(1, _button.manualSave.RaiseCalls);
        Assert.True(UIScreenFlash.instance.ActiveForTests);
        Assert.Equal(ReturnToMenuNativeStage.Verification, result.Stage);
    }

    [Fact]
    public void MutableAdmissionRefusesWrongSceneActiveTransitionMissingControlAndRevokedPermit()
    {
        using var wrongScene = Boundary(scene: "Start");
        Assert.Equal(ReturnToMenuPreflight.WrongScene, Submit(wrongScene).Preflight);

        UIScreenFlash.instance.FadeIn(0f, 0f);
        using var active = Boundary();
        Assert.Equal(ReturnToMenuPreflight.TransitionInProgress, Submit(active).Preflight);
        UIScreenFlash.ResetForTests();

        using var missing = Boundary(buttons: Array.Empty<object>());
        Assert.Equal(ReturnToMenuPreflight.ControlUnavailable, Submit(missing).Preflight);

        using var revoked = Boundary(permit: false);
        Assert.Equal(ReturnToMenuPreflight.MutationPermitUnavailable, Submit(revoked).Preflight);
        Assert.Equal(0, _button.manualSave.RaiseCalls);
    }

    [Fact]
    public void NativeNoOpFailsTheSingleScreenTransitionSentinel()
    {
        UIScreenFlash.SuppressFade = true;
        using var boundary = Boundary();

        var result = Submit(boundary);

        Assert.Equal(ReturnToMenuPreflight.VerificationFailed, result.Preflight);
        Assert.Equal(1, _button.manualSave.RaiseCalls);
        Assert.False(UIScreenFlash.instance.ActiveForTests);
    }

    [Fact]
    public async Task OffThreadSubmissionRefusesBeforeTheUiCallback()
    {
        using var boundary = Boundary();

        var result = await Task.Run(() => Submit(boundary));

        Assert.Equal(ReturnToMenuPreflight.WrongThread, result.Preflight);
        Assert.Equal(0, _button.manualSave.RaiseCalls);
    }

    [Fact]
    public void EveryMissingMemberDisablesTheCompleteBindingSet()
    {
        foreach (var missing in ReturnToMenuNativeBindings.ContractIds)
        {
            using var boundary = Boundary(includeContract: id => id != missing);
            Assert.False(boundary.BindingsAvailable);
            Assert.Contains(missing, boundary.BindingFailure, StringComparison.Ordinal);
        }
    }

    private ReturnToMenuGameAction Boundary(
        string scene = "Main",
        object[]? buttons = null,
        bool permit = true,
        Func<string, bool>? includeContract = null) =>
        new(
            () => Epoch,
            () => permit,
            static () => "RunTransition ownership was revoked.",
            () => scene,
            _ => buttons ?? new object[] { _button },
            includeContract: includeContract);

    private static ReturnToMenuSubmission Submit(ReturnToMenuGameAction boundary)
    {
        var action = new ReturnToMenuAction(Epoch);
        return boundary.Submit(in action);
    }
}
