# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added

- **The training builder** (shared mermaid dialect — see
  `Documentation/Training-Builder.md`):
  - `TrainingMermaidBuilder` — parse a markdown+mermaid document into a
    scenario (`TryParseMarkdown`, with `%% minimumDetectionConfidence`
    support) and generate the document back (`ToMarkdown`, diagram +
    verification table; lossless config→md→config round-trip).
  - `TrainingCsvBuilder` — spreadsheet CSV import (flexible headers,
    `;`-separated options, RFC-4180 quoting).
  - `TrainingScenarioValidator` + `TrainingValidationReport` — the chain-rule
    checks (unreachable / early-completion / dead-end + expected-class queue)
    as a reusable core report, shared by the builder and the Unity inspector.
  - Mirrored 1:1 in the Python port (`mermaid.py`, `csv_io.py`,
    `validation.py`, `builder.py` CLI).

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
