# Project roadmap

> **Lifecycle: Active.** This file lists future product work only. Current behavior is documented by each module and the runtime architecture dossier.

[Back to plans](README.md) · [Project overview](../../README.md)

What the suite already does is the [runtime architecture dossier](../runtime-architecture/README.md)'s
to state, not this file's.

## What remains

1. Migrate Mentor, the remaining legacy feature, onto ServiceCycle. Retire its declared legacy native surface and any CPU-budget machinery left with no live consumer; Auto Concept's two work identities and profile rules are already gone.
2. Compare Auto Buy and Auto Harvest traces to identify measured runtime costs, and act only on material findings.
3. Complete combined-suite runtime and package validation, then prepare a reviewed beta release.
4. Build the strategist: a service that publishes a real `SuiteStrategy` bulletin instead of the neutral constant every consumer reads today, so per-resource, time-varying policy replaces per-feature thresholds.

## Later modules

- [Orb Insights](insights.md) may add read-only gameplay explanations and diagnostics.
- [Orb Toolbox](toolbox.md) may add explicit, reversible advanced player operations.

New automation features should use the shared lifecycle, ownership, diagnostics, and ServiceCycle contracts instead of introducing another scheduler.
