using System;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

public sealed class EntityUuidTranslatorTests
{
    [Fact]
    public void EmbeddedCatalogCoversBothDatasetsAndRetainsExactIdentity()
    {
        Assert.Equal(2794, EntityUuidTranslator.Count);

        var formatted = EntityUuidTranslator.Format(KnownEntities.ScrollAdvancement.Uuid);
        Assert.Contains("Scroll of Advancement", formatted);
        Assert.Contains("ScrollAdvancement/ConsumableSO", formatted);
        Assert.Contains(KnownEntities.ScrollAdvancement.Uuid.ToString("D"), formatted);

        var unknown = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        Assert.Equal(unknown.ToString("D"), EntityUuidTranslator.Format(unknown));
    }
}
