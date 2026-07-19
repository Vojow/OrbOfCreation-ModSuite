using OrbModding.PerformanceEvidence;

if (args.Length < 4 || args[0] != "--profile" || args[2] != "--evidence")
{
    Console.Error.WriteLine("Usage: OrbModding.PerformanceEvidence --profile <profile.json> --evidence <evidence.json> [--json-output <result.json>] [--markdown-output <result.md>]");
    return 2;
}

string? jsonOutput = null;
string? markdownOutput = null;
for (var index = 4; index < args.Length; index += 2)
{
    if (index + 1 >= args.Length)
    {
        Console.Error.WriteLine("Output switches require a path.");
        return 2;
    }

    switch (args[index])
    {
        case "--json-output": jsonOutput = args[index + 1]; break;
        case "--markdown-output": markdownOutput = args[index + 1]; break;
        default:
            Console.Error.WriteLine($"Unknown switch: {args[index]}");
            return 2;
    }
}

try
{
    var profile = PerformanceEvidencePipeline.ReadProfile(args[1]);
    var evidence = PerformanceEvidencePipeline.ReadEvidence(args[3]);
    var evaluation = PerformanceEvidencePipeline.Evaluate(profile, evidence);
    var json = PerformanceEvidencePipeline.WriteEvaluationJson(evaluation);
    var markdown = PerformanceEvidencePipeline.WriteEvaluationMarkdown(evaluation);
    if (jsonOutput is not null)
    {
        PerformanceEvidencePipeline.WriteAtomic(jsonOutput, json);
    }

    if (markdownOutput is not null)
    {
        PerformanceEvidencePipeline.WriteAtomic(markdownOutput, markdown);
    }

    var gate = PerformanceEvidencePipeline.EvaluateGate(profile, evaluation);
    var exceeded = evaluation.Results.Count(result => result.Classification == "exceeded");
    var insufficient = evaluation.Results.Count(result =>
        result.Classification == "insufficient-samples" || result.Classification == "insufficient-window");
    Console.WriteLine(
        $"Validated {evaluation.Results.Count} suite metrics; {exceeded} target exceedance(s), {insufficient} insufficient enforced result(s); gate={gate}.");
    return gate == PerformanceGateStatus.Passed ? 0 : 3;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1; // Invalid or incompatible input is distinct from target failure (3).
}
