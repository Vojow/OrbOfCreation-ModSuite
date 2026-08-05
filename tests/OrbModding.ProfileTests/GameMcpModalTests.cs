using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpModalTests : IDisposable
{
    private const long Epoch = 41;

    public GameMcpModalTests() => UnityEngine.Resources.Objects.Clear();

    public void Dispose() => UnityEngine.Resources.Objects.Clear();

    [Fact]
    public void Tool_is_one_targetless_ui_state_control()
    {
        var tool = Assert.Single(GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "game_modal");

        Assert.Equal(new[] { "mode" }, tool["inputSchema"]!["required"]!.Values<string>());
        Assert.Equal(new[] { "dismiss" },
            tool["inputSchema"]!["properties"]!["mode"]!["enum"]!.Values<string>());
        var operation = GameMcpProtocolRouter.BuildOperation("game_modal", new JObject
        {
            ["mode"] = "dismiss",
        });
        Assert.Equal(GameMcpOperationClass.UiState, operation.Classification);
        Assert.Equal(Guid.Empty, operation.Uuid);
    }

    [Fact]
    public void Dismiss_uses_the_only_open_modal_and_observes_native_closing()
    {
        var modal = new UIModal();
        modal.OpenForTest();
        UnityEngine.Resources.Objects.Add(modal);
        using var action = new ModalDismissGameAction(() => Epoch);

        var result = action.Submit(Epoch);
        var observedBeforeAnimation = action.TryObserveDismissed(Epoch, out var before, out var reason);
        var unrelated = new UIModal();
        unrelated.OpenForTest();
        UnityEngine.Resources.Objects.Add(unrelated);
        modal.FinishCloseForTest();
        var observedAfterAnimation = action.TryObserveDismissed(Epoch, out var after, out reason);

        Assert.True(result.Committed, result.Reason);
        Assert.True(observedBeforeAnimation, reason);
        Assert.False(before);
        Assert.True(observedAfterAnimation, reason);
        Assert.True(after);
    }

    [Fact]
    public void Unity_framework_types_do_not_depend_on_the_game_type_resolver()
    {
        using var action = new ModalDismissGameAction(
            () => Epoch,
            resolveType: name => name.StartsWith("UnityEngine.", StringComparison.Ordinal)
                ? null
                : ReflectionUtil.FindLoadedType(name));

        Assert.True(action.BindingsAvailable, action.BindingFailure);
    }

    [Fact]
    public void Dismiss_refuses_zero_multiple_grace_and_native_no_op_states()
    {
        using var action = new ModalDismissGameAction(() => Epoch);
        Assert.Equal("no_modal_open", action.Submit(Epoch).Code);

        var first = new UIModal();
        first.OpenForTest();
        var second = new UIModal();
        second.OpenForTest();
        UnityEngine.Resources.Objects.Add(first);
        UnityEngine.Resources.Objects.Add(second);
        Assert.Equal("multiple_modals_open", action.Submit(Epoch).Code);

        UnityEngine.Resources.Objects.Remove(second);
        first.FinishCloseForTest();
        var grace = new UIModal();
        grace.OpenForTest(0.2f);
        UnityEngine.Resources.Objects.Add(grace);
        Assert.Equal("modal_close_not_ready", action.Submit(Epoch).Code);

        grace.FinishCloseForTest();
        var noOp = new UIModal { SuppressClose = true };
        noOp.OpenForTest();
        UnityEngine.Resources.Objects.Add(noOp);
        Assert.Equal("requested_state_not_reached", action.Submit(Epoch).Code);
    }

    [Fact]
    public void Every_missing_member_disables_the_complete_modal_binding_set()
    {
        foreach (var missing in ModalDismissGameAction.ContractIds)
        {
            using var action = new ModalDismissGameAction(
                () => Epoch,
                includeContract: id => id != missing);
            Assert.False(action.BindingsAvailable);
            Assert.Contains(missing, action.BindingFailure, StringComparison.Ordinal);
        }
    }
}
