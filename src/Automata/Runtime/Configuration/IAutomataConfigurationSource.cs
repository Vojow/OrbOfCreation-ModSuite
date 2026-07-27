using OrbModding.Common.Runtime.Configuration;

namespace OrbAutomata;

internal interface IAutomataConfigurationSource
{
    SuiteRuntimeConfiguration Current { get; }
}

internal interface IAutomataConfigurationEditor : IAutomataConfigurationSource
{
    void ToggleAutoBuy();

    /// <summary>
    /// Turns Auto Buy off through the same setting the toggle writes, whatever it was before. A
    /// toggle cannot express this: the caller that stands the service down means "off", and calling
    /// the toggle from an already-off state would turn it on.
    /// </summary>
    void DisableAutoBuy();

    void ToggleAutoCast();
    void ToggleAutoConcept();
}
