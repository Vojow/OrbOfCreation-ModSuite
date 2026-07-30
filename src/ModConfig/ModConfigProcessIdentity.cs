namespace OrbModConfig;

internal static class ModConfigProcessIdentity
{
    internal static int CaptureCurrentProcessId()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        return process.Id;
    }
}
