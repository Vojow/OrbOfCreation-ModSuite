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
    /// It rides on a real policy value that the generic ServiceCycle framework does not interpret.
    /// The test services recording the number therefore say which snapshot reached them and nothing
    /// else.
    /// </remarks>
    internal static SuiteRuntimeConfiguration WithSetting(int setting) => new()
    {
        Reserves = new OrbAutomata.AutomataReserveConfiguration
        {
            AbsoluteReserve = setting.ToString(System.Globalization.CultureInfo.InvariantCulture),
        },
    };

    /// <summary>Reads back what <see cref="WithSetting"/> put in.</summary>
    internal static int SettingOf(SuiteRuntimeConfiguration configuration) =>
        int.TryParse(
            configuration.Reserves.AbsoluteReserve,
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out var setting)
            ? setting
            : 0;

    /// <summary>What the suite says about the emergency stop.</summary>
    internal static SuiteRuntimeConfiguration WithEmergencyDisable(bool disable) => new()
    {
        Safety = new SuiteSafetyConfiguration { EmergencyDisable = disable },
    };
}
