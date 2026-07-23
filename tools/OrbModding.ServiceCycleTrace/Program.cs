using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.ServiceCycleTrace;
using OrbModding.ServiceCycleTrace.IO;
using OrbModding.ServiceCycleTrace.Journal;
using OrbModding.ServiceCycleTrace.ManualTrace;
#if SERVICE_CYCLE_PROFILE
using OrbModding.ServiceCycleTrace.Dashboard;
using OrbModding.ServiceCycleTrace.Performance;
#endif

if (!TraceCommandLine.TryParse(args, out var options))
{
    Console.Error.WriteLine(
        "Usage: OrbModding.ServiceCycleTrace [--full|--journal|--performance|--dashboard] --input <artifact-or-session> " +
        "[--profile generic|auto-harvest] [--output <report.md>]");
    return 2;
}

try
{
    var inputPath = Path.GetFullPath(options.InputPath);
#if SERVICE_CYCLE_PROFILE
    if (options.InputKind == TraceInputKind.Dashboard)
    {
        TraceDashboardWriter.Write(inputPath, Path.GetFullPath(options.OutputPath!));
    }
    else if (options.InputKind == TraceInputKind.PerformanceProfile)
    {
        var session = ServiceCycleProfileSessionReader.Read(inputPath);
        if (options.OutputPath is null)
            ServiceCycleProfileReport.Write(Console.Out, session);
        else
            AtomicTextFile.Write(options.OutputPath, writer => ServiceCycleProfileReport.Write(writer, session));
    }
    else
#endif
    if (options.InputKind == TraceInputKind.DecisionJournal)
    {
        using var report = DecisionJournalReportReader.Read(inputPath);
        if (options.OutputPath is null)
            DecisionJournalReport.Write(Console.Out, report);
        else
        {
            report.EnsureSafeReportPath(options.OutputPath);
            AtomicTextFile.Write(options.OutputPath, writer => DecisionJournalReport.Write(writer, report));
        }
    }
    else if (options.InputKind == TraceInputKind.ManualFullTrace)
    {
        var session = ManualFullTraceSessionReader.Read(inputPath);
        if (options.OutputPath is null)
            ManualFullTraceReport.Write(Console.Out, session);
        else
        {
            session.EnsureSafeReportPath(options.OutputPath);
            AtomicTextFile.Write(options.OutputPath, writer => ManualFullTraceReport.Write(writer, session));
        }
    }
    else
    {
        var artifact = ServiceCycleReplayArtifactCodec.Decode(BoundedBinaryFile.Read(
            inputPath,
            0,
            ServiceCycleReplayArtifactFormat.MaximumArtifactBytes));
        var report = ServiceCycleTraceReport.Render(Path.GetFileName(inputPath), artifact, options.Profile);
        if (options.OutputPath is null) Console.Write(report);
        else AtomicTextFile.Write(options.OutputPath, writer => writer.Write(report));
    }
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}
