namespace OrbModding.ServiceCycleTrace;

internal enum TraceInputKind
{
    Replay,
    ManualFullTrace,
    DecisionJournal,
#if SERVICE_CYCLE_PROFILE
    PerformanceProfile,
    Dashboard,
#endif
}

internal sealed class TraceCommandLine
{
    private TraceCommandLine(
        TraceInputKind inputKind,
        string inputPath,
        string? outputPath,
        ServiceCycleTraceProfile profile)
    {
        InputKind = inputKind;
        InputPath = inputPath;
        OutputPath = outputPath;
        Profile = profile;
    }

    internal TraceInputKind InputKind { get; }
    internal string InputPath { get; }
    internal string? OutputPath { get; }
    internal ServiceCycleTraceProfile Profile { get; }

    internal static bool TryParse(string[] args, out TraceCommandLine options)
    {
        string? input = null;
        string? output = null;
        var inputKind = TraceInputKind.Replay;
        var inputKindSet = false;
        var profile = ServiceCycleTraceProfile.Generic;
        var profileSet = false;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--full" when !inputKindSet:
                    inputKindSet = true;
                    inputKind = TraceInputKind.ManualFullTrace;
                    break;
                case "--journal" when !inputKindSet:
                    inputKindSet = true;
                    inputKind = TraceInputKind.DecisionJournal;
                    break;
#if SERVICE_CYCLE_PROFILE
                case "--performance" when !inputKindSet:
                    inputKindSet = true;
                    inputKind = TraceInputKind.PerformanceProfile;
                    break;
                case "--dashboard" when !inputKindSet:
                    inputKindSet = true;
                    inputKind = TraceInputKind.Dashboard;
                    break;
#endif
                case "--input" when input is null && TryTakeValue(args, ref index, out input):
                    break;
                case "--output" when output is null && TryTakeValue(args, ref index, out output):
                    break;
                case "--profile" when !profileSet &&
                    TryTakeValue(args, ref index, out var profileValue) &&
                    TryParseProfile(profileValue, out profile):
                    profileSet = true;
                    break;
                default:
                    options = null!;
                    return false;
            }
        }
        var dashboardWithoutOutput = false;
#if SERVICE_CYCLE_PROFILE
        dashboardWithoutOutput = inputKind == TraceInputKind.Dashboard && output is null;
#endif
        if (string.IsNullOrWhiteSpace(input) || output is not null && string.IsNullOrWhiteSpace(output) ||
            inputKind != TraceInputKind.Replay && profileSet || dashboardWithoutOutput)
        {
            options = null!;
            return false;
        }
        options = new TraceCommandLine(inputKind, input, output, profile);
        return true;
    }

    private static bool TryTakeValue(string[] args, ref int index, out string value)
    {
        index++;
        if (index >= args.Length)
        {
            value = string.Empty;
            return false;
        }
        value = args[index];
        return true;
    }

    private static bool TryParseProfile(string value, out ServiceCycleTraceProfile profile)
    {
        switch (value)
        {
            case "generic":
                profile = ServiceCycleTraceProfile.Generic;
                return true;
            case "auto-harvest":
                profile = ServiceCycleTraceProfile.AutoHarvest;
                return true;
            default:
                profile = default;
                return false;
        }
    }
}
