using OrbModding.Common.Runtime.Configuration;

namespace OrbModding.Tests.Runtime.ServiceCycle.TestSupport;

/// <summary>
/// Suite configuration snapshots for tests, built the way the plugin builds them.
/// </summary>
/// <remarks>
/// The suite has one configuration type and every registry starts on its all-defaults snapshot, so a
/// test that does not care about settings publishes nothing at all. These are for the tests that do:
/// either which snapshot a callback was handed, or what the suite says about the stop.
/// </remarks>
internal static class TestSuiteConfiguration
{
    /// <summary>The all-defaults snapshot a registry starts on.</summary>
    internal static SuiteRuntimeConfiguration Default => SuiteRuntimeConfigurationDefaults.Empty;

    /// <summary>A snapshot a test can tell apart from another by the number it carries.</summary>
    /// <remarks>
    /// It rides on a real setting — the logged-rejection limit — that no ServiceCycle code reads, so
    /// varying it cannot change what the runtime does. That is the point: a service recording the
    /// number it was handed says which snapshot reached it and nothing else.
    /// </remarks>
    internal static SuiteRuntimeConfiguration WithSetting(int setting) => new()
    {
        Diagnostics = new SuiteDiagnosticsConfiguration { MaxLoggedRejections = setting },
    };

    /// <summary>Reads back what <see cref="WithSetting"/> put in.</summary>
    internal static int SettingOf(SuiteRuntimeConfiguration configuration) =>
        configuration.Diagnostics.MaxLoggedRejections;

    /// <summary>What the suite says about the emergency stop.</summary>
    internal static SuiteRuntimeConfiguration WithEmergencyDisable(bool disable) => new()
    {
        Safety = new SuiteSafetyConfiguration { EmergencyDisable = disable },
    };
}
