using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests.Services.AutoScribe;

public sealed class AutoScribeIdentityCatalogTests
{
    [Fact]
    public void AuditedProfileExposesSemanticRolesWithoutDuplicateNativeIdentities()
    {
        var catalog = new AutoScribeIdentityCatalog();

        Assert.True(catalog.TryGetProfile(
            GameAssemblyAudit.WindowsV1052BaselineId,
            out var profile));
        Assert.Equal(8, profile.Roles.Count);

        var keys = new HashSet<string>(System.StringComparer.Ordinal);
        var scrolls = new HashSet<System.Guid>();
        var enchantments = new HashSet<System.Guid>();
        var craftOrders = new HashSet<int>();
        var producible = 0;
        for (var index = 0; index < profile.Roles.Count; index++)
        {
            var role = profile.Roles[index];
            if (role.IsProducible)
            {
                producible++;
                Assert.True(craftOrders.Add(role.CraftCostOrder));
            }
            Assert.StartsWith("scribe.", role.Key.Value);
            Assert.True(keys.Add(role.Key.Value));
            Assert.True(scrolls.Add(role.Scroll.Uuid));
            Assert.True(enchantments.Add(role.Enchantment.Uuid));
        }
        Assert.Equal(6, producible);
        Assert.Equal(6, craftOrders.Count);
        for (var order = 0; order < 6; order++)
            Assert.Contains(order, craftOrders);
        AssertCostOrder(profile, "scribe.advancement", 0);
        AssertCostOrder(profile, "scribe.power", 1);
        AssertCostOrder(profile, "scribe.learning", 2);
        AssertCostOrder(profile, "scribe.excellence", 3);
    }

    [Fact]
    public void UnknownBaselineHasNoFallbackUuidProfile()
    {
        var catalog = new AutoScribeIdentityCatalog();

        Assert.False(catalog.TryGetProfile("future-unknown-build", out _));
    }

    private static void AssertCostOrder(
        AutoScribeIdentityProfile profile,
        string key,
        int expected)
    {
        Assert.True(profile.TryFind(new ScrollRoleKey(key), out var role));
        Assert.Equal(expected, role.CraftCostOrder);
    }
}
