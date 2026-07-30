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
        Assert.Equal(8, profile.Roles.Length);
        Assert.Equal(6, System.Array.FindAll(
            profile.Roles,
            static role => role.IsProducible).Length);

        var keys = new HashSet<string>(System.StringComparer.Ordinal);
        var scrolls = new HashSet<System.Guid>();
        var enchantments = new HashSet<System.Guid>();
        foreach (var role in profile.Roles)
        {
            Assert.StartsWith("scribe.", role.Key.Value);
            Assert.True(keys.Add(role.Key.Value));
            Assert.True(scrolls.Add(role.Scroll.Uuid));
            Assert.True(enchantments.Add(role.Enchantment.Uuid));
        }
    }

    [Fact]
    public void UnknownBaselineHasNoFallbackUuidProfile()
    {
        var catalog = new AutoScribeIdentityCatalog();

        Assert.False(catalog.TryGetProfile("future-unknown-build", out _));
    }
}
