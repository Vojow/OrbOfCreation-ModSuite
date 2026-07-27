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
        "Usage: OrbModding.ServiceCycleTrace [--full|--journal|--performance|--dashboard] " +
        "--input <artifact-or-session> [--output <report.md>]");
    return 2;
}

try
{
    var inputPath = Path.GetFullPath(options.InputPath);
#if SERVICE_CYCLE_PROFILE
    if (options.InputKind == TraceInputKind.Dashboard)
    {
        var selection = Locate(inputPath);
        TraceDashboardWriter.Write(selection, Path.GetFullPath(options.OutputPath!));
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
    else
    {
        var session = ManualFullTraceSessionReader.Read(Locate(inputPath).FullSessionDirectory);
        if (options.OutputPath is null)
            ManualFullTraceReport.Write(Console.Out, session);
        else
        {
            session.EnsureSafeReportPath(options.OutputPath);
            AtomicTextFile.Write(options.OutputPath, writer => ManualFullTraceReport.Write(writer, session));
        }
    }
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

// Reported rather than silent: when the caller named a trace root or a run folder, which capture
// the report describes is part of reading it.
static TraceCaptureSelection Locate(string inputPath)
{
    var selection = TraceCaptureLocator.Locate(inputPath);
    foreach (var note in selection.Notes) Console.Error.WriteLine(note);
    return selection;
}
