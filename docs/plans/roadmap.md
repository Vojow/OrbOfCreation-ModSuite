# Project roadmap

> **Lifecycle: Active.** This file lists future product work only. Current behavior is documented by each module and the runtime architecture dossier.

[Back to plans](README.md) · [Project overview](../../README.md)

What the suite already does is the [runtime architecture dossier](../runtime-architecture/README.md)'s
to state, not this file's.

## What remains

1. Compare Auto Buy, Auto Harvest, and Mentor traces to identify measured runtime costs, and act only on material findings.
2. Build the strategist: a service that publishes a real `SuiteStrategy` bulletin instead of the neutral constant every consumer reads today, so per-resource, time-varying policy replaces per-feature thresholds.

## Completed

- Mentor is an ordinary ServiceCycle service. Its legacy engine,
  legacy native-contract surface, operations-per-frame and CPU-budget settings,
  shared performance coordinator, and coordinator evidence product are retired.
- Combined-suite runtime and package validation completed and the reviewed
  0.5.0 release prepared.

New automation features should use the shared lifecycle, ownership, diagnostics, and ServiceCycle contracts instead of introducing another scheduler.
