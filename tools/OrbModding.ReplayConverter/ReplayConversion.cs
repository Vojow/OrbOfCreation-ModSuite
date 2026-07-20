using System.Text;
using OrbModding.RuntimeReplay;

namespace OrbModding.ReplayConverter;

public static class ReplayConversion
{
    public static void Convert(string setupPath, string observationsPath, string outputPath, string replayId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(setupPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(observationsPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(replayId);

        var setup = ReplayJsonCodec.ParseSetup(File.ReadAllText(setupPath, Encoding.UTF8));
        var events = new List<ReplayEvent>();
        var lineNumber = 0;
        foreach (var line in File.ReadLines(observationsPath, Encoding.UTF8))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                events.Add(ReplayJsonCodec.ParseEvent(line));
            }
            catch (ReplayFormatException exception)
            {
                throw new ReplayFormatException($"Observation line {lineNumber}: {exception.Message}");
            }
        }

        var replay = new OrbModding.RuntimeReplay.RuntimeReplay(
            OrbModding.RuntimeReplay.RuntimeReplay.SchemaIdentifier,
            OrbModding.RuntimeReplay.RuntimeReplay.CurrentSchemaVersion,
            replayId,
            setup,
            events.AsReadOnly());
        var canonicalJson = ReplayJsonCodec.Write(replay);

        var fullOutputPath = Path.GetFullPath(outputPath);
        var outputDirectory = Path.GetDirectoryName(fullOutputPath);
        if (string.IsNullOrEmpty(outputDirectory))
            throw new InvalidOperationException("The output path has no parent directory.");
        Directory.CreateDirectory(outputDirectory);
        var temporaryPath = Path.Combine(
            outputDirectory,
            "." + Path.GetFileName(fullOutputPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temporaryPath, canonicalJson, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, fullOutputPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
