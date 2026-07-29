#if SERVICE_CYCLE_PROFILE
using System.Text.Json;
using System.Text.Json.Serialization;
using OrbModding.ServiceCycleTrace.IO;

namespace OrbModding.ServiceCycleTrace.Dashboard;

internal static class TraceDashboardWriter
{
    private const string DataMarker = "__TRACE_DASHBOARD_JSON__";
    private const string ScriptMarker = "__TRACE_DASHBOARD_VENDOR_JS__";

    /// <summary>
    /// The charting libraries, inlined rather than linked. A dashboard is shared and read offline,
    /// so a page that fetches its own renderer is a page that renders nothing on the machine it was
    /// sent to. Neither file contains a closing script tag, so neither can end the block it lands in.
    /// </summary>
    private static readonly string[] VendorScripts =
    {
        "chart.umd.min.js",
        "chartjs-plugin-zoom.min.js",
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal static void Write(TraceCaptureSelection selection, string outputPath)
    {
        var htmlPath = Path.GetFullPath(outputPath);
        if (!string.Equals(Path.GetExtension(htmlPath), ".html", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The dashboard output must be an HTML file.", nameof(outputPath));
        var jsonPath = Path.ChangeExtension(htmlPath, ".json");
        var document = TraceDashboardReader.Read(selection);
        var json = JsonSerializer.Serialize(document, JsonOptions);
        var templatePath = Path.Combine(AppContext.BaseDirectory, "trace-dashboard.html");
        var template = File.ReadAllText(templatePath);
        if (template.IndexOf(DataMarker, StringComparison.Ordinal) < 0)
            throw new InvalidDataException("The trace dashboard template has no data marker.");
        if (template.IndexOf(ScriptMarker, StringComparison.Ordinal) < 0)
            throw new InvalidDataException("The trace dashboard template has no vendor script marker.");
        var html = template
            .Replace(ScriptMarker, ReadVendorScripts(), StringComparison.Ordinal)
            .Replace(DataMarker, json, StringComparison.Ordinal);
        AtomicTextFile.Write(jsonPath, writer => writer.Write(json));
        AtomicTextFile.Write(htmlPath, writer => writer.Write(html));
    }

    private static string ReadVendorScripts()
    {
        var builder = new System.Text.StringBuilder();
        foreach (var name in VendorScripts)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "dashboard-vendor", name);
            if (!File.Exists(path))
                throw new InvalidDataException($"The vendored dashboard script '{name}' is missing.");
            builder.Append("<script>").Append(File.ReadAllText(path)).AppendLine("</script>");
        }
        return builder.ToString();
    }
}
#endif
