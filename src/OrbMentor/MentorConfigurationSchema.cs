using System;
using OrbModding.Common;

namespace OrbMentor;

internal static class MentorConfigurationSchema
{
    internal static ConfigurationSchemaPlan Plan { get; } = new(1, new[]
    {
        new ConfigurationMigrationStep(0, 1, Array.Empty<ConfigurationKey>(), _ => { }),
    });
}
