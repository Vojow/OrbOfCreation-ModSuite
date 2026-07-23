namespace OrbAutomata;

internal interface IAutomataConfigurationSource
{
    AutomataConfiguration Current { get; }
}

internal interface IAutomataConfigurationEditor : IAutomataConfigurationSource
{
    void ToggleAutoBuy();
    void ToggleAutoCast();
    void ToggleAutoConcept();
}
