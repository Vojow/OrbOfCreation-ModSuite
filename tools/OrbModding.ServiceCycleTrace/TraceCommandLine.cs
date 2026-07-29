namespace OrbModding.ServiceCycleTrace;

internal enum TraceInputKind
{
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
        string? outputPath)
    {
        InputKind = inputKind;
        InputPath = inputPath;
        OutputPath = outputPath;
    }

    internal TraceInputKind InputKind { get; }
    internal string InputPath { get; }
    internal string? OutputPath { get; }

    internal static bool TryParse(string[] args, out TraceCommandLine options)
    {
        string? input = null;
        string? output = null;
        var inputKind = TraceInputKind.ManualFullTrace;
        var inputKindSet = false;
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
            dashboardWithoutOutput)
        {
            options = null!;
            return false;
        }
        options = new TraceCommandLine(inputKind, input, output);
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
}
