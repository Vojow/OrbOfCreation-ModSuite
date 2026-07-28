using System;
using UnityEngine.UI;

namespace OrbAutomata;

/// <summary>
/// Gives a suite-rendered configured-intent control exclusive ownership of its visuals.
/// </summary>
/// <remarks>
/// The controls are cloned from the game's native toggle, whose <see cref="Selectable"/>
/// still owns a target graphic after the native toggle component and view bindings are removed.
/// Pointer hover, press, release, and selection can therefore repaint that graphic independently
/// of the suite's configured-intent renderer. The image remains a raycast target; removing it from
/// the selectable transition is what removes the second visual writer.
/// </remarks>
internal static class ConfiguredIntentButtonVisualOwnership
{
    internal static void Claim(Button button)
    {
        if (button is null) throw new ArgumentNullException(nameof(button));
        button.targetGraphic = null;
    }
}
