# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

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
