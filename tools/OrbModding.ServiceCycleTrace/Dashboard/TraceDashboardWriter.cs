#if SERVICE_CYCLE_PROFILE
using System.Text.Json;
using System.Text.Json.Serialization;
using OrbModding.ServiceCycleTrace.IO;

namespace OrbModding.ServiceCycleTrace.Dashboard;

internal static class TraceDashboardWriter
{
    private const string DataMarker = "__TRACE_DASHBOARD_JSON__";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal static void Write(string captureDirectory, string outputPath)
    {
        var htmlPath = Path.GetFullPath(outputPath);
        if (!string.Equals(Path.GetExtension(htmlPath), ".html", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The dashboard output must be an HTML file.", nameof(outputPath));
        var jsonPath = Path.ChangeExtension(htmlPath, ".json");
        var document = TraceDashboardReader.Read(captureDirectory);
        var json = JsonSerializer.Serialize(document, JsonOptions);
        var templatePath = Path.Combine(AppContext.BaseDirectory, "trace-dashboard.html");
        var template = File.ReadAllText(templatePath);
        if (template.IndexOf(DataMarker, StringComparison.Ordinal) < 0)
            throw new InvalidDataException("The trace dashboard template has no data marker.");
        var html = template.Replace(DataMarker, json, StringComparison.Ordinal);
        AtomicTextFile.Write(jsonPath, writer => writer.Write(json));
        AtomicTextFile.Write(htmlPath, writer => writer.Write(html));
    }
}
#endif
