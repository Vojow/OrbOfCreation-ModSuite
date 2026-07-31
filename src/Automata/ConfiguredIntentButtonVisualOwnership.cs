using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace OrbAutomata;

/// <summary>
/// Gives a suite-rendered configured-intent control exclusive ownership of its visuals.
/// </summary>
/// <remarks>
/// Suite controls are cloned from native buttons or created with native frames. Their
/// <see cref="Selectable"/> still owns a target graphic unless the suite explicitly releases it.
/// Pointer hover, press, release, and selection can therefore repaint that graphic independently
/// of the suite's configured-intent renderer. The image remains a raycast target; removing it from
/// the selectable transition is what removes the second visual writer.
/// </remarks>
internal static class ConfiguredIntentButtonVisualOwnership
{
    internal static void Claim(Button button) => Claim(button, effectsType: null);

    internal static void Claim(Button button, Type? effectsType)
    {
        if (button is null) throw new ArgumentNullException(nameof(button));
        button.targetGraphic = null;
        if (effectsType is null || button.gameObject is null) return;
        foreach (var component in button.gameObject.GetComponents<Component>()
                     .Where(component => effectsType.IsInstanceOfType(component)))
        {
            if (component is Behaviour behaviour) behaviour.enabled = false;
            UnityEngine.Object.Destroy(component);
        }
    }
}
