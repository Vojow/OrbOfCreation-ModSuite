# Orb Insights plan

[Back to roadmap](roadmap.md)

## Goal

Turn the game's existing tooltip system into a precise inspection and explanation layer without replacing its visual language or creating a separate debug window.

## Interaction model

- Normal hover: native tooltip remains unchanged.
- Hold `Alt`: show advanced gameplay values.
- Hold `Ctrl+Alt`: show developer identity and runtime details.
- Optional configuration can make either layer always visible.

This avoids permanent tooltip clutter while making advanced information available exactly where the player is already looking.

## Architecture

Patch tooltip-building methods with Harmony postfixes and append native `TooltipNode` objects.

Verified candidate surfaces:

```text
TooltipableObject.GetTooltipNodes()
TooltipableObject.GetAltTooltipNodes()
ResourceSO.GetTooltipNodes()
ResourceSO.GetAltTooltipNodes()
ResourceSO.GetBaseTooltipNodes(bool useAlt)
```

Use a provider model:

```csharp
public interface ITooltipExtension
{
    bool Supports(ITooltipable target);
    IEnumerable<TooltipNode> BuildNodes(ITooltipable target, TooltipContext context);
}
```

Planned providers:

- `IdentityTooltipExtension`
- `ResourceTooltipExtension`
- `StructureTooltipExtension`
- `ResearchTooltipExtension`
- `SpellTooltipExtension`
- `AlchemyTooltipExtension`
- `AutomataDecisionTooltipExtension`

## Resource tooltip MVP

Advanced layer:

```text
Current quantity
True quantity
Lifetime quantity
Maximum and functional maximum
True gain/drain rate
Equilibrium
Overflow and soft-cap state
```

Developer layer:

```text
Runtime type
UUID
Visible / available / discovered
Resource types
Raw BigDouble mantissa and exponent
Observable identifiers where useful
```

All numbers should use the game's formatting by default, with an option to show exact scientific notation.

## Automation integration

Orb Automata should be able to register optional tooltip providers at runtime. If Automata is installed, tooltips may append:

```text
Automata: Eligible / Rejected / Deferred
Reason: reserve violation
Priority: 3
Next evaluation: 0.8 seconds
```

Insights must remain fully functional without Automata. Integration should use soft discovery or a minimal shared interface only after the need is proven.

## Patch safety

- Append nodes; never replace the original result.
- Do not mutate cached native tooltip collections in place unless ownership is confirmed.
- Catch provider exceptions independently so one extension cannot break all tooltips.
- Cache stable identity strings but calculate live quantities when the tooltip refreshes.
- Avoid reflection and allocations every frame after initial provider discovery.

## Future features

- Click or context action to copy UUID/value.
- Pin an inspected entity to an overlay.
- Show modifier sources and before/after calculations.
- Compare current and next-level values.
- Open the selected resource in Orb Toolbox if installed.

## Definition of done for v0.1

- Resource tooltips show exact advanced data while a modifier key is held.
- Native tooltip content and ordering remain intact.
- UUID and runtime type are correct for known test resources.
- No measurable tooltip-frame stutter in normal play.
- Missing or changed fields fail silently with a diagnostic log entry.
- Works with the supported project mod suite installed.
