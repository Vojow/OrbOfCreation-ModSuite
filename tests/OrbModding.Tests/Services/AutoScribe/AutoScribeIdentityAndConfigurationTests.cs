using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using Xunit;

namespace OrbModding.Tests.Services.AutoScribe;

public sealed class AutoScribeIdentityAndConfigurationTests
{
    [Fact]
    public void AuditedProfilesExposeStableSemanticRolesAndCostRanks()
    {
        var catalog = new AutoScribeIdentityCatalog();

        Assert.True(catalog.TryGetProfile(
            GameAssemblyAudit.WindowsV1052BaselineId,
            out var windows));
        Assert.True(catalog.TryGetProfile(
            GameAssemblyAudit.MacV1052BaselineId,
            out var mac));
        Assert.Equal(8, windows.Roles.Count);
        Assert.Equal(8, mac.Roles.Count);

        var keys = new HashSet<string>(StringComparer.Ordinal);
        var scrolls = new HashSet<Guid>();
        var enchantments = new HashSet<Guid>();
        var recipes = new HashSet<Guid>();
        var ranks = new HashSet<int>();
        for (var index = 0; index < windows.Roles.Count; index++)
        {
            var role = windows.Roles[index];
            Assert.StartsWith("scribe.", role.Key.Value);
            Assert.True(keys.Add(role.Key.Value));
            Assert.True(scrolls.Add(role.Scroll.Uuid));
            Assert.True(enchantments.Add(role.Enchantment.Uuid));
            if (!role.Recipe.HasValue) continue;
            Assert.True(recipes.Add(role.Recipe.Value.Uuid));
            Assert.True(ranks.Add(role.CraftCostOrder));
        }

        Assert.Equal(6, recipes.Count);
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, ranks.OrderBy(rank => rank));
        Assert.Equal(
            windows.Roles.AsSpan().ToArray(),
            mac.Roles.AsSpan().ToArray());
    }

    [Fact]
    public void UnknownBaselineHasNoFallbackIdentityProfile()
    {
        Assert.False(new AutoScribeIdentityCatalog().TryGetProfile(
            "future-unknown-build",
            out _));
    }

    [Fact]
    public void NoneSelectsNoRoleAndUnknownOrDuplicateKeysAreIgnored()
    {
        var roles = AutoScribeIdentityCatalog.Audited.Roles;

        var none = AutoScribeRoleSelection.ParsePublication(" NONE ", roles);
        var selected = AutoScribeRoleSelection.ParsePublication(
            "unknown,scribe.power,scribe.advancement,scribe.power",
            roles);

        Assert.NotNull(none);
        Assert.Equal(0, none!.Count);
        Assert.NotNull(selected);
        Assert.Equal(2, selected!.Count);
        Assert.Equal("scribe.advancement", selected[0].Value);
        Assert.Equal("scribe.power", selected[1].Value);
    }

    [Fact]
    public void WorkerStatePinsRoleNarrowingForOneConfigurationGeneration()
    {
        var profile = AutoScribeIdentityCatalog.Audited;
        var state = AutoScribeCycleState.Create(new LifecycleGeneration(1));
        state.PinRoles(new ConfigGeneration(1), "scribe.power", profile);
        state.ObserveSelection(craftCostOrder: 3);
        state.PinRoles(new ConfigGeneration(1), "scribe.learning", profile);

        Assert.True(AutoScribeRoleSelection.Contains(
            state.EnabledRoles,
            new ScrollRoleKey("scribe.power")));
        Assert.False(AutoScribeRoleSelection.Contains(
            state.EnabledRoles,
            new ScrollRoleKey("scribe.learning")));
        Assert.Equal(3, state.LastSelectedCraftCostOrder);

        state.PinRoles(new ConfigGeneration(2), "scribe.learning", profile);

        Assert.False(AutoScribeRoleSelection.Contains(
            state.EnabledRoles,
            new ScrollRoleKey("scribe.power")));
        Assert.True(AutoScribeRoleSelection.Contains(
            state.EnabledRoles,
            new ScrollRoleKey("scribe.learning")));
        Assert.Equal(-1, state.LastSelectedCraftCostOrder);
    }

    [Fact]
    public void ActionCarriesOnlyTheCyclePinnedNativeRelationAndFreshnessEpoch()
    {
        Assert.Equal(
            new[] { "CollectedAtEpoch", "CollectedAtFrame", "Level", "RecipeId", "ScrollId" },
            typeof(AutoScribeCycleAction)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal));
    }
}
