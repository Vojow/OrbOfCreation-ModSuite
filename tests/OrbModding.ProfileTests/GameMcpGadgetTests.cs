using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpGadgetTests
{
    [Theory]
    [InlineData("runtime", true)]
    [InlineData("action_queue_room", true)]
    [InlineData("audio_pool", true)]
    [InlineData("navigation", true)]
    [InlineData("SaveStateManager", false)]
    [InlineData("System.IO.File.Delete", false)]
    public void ProbeVocabularyIsClosed(string probe, bool expected)
    {
        Assert.Equal(expected, GameMcpGadgetPolicy.IsAllowlistedProbe(probe));
    }

    [Theory]
    [InlineData("Canvas[0]/ContentArea[2]/MainContentContainer[2]/SubviewRadio[1]/Tab[0]", true)]
    [InlineData("Canvas[0]/PopupContainer[6]/Modal(Clone)[25]/Tab[0]", false)]
    [InlineData("", false)]
    public void SubtabCatalogIncludesOnlyTheCurrentContentHierarchy(
        string path,
        bool expected)
    {
        Assert.Equal(expected, GameMcpGadgetPolicy.IsCurrentContentSubtabPath(path));
    }

    [Fact]
    public void ScreenshotHasNoRequiredParametersOrCallerFilename()
    {
        var screenshot = Tool("game_screenshot");
        Assert.Null(screenshot["inputSchema"]!["required"]);
        var properties = (JObject)screenshot["inputSchema"]!["properties"]!;
        Assert.Equal(new[] { "save" }, properties.Properties().Select(p => p.Name));
    }

    [Fact]
    public void ContinueHasNoCallerSelectedSaveOrNativeSurface()
    {
        var run = Tool("game_continue");
        Assert.Null(run["inputSchema"]!["required"]);
        Assert.Empty((JObject)run["inputSchema"]!["properties"]!);
        Assert.False((bool)run["annotations"]!["readOnlyHint"]!);
    }

    [Fact]
    public void TooltipDiscoveryIsBoundedAndSelectorFree()
    {
        var tooltips = Tool("game_tooltips");
        Assert.Null(tooltips["inputSchema"]!["required"]);
        var properties = (JObject)tooltips["inputSchema"]!["properties"]!;
        Assert.Equal(
            new[] { "offset", "limit" },
            properties.Properties().Select(property => property.Name));
        Assert.Null(properties["path"]);
        Assert.Equal(200, (int)properties["limit"]!["maximum"]!);
    }

    [Fact]
    public void NavigationUsesGenericCatalogSelectorsOnly()
    {
        var navigation = Tool("game_navigate");
        var properties = (JObject)navigation["inputSchema"]!["properties"]!;
        Assert.Equal(
            new[] { "tab", "subtab", "plotNodeUuid", "capture" },
            properties.Properties().Select(property => property.Name));
        Assert.Null(properties["operation"]);
        Assert.Null(properties["tabIndex"]);
    }

    [Fact]
    public void AudioLoopControlIsBoundedAndNeverOffersForceStop()
    {
        var control = Tool("game_audio_loop_control");
        var properties = (JObject)control["inputSchema"]!["properties"]!;
        Assert.Equal(new[] { "operation" }, properties.Properties().Select(p => p.Name));
        Assert.Equal(
            new[] { "enable", "disable", "reset_counters" },
            properties["operation"]!["enum"]!.Values<string>());
        Assert.False((bool)control["annotations"]!["readOnlyHint"]!);
        Assert.DoesNotContain("force_stop", properties["operation"]!["enum"]!.Values<string>());
    }

    private static JObject Tool(string name)
    {
        var router = new GameMcpProtocolRouter(
            new GameMcpStateStore(),
            new GameMcpCommandBus());
        var response = router.Handle(GameMcpAcceptanceFixture.Request(
            1,
            "tools/list",
            new JObject()));
        return Assert.Single(
            response.Body!["result"]!["tools"]!.Values<JObject>(),
            value => (string?)value!["name"] == name)!;
    }
}
