using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using Xunit;

namespace OrbModding.Tests.Services.AutoScribe;

public sealed class AutoScribeRoleSelectionTests
{
    [Fact]
    public void BlankMeansAllWhileNoneMeansNoRole()
    {
        var roles = Profile().Roles;

        var all = AutoScribeRoleSelection.ParsePublication(string.Empty, roles);
        var none = AutoScribeRoleSelection.ParsePublication(" NONE ", roles);

        Assert.Null(all);
        Assert.NotNull(none);
        Assert.Equal(0, none!.Count);
        Assert.True(AutoScribeRoleSelection.Contains(
            all,
            new ScrollRoleKey("scribe.advancement")));
        Assert.False(AutoScribeRoleSelection.Contains(
            none,
            new ScrollRoleKey("scribe.advancement")));
    }

    [Fact]
    public void SelectionFiltersUnknownAndDuplicateRolesAndSerializesInStableOrder()
    {
        var roles = Profile().Roles;

        var parsed = AutoScribeRoleSelection.ParseKnown(
            "unknown,scribe.power,scribe.advancement,scribe.power",
            roles);
        var publication = AutoScribeRoleSelection.ParsePublication(
            "unknown,scribe.power,scribe.advancement,scribe.power",
            roles);

        Assert.Equal(2, parsed.Count);
        Assert.Equal(
            "scribe.advancement,scribe.power",
            AutoScribeRoleSelection.Serialize(parsed, roles));
        Assert.NotNull(publication);
        Assert.Equal(2, publication!.Count);
        Assert.True(AutoScribeRoleSelection.Contains(
            publication,
            new ScrollRoleKey("scribe.advancement")));
        Assert.True(AutoScribeRoleSelection.Contains(
            publication,
            new ScrollRoleKey("scribe.power")));
        Assert.False(AutoScribeRoleSelection.Contains(
            publication,
            new ScrollRoleKey("scribe.learning")));
    }

    [Fact]
    public void WorkerStateReparsesRolesOnlyForANewConfigurationGeneration()
    {
        var profile = Profile();
        var state = AutoScribeCycleState.Create(new LifecycleGeneration(1));
        state.ObserveConfiguration(
            new ConfigGeneration(1),
            new AutoScribeConfiguration { Roles = "scribe.power" },
            profile.Roles);

        state.ObserveConfiguration(
            new ConfigGeneration(1),
            new AutoScribeConfiguration { Roles = "scribe.learning" },
            profile.Roles);

        Assert.True(AutoScribeRoleSelection.Contains(
            state.EnabledRoles,
            new ScrollRoleKey("scribe.power")));
        Assert.False(AutoScribeRoleSelection.Contains(
            state.EnabledRoles,
            new ScrollRoleKey("scribe.learning")));

        state.ObserveConfiguration(
            new ConfigGeneration(2),
            new AutoScribeConfiguration { Roles = "scribe.learning" },
            profile.Roles);

        Assert.False(AutoScribeRoleSelection.Contains(
            state.EnabledRoles,
            new ScrollRoleKey("scribe.power")));
        Assert.True(AutoScribeRoleSelection.Contains(
            state.EnabledRoles,
            new ScrollRoleKey("scribe.learning")));
    }

    private static AutoScribeIdentityProfile Profile()
    {
        var catalog = new AutoScribeIdentityCatalog();
        Assert.True(catalog.TryGetProfile(
            GameAssemblyAudit.WindowsV1052BaselineId,
            out var profile));
        return profile;
    }
}
