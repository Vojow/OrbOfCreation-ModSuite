namespace OrbModding.Common.Runtime.Verification;

public interface IDifferentialVerificationControl
{
    bool RunRequested { get; }

    long Revision { get; }

    bool RequestRun();
}
