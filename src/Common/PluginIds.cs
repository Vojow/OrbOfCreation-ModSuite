namespace OrbModding.Common;

public static class PluginIds
{
    public const string Author = "Vojow";

    /// <summary>
    /// One assembly, one loader identity, one config file. Deliberately a new GUID: the three old
    /// ones retire with the three DLLs, and BepInEx derives the configuration file name from the
    /// GUID, so this is the clean break the campaign chose. No migration from the retired files.
    /// </summary>
    public const string SuiteGuid = "dev.vojow.orbofcreation.modsuite";

    public const string SuiteName = "Orb Of Creation ModSuite";

    public const string Version = "0.4.0";
}
