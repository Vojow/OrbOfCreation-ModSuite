namespace OrbAutomata;

internal static class GameMcpActionRegistrationPolicy
{
    internal static bool ShouldCompose(bool runtimeActivationAllowed, bool automationEnabled)
    {
        _ = automationEnabled;
        return runtimeActivationAllowed;
    }
}
