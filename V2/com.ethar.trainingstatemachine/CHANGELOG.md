# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added

- `TrainingScenarioValidator` + `TrainingValidationReport` — the chain-rule
  checks (unreachable / early-completion / dead-end + expected-class queue)
  as a reusable core report. The `TrainingScenarioAsset` inspector now runs
  this shared validator instead of its own duplicate; the Python export's
  `validate_scenario` is the behavioural twin (used by the builder CLI).
- **Authoring tooling consolidated into the Python export** (one tool by
  design): mermaid-diagram ⇄ configuration and CSV import live in
  `com.ethar.trainingstatemachine.python` (`builder.py` CLI), with scenario
  JSON as the interchange this package parses — see
  `Documentation/Training-Builder.md`.
- **`Tests~/` headless test runner** — a ~30-line csproj (no logic) that
  compiles Runtime + EditMode test sources and runs the full NUnit suite via
  `dotnet test Tests~`, so the package verifies without Unity (CI-ready).
  The `~` folder is invisible to the Unity importer.

- `TrainingStep.ModelRef` (+ `HasModel`, wire key `modelRef`, additive and
  optional): a host-resolved model catalog key spawned aligned to the step's
  physical marker (e.g. AprilTag) by the host's placement layer. Carried
  through `TrainingStepData`, the parser and the authoring mirrors.
- `TrainingClassSource.AprilTag`: on-device fiducial sightings bridged into
  the class pipeline. Deterministic — never gated by
  `MinimumDetectionConfidence`.
- Mirrored 1:1 in the Python port (`model_ref`, `has_model`, `APRIL_TAG`).

## [1.0.0] - 2026-07-12

### Added

- Initial release, extracted from `com.questvisionstream.unity`:
  - `TrainingStateMachine` — the training flow state queue with confidence
    gating, class processing (`ProcessClass`), form action feedback
    (`CompleteStep`) and events, using only basic C# types.
  - `TrainingStateMachineConfig` / `TrainingScenarioData` / `TrainingStepData`
    serializable configuration structs.
  - `TrainingScenario` / `TrainingStep` immutable runtime model.
  - `TrainingScenarioParser` JSON parse/serialize for scenarios and configs.
  - `TrainingScenarioLibrary` built-in Ethar demo scenario.
  - EditMode unit tests covering valid, ignore and result processing.
